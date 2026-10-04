namespace MimaEmuPrinter.Core.Network
{
	using System;
	using System.Diagnostics;
	using System.Globalization;
	using System.IO;
	using System.Net;
	using System.Net.Sockets;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;
	using Archive;
	using Logging;
	using Paper;
	using Protocol;
	using Rendering;
	using Status;

	/// <summary>
	/// One cash register connection: reads the ESC/POS stream, answers the status requests at once,
	/// renders the ticket, closes the jobs (GS V, socket closed, 400 ms idle) and hands them to the archive.
	/// </summary>
	internal sealed class PrinterSession : IEscPosHandler
	{
		private const Int64 MaxRawBytes = 8 * 1024 * 1024;
		private const Int32 MaxBandLines = 12;
		private const Int32 DotsPerBandLine = 30;

		private readonly Object sync = new();
		private readonly Object writeLock = new();
		private readonly TcpClient client;
		private readonly NetworkStream stream;
		private readonly StatusEngine status;
		private readonly JournalService journal;
		private readonly ArchiveService archive;
		private readonly Func<PaperWidth> paperProvider;
		private readonly TimeProvider time;
		private readonly PrinterServerOptions options;
		private readonly Action<TicketUpdate> publish;
		private readonly EscPosParser parser;
		private readonly TicketBuilder builder = new();
		private readonly MemoryStream rawBuffer = new();
		private readonly String remoteAddress;
		private readonly String remoteEndpoint;

		private Job? job;
		private Byte asbValue;
		private Int32 ignoredCount;
		private Int64 totalBytes;
		private Int32 publishedVersion = -1;

		public PrinterSession(
			TcpClient client,
			StatusEngine status,
			JournalService journal,
			ArchiveService archive,
			Func<PaperWidth> paperProvider,
			TimeProvider time,
			PrinterServerOptions options,
			Action<TicketUpdate> publish)
		{
			this.client = client;
			this.status = status;
			this.journal = journal;
			this.archive = archive;
			this.paperProvider = paperProvider;
			this.time = time;
			this.options = options;
			this.publish = publish;

			stream = client.GetStream();
			client.SendTimeout = 2000;
			parser = new EscPosParser(this);
			builder.ColumnOverflow += OnColumnOverflow;

			var endPoint = client.Client.RemoteEndPoint as IPEndPoint;
			remoteAddress = endPoint?.Address.ToString() ?? "?";
			remoteEndpoint = endPoint?.ToString() ?? "?";
		}

		public async Task RunAsync(CancellationToken serverToken)
		{
			journal.Write("Connexion ouverte depuis " + remoteEndpoint);
			status.Changed += OnStatusChanged;

			var reason = ConnectionCloseReason.Peer;
			var connectionClock = Stopwatch.StartNew();
			var idleClock = Stopwatch.StartNew();
			var buffer = new Byte[4096];
			try
			{
				while (true)
				{
					var wait = options.InactivityTimeout - idleClock.Elapsed;
					var waitingForJobEnd = false;
					if (HasOpenJob() && options.JobIdleTimeout < wait)
					{
						wait = options.JobIdleTimeout;
						waitingForJobEnd = true;
					}

					if (wait < TimeSpan.Zero)
					{
						wait = TimeSpan.Zero;
					}

					Int32 read;
					using (var readSource = CancellationTokenSource.CreateLinkedTokenSource(serverToken))
					{
						readSource.CancelAfter(wait);
						try
						{
							read = await stream.ReadAsync(buffer.AsMemory(), readSource.Token).ConfigureAwait(false);
						}
						catch (OperationCanceledException) when (!serverToken.IsCancellationRequested)
						{
							if (waitingForJobEnd)
							{
								OnJobIdle();
								continue;
							}

							reason = ConnectionCloseReason.Timeout;
							break;
						}
					}

					if (read == 0)
					{
						break;
					}

					idleClock.Restart();
					Process(buffer, read);
				}
			}
			catch (OperationCanceledException)
			{
				reason = ConnectionCloseReason.Shutdown;
			}
			catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
			{
				reason = ConnectionCloseReason.Peer;
			}
			catch (Exception ex)
			{
				journal.Write("Erreur interne de connexion : " + ex.Message);
			}
			finally
			{
				status.Changed -= OnStatusChanged;
				Finish(reason, connectionClock.Elapsed);
			}
		}

		// ----- IEscPosHandler: print content -----

		public void PrintableByte(Byte value)
		{
			if (BeginPrint())
			{
				builder.AppendByte(value);
			}
		}

		public void LineFeed()
		{
			if (BeginPrint())
			{
				builder.LineFeed();
			}
		}

		public void CarriageReturn()
		{
			if (job != null && !job.Blocked)
			{
				builder.CarriageReturn();
			}
		}

		public void HorizontalTab()
		{
			if (BeginPrint())
			{
				builder.Tab();
			}
		}

		public void FeedLines(Int32 lines)
		{
			if (BeginPrint())
			{
				builder.FeedLines(lines);
			}
		}

		public void FeedDots(Int32 dots)
		{
			if (BeginPrint())
			{
				builder.FeedDots(dots);
			}
		}

		public void PrintBarcode(String data)
		{
			if (BeginPrint())
			{
				builder.AddNotice("[BAR] " + data);
			}
		}

		public void PrintQrCode(String data)
		{
			if (BeginPrint())
			{
				builder.AddNotice("[QR] " + data);
			}
		}

		public void PrintRaster(Int32 widthDots, Int32 heightDots)
		{
			if (BeginPrint())
			{
				Int32 bandLines = Math.Min(MaxBandLines, Math.Max(1, (heightDots + DotsPerBandLine - 1) / DotsPerBandLine));
				builder.AddBand(String.Format(CultureInfo.InvariantCulture, "logo {0} × {1}", widthDots, heightDots), bandLines);
			}
		}

		public void Cut()
		{
			if (job == null)
			{
				return;
			}

			if (!job.Blocked)
			{
				builder.AddCutMark();
			}

			CloseJob("GS V");
		}

		// ----- IEscPosHandler: style -----

		public void Initialize()
		{
			builder.Initialize();
		}

		public void SetPrintMode(Byte value)
		{
			builder.SetPrintMode(value);
		}

		public void SetCharacterSize(Byte value)
		{
			builder.SetCharacterSize(value);
		}

		public void SetAlignment(Byte value)
		{
			builder.SetAlignment(value);
		}

		public void SetEmphasis(Byte value)
		{
			builder.SetEmphasis(value);
		}

		public void SetUnderline(Byte value)
		{
			builder.SetUnderline(value);
		}

		public void SetCodePage(Byte value)
		{
			builder.SetCodePage(value);
		}

		public void PulseDrawer(Byte pin)
		{
			journal.Write("Pulse tiroir-caisse (ESC p) reçu, journalisé et non dessiné");
		}

		public void IgnoreCommand(String name)
		{
			ignoredCount++;
		}

		// ----- IEscPosHandler: real-time commands -----

		public void RealTime(StatusRequest request)
		{
			switch (request.Kind)
			{
				case StatusRequestKind.DleEot:
					AnswerDleEot(request.Value);
					break;
				case StatusRequestKind.DleEnq:
					journal.Write(String.Format(
						CultureInfo.InvariantCulture,
						"DLE ENQ {0} reçu : accepté, la panne n'est pas levée (pannes actives : {1})",
						request.Value,
						FaultRules.Describe(status.Effective)));
					break;
				case StatusRequestKind.GsR:
					AnswerGsR(request.Value);
					break;
				case StatusRequestKind.GsI:
					AnswerGsI(request.Value);
					break;
				case StatusRequestKind.GsA:
					asbValue = request.Value;
					journal.Write(request.Value == 0
						? "ASB désarmé (GS a 0)"
						: String.Format(CultureInfo.InvariantCulture, "ASB armé (GS a {0})", request.Value));
					break;
			}
		}

		private void AnswerDleEot(Byte n)
		{
			if (!status.TryGetRealTimeStatus(n, out Byte value))
			{
				ignoredCount++;
				journal.Write(String.Format(CultureInfo.InvariantCulture, "DLE EOT {0} non pris en charge, ignoré", n));
				return;
			}

			Write(new Byte[] { value });
			journal.Write(String.Format(CultureInfo.InvariantCulture, "Statut interrogé : DLE EOT {0}, octet renvoyé 0x{1:X2}", n, value));
		}

		private void AnswerGsR(Byte n)
		{
			Byte value;
			if (n == 1 || n == 49)
			{
				value = status.GetPaperSensorStatus();
			}
			else if (n == 2 || n == 50)
			{
				value = 0;
			}
			else
			{
				ignoredCount++;
				journal.Write(String.Format(CultureInfo.InvariantCulture, "GS r {0} non pris en charge, ignoré", n));
				return;
			}

			Write(new Byte[] { value });
			journal.Write(String.Format(CultureInfo.InvariantCulture, "Statut interrogé : GS r {0}, octet renvoyé 0x{1:X2}", n, value));
		}

		private void AnswerGsI(Byte n)
		{
			Byte[] answer;
			switch (n)
			{
				case 1:
				case 49:
					answer = new Byte[] { 0x20 };
					break;
				case 2:
				case 50:
					answer = new Byte[] { 0x00 };
					break;
				case 3:
				case 51:
					answer = new Byte[] { 0x01 };
					break;
				case 65:
					answer = BuildIdentityText("1.0");
					break;
				case 66:
					answer = BuildIdentityText("MimaCafe");
					break;
				case 67:
				case 68:
					answer = BuildIdentityText("MimaEmuPrinter");
					break;
				default:
					ignoredCount++;
					journal.Write(String.Format(CultureInfo.InvariantCulture, "GS I {0} non pris en charge, ignoré", n));
					return;
			}

			Write(answer);
			journal.Write(String.Format(CultureInfo.InvariantCulture, "Identité interrogée : GS I {0}, réponse {1}", n, FormatBytes(answer)));
		}

		private static Byte[] BuildIdentityText(String text)
		{
			Byte[] characters = Encoding.ASCII.GetBytes(text);
			Byte[] answer = new Byte[characters.Length + 2];
			answer[0] = 0x5F;
			Array.Copy(characters, 0, answer, 1, characters.Length);
			return answer;
		}

		// ----- Stream processing -----

		private Boolean HasOpenJob()
		{
			lock (sync)
			{
				return job != null;
			}
		}

		private void Process(Byte[] buffer, Int32 count)
		{
			lock (sync)
			{
				totalBytes += count;
				for (Int32 index = 0; index < count; index++)
				{
					if (rawBuffer.Length < MaxRawBytes)
					{
						rawBuffer.WriteByte(buffer[index]);
					}

					try
					{
						parser.Feed(buffer[index]);
					}
					catch (Exception ex)
					{
						parser.Reset();
						journal.Write("Séquence non traitée, ignorée : " + ex.Message);
					}
				}

				PublishIfChanged();
			}
		}

		private void OnJobIdle()
		{
			lock (sync)
			{
				if (job != null)
				{
					CloseJob("inactivité 400 ms");
				}
				else
				{
					rawBuffer.SetLength(0);
				}
			}
		}

		private void Finish(ConnectionCloseReason reason, TimeSpan duration)
		{
			lock (sync)
			{
				try
				{
					parser.Reset();
					CloseJob("fermeture de la connexion");
				}
				catch (Exception ex)
				{
					journal.Write("Erreur à la clôture du job : " + ex.Message);
				}
			}

			String reasonText;
			switch (reason)
			{
				case ConnectionCloseReason.Timeout:
					reasonText = "timeout";
					break;
				case ConnectionCloseReason.Shutdown:
					reasonText = "arrêt";
					break;
				default:
					reasonText = "caisse";
					break;
			}

			journal.Write(String.Format(
				CultureInfo.InvariantCulture,
				"Connexion fermée : durée {0:0.0} s, {1} octets reçus, motif : {2}",
				duration.TotalSeconds,
				totalBytes,
				reasonText));

			client.Dispose();
		}

		// ----- Jobs -----

		private Boolean BeginPrint()
		{
			if (job == null)
			{
				StartJob();
			}

			return !job!.Blocked;
		}

		private void StartJob()
		{
			DateTime now = time.GetLocalNow().DateTime;
			PaperWidth paper = paperProvider();
			PaperProfile profile = PaperProfile.For(paper);
			String? blockReason = FaultRules.GetBlockReason(status.Effective);

			job = new Job(archive.AllocateJobId(now), now, paper, profile)
			{
				Blocked = blockReason != null,
				Outcome = blockReason != null ? JobOutcome.NotPrinted : JobOutcome.Printed,
				Reason = blockReason,
			};
			builder.BeginTicket(profile);

			journal.Write(String.Format(
				CultureInfo.InvariantCulture,
				"Job {0} démarré : papier {1} mm, {2} colonnes{3}",
				job.Id,
				profile.WidthMm,
				profile.Columns,
				blockReason != null ? " (non imprimé : " + blockReason + ")" : String.Empty));
			Publish(false);
		}

		private void CloseJob(String trigger)
		{
			if (job == null)
			{
				return;
			}

			Job closing = job;
			job = null;

			Ticket ticket = builder.Snapshot();
			Int32 textLines = 0;
			foreach (TicketLine line in ticket.Lines)
			{
				if (line.Kind != TicketLineKind.Cut)
				{
					textLines++;
				}
			}

			PrinterFaults faults = status.Effective;
			Byte[] raw = rawBuffer.ToArray();
			rawBuffer.SetLength(0);
			Int32 ignored = ignoredCount;
			ignoredCount = 0;

			JobRecord record = new JobRecord
			{
				Id = closing.Id,
				StartedAt = closing.StartedAt,
				ClosedAt = time.GetLocalNow().DateTime,
				Source = remoteAddress,
				Paper = closing.Paper,
				Columns = closing.Profile.Columns,
				Outcome = closing.Outcome,
				Reason = closing.Reason,
				ByteCount = raw.Length,
				IgnoredCommands = ignored,
				ActiveFaults = FaultRules.Describe(faults),
				Lines = ticket.Lines,
			};

			journal.Write(String.Format(
				CultureInfo.InvariantCulture,
				"Job {0} clos ({1}) : {2} colonnes, {3} lignes, {4} octets, {5} commandes ignorées, événements actifs : {6}{7}",
				record.Id,
				trigger,
				record.Columns,
				textLines,
				record.ByteCount,
				record.IgnoredCommands,
				record.ActiveFaults,
				record.Notice != null ? " [" + record.Notice + "]" : String.Empty));

			publish(new TicketUpdate(closing.Id, ticket, closing.Paper, true, closing.Outcome, closing.Reason));
			_ = archive.ArchiveAsync(record, raw);
		}

		private void Publish(Boolean closed)
		{
			if (job == null)
			{
				return;
			}

			publishedVersion = builder.Version;
			publish(new TicketUpdate(job.Id, builder.Snapshot(), job.Paper, closed, job.Outcome, job.Reason));
		}

		private void PublishIfChanged()
		{
			if (job != null && builder.Version != publishedVersion)
			{
				Publish(false);
			}
		}

		// ----- Events -----

		private void OnStatusChanged(Object? sender, FaultsChangedEventArgs e)
		{
			lock (sync)
			{
				if (asbValue != 0)
				{
					Byte[] asb = StatusEngine.BuildAutomaticStatus(e.Current);
					Write(asb);
					journal.Write("ASB émis (" + e.Cause + ") : " + FormatBytes(asb));
				}

				if (job != null && !job.Blocked)
				{
					String? reason = FaultRules.GetBlockReason(e.Current);
					if (reason != null)
					{
						job.Blocked = true;
						job.Outcome = JobOutcome.Truncated;
						job.Reason = reason;
						Publish(false);
					}
				}
			}
		}

		private void OnColumnOverflow(Object? sender, ColumnOverflowEventArgs e)
		{
			journal.Write(String.Format(
				CultureInfo.InvariantCulture,
				"Dépassement de colonne (job {0}) : ligne {1}, longueur {2} pour {3} colonnes",
				job?.Id ?? "?",
				e.LineNumber,
				e.Length,
				e.Columns));
		}

		private void Write(Byte[] data)
		{
			lock (writeLock)
			{
				try
				{
					stream.Write(data, 0, data.Length);
				}
				catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is SocketException)
				{
					// The cash register is gone: the read loop notices and closes the connection.
				}
			}
		}

		private static String FormatBytes(Byte[] data)
		{
			StringBuilder text = new StringBuilder();
			foreach (Byte value in data)
			{
				if (text.Length > 0)
				{
					text.Append(' ');
				}

				text.Append(value.ToString("X2", CultureInfo.InvariantCulture));
			}

			return text.ToString();
		}

		private sealed class Job
		{
			public Job(String id, DateTime startedAt, PaperWidth paper, PaperProfile profile)
			{
				Id = id;
				StartedAt = startedAt;
				Paper = paper;
				Profile = profile;
			}

			public String Id { get; }

			public DateTime StartedAt { get; }

			public PaperWidth Paper { get; }

			public PaperProfile Profile { get; }

			public Boolean Blocked { get; set; }

			public JobOutcome Outcome { get; set; }

			public String? Reason { get; set; }
		}
	}
}