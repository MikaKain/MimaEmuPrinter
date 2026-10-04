namespace MimaEmuPrinter.Core.Network
{
	using System;
	using System.Globalization;
	using System.Net;
	using System.Net.Sockets;
	using System.Threading;
	using System.Threading.Tasks;
	using MimaEmuPrinter.Core.Archive;
	using MimaEmuPrinter.Core.Logging;
	using MimaEmuPrinter.Core.Paper;
	using MimaEmuPrinter.Core.Rendering;
	using MimaEmuPrinter.Core.Status;

	/// <summary>
	/// The virtual printer: a raw TCP listener accepting one cash register connection at a time.
	/// A second connection is refused while the first one is open. Nothing here ever throws to the caller:
	/// a bind failure, a broken socket or an unknown frame is journaled and the process keeps running.
	/// </summary>
	public sealed class PrinterServer : IAsyncDisposable
	{
		private readonly Object gate = new Object();
		private readonly StatusEngine status;
		private readonly JournalService journal;
		private readonly ArchiveService archive;
		private readonly Func<PaperWidth> paperProvider;
		private readonly TimeProvider time;
		private readonly PrinterServerOptions options;

		private TcpListener? listener;
		private IPEndPoint? lastEndPoint;
		private CancellationTokenSource? cancellation;
		private Task? acceptTask;
		private Task? sessionTask;
		private PrinterSession? activeSession;
		private ListenerState state = ListenerState.Stopped;

		public PrinterServer(
			StatusEngine status,
			JournalService journal,
			ArchiveService archive,
			Func<PaperWidth> paperProvider,
			TimeProvider? time = null,
			PrinterServerOptions? options = null)
		{
			this.status = status;
			this.journal = journal;
			this.archive = archive;
			this.paperProvider = paperProvider;
			this.time = time ?? TimeProvider.System;
			this.options = options ?? new PrinterServerOptions();
		}

		/// <summary>Raised (on a worker thread) when the listener state changes.</summary>
		public event EventHandler? StateChanged;

		/// <summary>Raised (on a worker thread) when the ticket being printed changes or a job is closed.</summary>
		public event EventHandler<TicketUpdate>? TicketUpdated;

		public ListenerState State
		{
			get
			{
				lock (gate)
				{
					return state;
				}
			}
		}

		/// <summary>The address and port actually bound, or null when stopped.</summary>
		public IPEndPoint? LocalEndPoint
		{
			get
			{
				lock (gate)
				{
					return listener?.LocalEndpoint as IPEndPoint;
				}
			}
		}

		/// <summary>Starts listening. Returns false (and journals the reason) when the bind fails.</summary>
		public Boolean Start(IPAddress address, Int32 port)
		{
			lock (gate)
			{
				if (listener != null)
				{
					return true;
				}
			}

			TcpListener newListener;
			try
			{
				newListener = new TcpListener(address, port);
				if (OperatingSystem.IsWindows())
				{
					newListener.ExclusiveAddressUse = true;
				}

				newListener.Start();
			}
			catch (Exception ex) when (ex is SocketException || ex is ArgumentOutOfRangeException || ex is InvalidOperationException)
			{
				journal.Write(String.Format(CultureInfo.InvariantCulture, "Échec du bind sur {0}:{1} : {2}", address, port, ex.Message));
				return false;
			}

			IPEndPoint bound = (IPEndPoint)newListener.LocalEndpoint;
			CancellationTokenSource source = new CancellationTokenSource();
			lock (gate)
			{
				listener = newListener;
				lastEndPoint = bound;
				cancellation = source;
				acceptTask = Task.Run(() => AcceptLoopAsync(newListener, source.Token));
				state = ListenerState.Listening;
			}

			journal.Write(String.Format(CultureInfo.InvariantCulture, "Listener démarré sur {0}:{1} : bind réussi", bound.Address, bound.Port));
			RaiseStateChanged();
			return true;
		}

		/// <summary>Stops listening and closes the open connection, if any.</summary>
		public async Task StopAsync()
		{
			TcpListener? stopping;
			CancellationTokenSource? source;
			Task? accept;
			Task? session;
			lock (gate)
			{
				stopping = listener;
				source = cancellation;
				accept = acceptTask;
				session = sessionTask;
				listener = null;
				cancellation = null;
				acceptTask = null;
			}

			if (stopping == null || source == null)
			{
				return;
			}

			source.Cancel();
			stopping.Stop();
			try
			{
				if (accept != null)
				{
					await accept.ConfigureAwait(false);
				}

				if (session != null)
				{
					await session.ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				journal.Write("Erreur à l'arrêt du listener : " + ex.Message);
			}

			source.Dispose();
			lock (gate)
			{
				state = ListenerState.Stopped;
			}

			journal.Write(String.Format(
				CultureInfo.InvariantCulture,
				"Listener arrêté ({0}:{1})",
				lastEndPoint?.Address,
				lastEndPoint?.Port));
			RaiseStateChanged();
		}

		public async ValueTask DisposeAsync()
		{
			await StopAsync().ConfigureAwait(false);
		}

		private async Task AcceptLoopAsync(TcpListener tcpListener, CancellationToken token)
		{
			while (!token.IsCancellationRequested)
			{
				TcpClient incoming;
				try
				{
					incoming = await tcpListener.AcceptTcpClientAsync(token).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (ObjectDisposedException)
				{
					break;
				}
				catch (SocketException ex)
				{
					if (token.IsCancellationRequested)
					{
						break;
					}

					journal.Write("Erreur d'acceptation de connexion : " + ex.Message);
					await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
					continue;
				}

				try
				{
					HandleIncoming(incoming, token);
				}
				catch (Exception ex)
				{
					journal.Write("Erreur à l'ouverture de la connexion : " + ex.Message);
					incoming.Dispose();
				}
			}
		}

		private void HandleIncoming(TcpClient incoming, CancellationToken token)
		{
			PrinterSession? session = null;
			lock (gate)
			{
				if (activeSession == null)
				{
					session = new PrinterSession(
						incoming,
						status,
						journal,
						archive,
						paperProvider,
						time,
						options,
						update => TicketUpdated?.Invoke(this, update));
					activeSession = session;
					state = ListenerState.Busy;
				}
			}

			if (session == null)
			{
				RefuseConnection(incoming);
				return;
			}

			RaiseStateChanged();
			Task running = Task.Run(async () =>
			{
				try
				{
					await session.RunAsync(token).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					journal.Write("Erreur de session : " + ex.Message);
				}
				finally
				{
					lock (gate)
					{
						activeSession = null;
						if (listener != null)
						{
							state = ListenerState.Listening;
						}
					}

					RaiseStateChanged();
				}
			});

			lock (gate)
			{
				sessionTask = running;
			}
		}

		private void RefuseConnection(TcpClient incoming)
		{
			String source = incoming.Client.RemoteEndPoint?.ToString() ?? "?";
			try
			{
				incoming.LingerState = new LingerOption(true, 0);
				incoming.Close();
			}
			catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
			{
				// Already gone.
			}

			journal.Write("Connexion refusée depuis " + source + " : imprimante occupée");
		}

		private void RaiseStateChanged()
		{
			StateChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
