namespace MimaEmuPrinter.Tests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Net;
	using System.Net.Sockets;
	using System.Text;
	using System.Threading.Tasks;
	using MimaEmuPrinter.Core.Archive;
	using MimaEmuPrinter.Core.Logging;
	using MimaEmuPrinter.Core.Network;
	using MimaEmuPrinter.Core.Paper;
	using MimaEmuPrinter.Core.Rendering;
	using MimaEmuPrinter.Core.Status;

	/// <summary>A complete emulator (status, journal, archive, server) on a temporary folder and an ephemeral port.</summary>
	public sealed class PrinterTestBench : IAsyncDisposable
	{
		public PrinterTestBench(PrinterServerOptions? options = null)
		{
			Directory = Path.Combine(Path.GetTempPath(), "MimaEmuPrinterTests", Guid.NewGuid().ToString("N"));
			Status = new StatusEngine();
			Journal = new JournalService(Path.Combine(Directory, ArchiveService.JournalFileName));
			Archive = new ArchiveService(Directory, Journal);
			Archive.EnsureDirectories();
			Server = new PrinterServer(Status, Journal, Archive, () => Paper, TimeProvider.System, options);
			Server.TicketUpdated += (sender, update) =>
			{
				lock (Updates)
				{
					Updates.Add(update);
				}
			};
		}

		public String Directory { get; }

		public StatusEngine Status { get; }

		public JournalService Journal { get; }

		public ArchiveService Archive { get; }

		public PrinterServer Server { get; }

		public PaperWidth Paper { get; set; } = PaperWidth.Mm80Col42;

		public List<TicketUpdate> Updates { get; } = new List<TicketUpdate>();

		public Int32 Port
		{
			get { return Server.LocalEndPoint!.Port; }
		}

		public void Start()
		{
			if (!Server.Start(IPAddress.Loopback, 0))
			{
				throw new InvalidOperationException("The test server could not start.");
			}
		}

		public async Task<PrinterClient> ConnectAsync()
		{
			TcpClient client = new TcpClient();
			await client.ConnectAsync(IPAddress.Loopback, Port);
			return new PrinterClient(client);
		}

		public String JournalText()
		{
			return String.Join("\n", Journal.Snapshot().Select(entry => entry.Message));
		}

		public Boolean JournalContains(String text)
		{
			return JournalText().Contains(text, StringComparison.Ordinal);
		}

		public async Task<Boolean> WaitForJournalAsync(String text, Int32 timeoutMs = 5000)
		{
			return await WaitUntilAsync(() => JournalContains(text), timeoutMs);
		}

		public String PdfPath()
		{
			return Archive.GetPdfPath(DateTime.Now);
		}

		public IReadOnlyList<JobRecord> ReadRecords()
		{
			List<JobRecord> records = new List<JobRecord>();
			foreach (String file in System.IO.Directory.GetFiles(Archive.RawDirectory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
			{
				records.Add(System.Text.Json.JsonSerializer.Deserialize<JobRecord>(
					File.ReadAllText(file),
					new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!);
			}

			return records;
		}

		public static async Task<Boolean> WaitUntilAsync(Func<Boolean> condition, Int32 timeoutMs = 5000)
		{
			DateTime limit = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (DateTime.UtcNow < limit)
			{
				if (condition())
				{
					return true;
				}

				await Task.Delay(10);
			}

			return condition();
		}

		public async ValueTask DisposeAsync()
		{
			await Server.StopAsync();
			try
			{
				System.IO.Directory.Delete(Directory, true);
			}
			catch (IOException)
			{
				// Best effort cleanup of the temporary folder.
			}
		}
	}

	/// <summary>A raw TCP cash register, like "nc ip 9100".</summary>
	public sealed class PrinterClient : IDisposable
	{
		private readonly TcpClient client;
		private readonly NetworkStream stream;

		public PrinterClient(TcpClient client)
		{
			this.client = client;
			stream = client.GetStream();
		}

		public static async Task<PrinterClient> ConnectAsync(Int32 port)
		{
			TcpClient tcp = new TcpClient();
			await tcp.ConnectAsync(IPAddress.Loopback, port);
			return new PrinterClient(tcp);
		}

		public async Task SendAsync(params Byte[] data)
		{
			await stream.WriteAsync(data);
			await stream.FlushAsync();
		}

		public Task SendTextAsync(String text)
		{
			return SendAsync(Encoding.Latin1.GetBytes(text));
		}

		public async Task<Byte[]> ReadAsync(Int32 count, Int32 timeoutMs = 3000)
		{
			using System.Threading.CancellationTokenSource timeout = new System.Threading.CancellationTokenSource(timeoutMs);
			Byte[] buffer = new Byte[count];
			Int32 total = 0;
			while (total < count)
			{
				Int32 read = await stream.ReadAsync(buffer.AsMemory(total, count - total), timeout.Token);
				if (read == 0)
				{
					throw new EndOfStreamException("The printer closed the connection.");
				}

				total += read;
			}

			return buffer;
		}

		/// <summary>True when the printer closed (or reset) the connection within the timeout.</summary>
		public async Task<Boolean> IsClosedByPrinterAsync(Int32 timeoutMs = 3000)
		{
			try
			{
				using System.Threading.CancellationTokenSource timeout = new System.Threading.CancellationTokenSource(timeoutMs);
				Byte[] buffer = new Byte[1];
				Int32 read = await stream.ReadAsync(buffer.AsMemory(), timeout.Token);
				return read == 0;
			}
			catch (IOException)
			{
				return true;
			}
			catch (OperationCanceledException)
			{
				return false;
			}
		}

		public void Dispose()
		{
			client.Dispose();
		}
	}
}
