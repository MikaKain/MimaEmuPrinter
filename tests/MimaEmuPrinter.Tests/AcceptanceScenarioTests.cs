namespace MimaEmuPrinter.Tests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Net;
	using System.Text;
	using System.Threading.Tasks;
	using MimaEmuPrinter.Core.Archive;
	using MimaEmuPrinter.Core.Network;
	using MimaEmuPrinter.Core.Paper;
	using MimaEmuPrinter.Core.Rendering;
	using MimaEmuPrinter.Core.Settings;
	using MimaEmuPrinter.Core.Status;
	using PdfSharp.Pdf.IO;
	using Xunit;

	/// <summary>The test rules T1 to T9 of the specification, plus the acceptance criteria that can be automated.</summary>
	public sealed class AcceptanceScenarioTests
	{
		private static readonly Byte[] Init = { 0x1B, 0x40, 0x1B, 0x74, 19 };

		private static readonly Byte[] Cut = { 0x1D, 0x56, 0x00 };

		private static Byte[] Ticket(params String[] lines)
		{
			List<Byte> bytes = new List<Byte>(Init);
			foreach (String line in lines)
			{
				bytes.AddRange(Encoding.Latin1.GetBytes(line + "\n"));
			}

			bytes.AddRange(Cut);
			return bytes.ToArray();
		}

		private static Int32 CountPages(String path)
		{
			using PdfSharp.Pdf.PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
			return document.PageCount;
		}

		[Fact]
		public async Task T1_TextTicketThenCutShowsInDisplayJournalAndPdf()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();

			using PrinterClient client = await bench.ConnectAsync();
			await client.SendAsync(Ticket("MIMA CAFE", "Table 12", "Couverts : 4"));

			Assert.True(await bench.WaitForJournalAsync("Job J0001 clos"));
			await bench.Archive.WaitIdleAsync();

			TicketUpdate closed = bench.Updates.Last();
			Assert.True(closed.IsClosed);
			Assert.Equal(new[] { "MIMA CAFE", "Table 12", "Couverts : 4" }, closed.Ticket.Lines.Where(l => l.Kind == TicketLineKind.Text).Select(l => l.Text).ToArray());
			Assert.Equal(TicketLineKind.Cut, closed.Ticket.Lines.Last().Kind);
			Assert.True(File.Exists(bench.PdfPath()));
			Assert.Equal(1, CountPages(bench.PdfPath()));
			Assert.True(bench.JournalContains("Connexion ouverte depuis 127.0.0.1"));
			Assert.True(File.Exists(bench.Archive.JournalPath));
			Assert.Contains("Job J0001 clos", File.ReadAllText(bench.Archive.JournalPath));
		}

		[Fact]
		public async Task T1_RawStreamIsKeptNextToThePdfWithTheSameTimestamp()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			Byte[] sent = Ticket("Hello");

			using PrinterClient client = await bench.ConnectAsync();
			await client.SendAsync(sent);
			await bench.WaitForJournalAsync("Job J0001 clos");
			await bench.Archive.WaitIdleAsync();

			String[] bins = Directory.GetFiles(bench.Archive.RawDirectory, "*.bin");
			String bin = Assert.Single(bins);
			Assert.Equal(sent, File.ReadAllBytes(bin));
			Assert.StartsWith(DateTime.Now.ToString("yyyyMMdd"), Path.GetFileName(bin));
		}

		[Fact]
		public async Task T1_ClosedJobAppearsInJournalAndPdfWithinTwoSeconds()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			DateTime start = DateTime.UtcNow;
			await client.SendAsync(Ticket("fast"));
			Boolean journaled = await bench.WaitForJournalAsync("Job J0001 clos", 2000);
			Boolean pdf = await PrinterTestBench.WaitUntilAsync(() => File.Exists(bench.PdfPath()), 2000);

			Assert.True(journaled);
			Assert.True(pdf);
			Assert.True((DateTime.UtcNow - start).TotalSeconds < 2.5);
		}

		[Fact]
		public async Task T2_PaperSwitchAppliesToTheNextJobOnly()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			String longLine = new String('A', 50);
			using PrinterClient client = await bench.ConnectAsync();

			bench.Paper = PaperWidth.Mm58Col32;
			await client.SendAsync(Ticket(longLine));
			await bench.WaitForJournalAsync("Job J0001 clos");
			bench.Paper = PaperWidth.Mm80Col42;
			await client.SendAsync(Ticket(longLine));
			await bench.WaitForJournalAsync("Job J0002 clos");
			bench.Paper = PaperWidth.Mm80Col48;
			await client.SendAsync(Ticket(longLine));
			await bench.WaitForJournalAsync("Job J0003 clos");
			await bench.Archive.WaitIdleAsync();

			IReadOnlyList<JobRecord> records = bench.ReadRecords();
			Assert.Equal(new[] { 32, 42, 48 }, records.Select(r => r.Columns).ToArray());
			Assert.Equal(new[] { 32, 42, 48 }, records.Select(r => r.Lines[0].Columns).ToArray());
			Assert.True(bench.JournalContains("Dépassement de colonne (job J0001) : ligne 1, longueur 50 pour 32 colonnes"));
			Assert.True(bench.JournalContains("Dépassement de colonne (job J0002) : ligne 1, longueur 50 pour 42 colonnes"));
			Assert.True(bench.JournalContains("Dépassement de colonne (job J0003) : ligne 1, longueur 50 pour 48 colonnes"));
		}

		[Fact]
		public async Task T2_PaperChangedDuringAJobDoesNotAffectTheTicketBeingRendered()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			bench.Paper = PaperWidth.Mm80Col42;
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendTextAsync("first line\n");
			await PrinterTestBench.WaitUntilAsync(() => bench.Updates.Count > 0);
			bench.Paper = PaperWidth.Mm58Col32;
			await client.SendAsync(Encoding.ASCII.GetBytes("second line\n").Concat(Cut).ToArray());
			await bench.WaitForJournalAsync("Job J0001 clos");

			TicketUpdate closed = bench.Updates.Last();
			Assert.Equal(PaperWidth.Mm80Col42, closed.Paper);
			Assert.Equal(42, closed.Ticket.Columns);
		}

		[Fact]
		public async Task T3_PaperOutAnswersStatusAndProducesAnEmptyMarkedJob()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			bench.Status.SetFault(PrinterFaults.PaperOut, true);
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendAsync(0x10, 0x04, 0x04);
			Byte[] paper = await client.ReadAsync(1);
			await client.SendAsync(0x10, 0x04, 0x01);
			Byte[] offline = await client.ReadAsync(1);
			await client.SendAsync(Ticket("never printed"));
			await bench.WaitForJournalAsync("Job J0001 clos");
			await bench.Archive.WaitIdleAsync();

			Assert.Equal(0x72, paper[0] & 0x72);
			Assert.Equal(0x08, offline[0] & 0x08);
			JobRecord record = Assert.Single(bench.ReadRecords());
			Assert.Equal(JobOutcome.NotPrinted, record.Outcome);
			Assert.Equal("plus de papier", record.Reason);
			Assert.Empty(record.Lines);
			Assert.Equal("non imprimé : plus de papier", record.Notice);
			Assert.True(bench.JournalContains("Statut interrogé : DLE EOT 4"));
		}

		[Theory]
		[InlineData(PrinterFaults.CoverOpen, "capot ouvert")]
		[InlineData(PrinterFaults.Offline, "hors ligne")]
		public async Task T3_CoverOpenOrOfflineBeforeThePrintGivesAnEmptyJob(PrinterFaults fault, String reason)
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			bench.Status.SetFault(fault, true);
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendAsync(Ticket("text"));
			await bench.WaitForJournalAsync("Job J0001 clos");
			await bench.Archive.WaitIdleAsync();

			JobRecord record = Assert.Single(bench.ReadRecords());
			Assert.Equal(JobOutcome.NotPrinted, record.Outcome);
			Assert.Equal(reason, record.Reason);
			Assert.Empty(record.Lines);
		}

		[Fact]
		public async Task T3_NearEndStillPrints()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			bench.Status.SetFault(PrinterFaults.PaperNearEnd, true);
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendAsync(Ticket("still printed"));
			await bench.WaitForJournalAsync("Job J0001 clos");
			await bench.Archive.WaitIdleAsync();

			JobRecord record = Assert.Single(bench.ReadRecords());
			Assert.Equal(JobOutcome.Printed, record.Outcome);
			Assert.Contains(record.Lines, line => line.Text == "still printed");
		}

		[Fact]
		public async Task T4_CoverOpenedDuringTheSendEmitsAsbAndTruncatesTheTicket()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendAsync(Init.Concat(new Byte[] { 0x1D, 0x61, 0xFF }).Concat(Encoding.ASCII.GetBytes("line one\n")).ToArray());
			await PrinterTestBench.WaitUntilAsync(() => bench.Updates.Any(u => u.Ticket.Lines.Any(l => l.Text == "line one")));
			bench.Status.SetFault(PrinterFaults.CoverOpen, true);

			Byte[] asb = await client.ReadAsync(4);
			await client.SendAsync(Encoding.ASCII.GetBytes("line two\n").Concat(Cut).ToArray());
			await bench.WaitForJournalAsync("Job J0001 clos");
			await bench.Archive.WaitIdleAsync();

			Assert.Equal(new Byte[] { 0x38, 0x00, 0x00, 0x00 }, asb);
			JobRecord record = Assert.Single(bench.ReadRecords());
			Assert.Equal(JobOutcome.Truncated, record.Outcome);
			Assert.Equal("tronqué : capot ouvert", record.Notice);
			Assert.Equal(new[] { "line one" }, record.Lines.Select(l => l.Text).ToArray());
			Assert.True(bench.JournalContains("ASB émis (Capot ouvert activé) : 38 00 00 00"));
		}

		[Fact]
		public async Task T4_AsbIsNotSentUnlessArmed()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();
			await client.SendAsync(0x10, 0x04, 0x01);
			await client.ReadAsync(1);

			bench.Status.SetFault(PrinterFaults.CoverOpen, true);
			await client.SendAsync(0x10, 0x04, 0x01);
			Byte[] next = await client.ReadAsync(1);

			Assert.Equal(0x1A, next[0]);
			Assert.False(bench.JournalContains("ASB émis"));
		}

		[Fact]
		public async Task T4_AsbCanBeDisarmed()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();
			await client.SendAsync(0x1D, 0x61, 0x0F, 0x1D, 0x61, 0x00);
			await bench.WaitForJournalAsync("ASB désarmé");

			bench.Status.SetFault(PrinterFaults.PaperOut, true);
			await client.SendAsync(0x10, 0x04, 0x01);
			Byte[] next = await client.ReadAsync(1);

			Assert.Equal(0x1A, next[0]);
		}

		[Fact]
		public async Task T5_CutterErrorThenDleEnqIsJournaledAndTheFaultStaysActive()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			bench.Status.SetFault(PrinterFaults.CutterError, true);
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendAsync(0x10, 0x05, 0x02);
			Assert.True(await bench.WaitForJournalAsync("DLE ENQ 2 reçu"));
			await client.SendAsync(0x10, 0x04, 0x03);
			Byte[] errors = await client.ReadAsync(1);

			Assert.Equal(0x1A, errors[0]);
			Assert.Equal(PrinterFaults.CutterError | PrinterFaults.Error | PrinterFaults.Offline, bench.Status.Effective);
			Assert.True(bench.JournalContains("pannes actives : Offline, Erreur, Erreur massicot"));
		}

		[Fact]
		public async Task T6_SecondSimultaneousConnectionIsRefusedAndTheFirstStaysIntact()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient first = await bench.ConnectAsync();
			await first.SendAsync(0x10, 0x04, 0x01);
			await first.ReadAsync(1);
			Assert.True(await PrinterTestBench.WaitUntilAsync(() => bench.Server.State == ListenerState.Busy));

			using PrinterClient second = await bench.ConnectAsync();
			Boolean refused = await second.IsClosedByPrinterAsync();

			Assert.True(refused);
			Assert.True(await bench.WaitForJournalAsync("Connexion refusée"));
			await first.SendAsync(0x10, 0x04, 0x01);
			Byte[] answer = await first.ReadAsync(1);
			Assert.Equal(0x12, answer[0]);
		}

		[Fact]
		public async Task T6_AConnectionIsAcceptedAgainOnceTheFirstIsClosed()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			PrinterClient first = await bench.ConnectAsync();
			await first.SendAsync(0x10, 0x04, 0x01);
			await first.ReadAsync(1);
			first.Dispose();
			Assert.True(await PrinterTestBench.WaitUntilAsync(() => bench.Server.State == ListenerState.Listening));

			using PrinterClient second = await bench.ConnectAsync();
			await second.SendAsync(0x10, 0x04, 0x01);
			Byte[] answer = await second.ReadAsync(1);

			Assert.Equal(0x12, answer[0]);
		}

		[Fact]
		public async Task T7_PortAlreadyTakenIsJournaledWithoutThrowing()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			await using PrinterTestBench other = new PrinterTestBench();

			Boolean started = other.Server.Start(IPAddress.Loopback, bench.Port);

			Assert.False(started);
			Assert.Equal(ListenerState.Stopped, other.Server.State);
			Assert.True(other.JournalContains("Échec du bind sur 127.0.0.1:" + bench.Port));
		}

		[Fact]
		public async Task T7_ServerCanStartAgainOnceThePortIsFree()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			Int32 port = bench.Port;
			await using PrinterTestBench other = new PrinterTestBench();
			Assert.False(other.Server.Start(IPAddress.Loopback, port));

			await bench.Server.StopAsync();

			Assert.True(other.Server.Start(IPAddress.Loopback, port));
		}

		[Fact]
		public async Task T8_TenJobsFillOneA4PageAndTheEleventhOpensAPage()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			for (Int32 index = 1; index <= 10; index++)
			{
				await client.SendAsync(Ticket("Ticket " + index, "Table " + index));
			}

			Assert.True(await bench.WaitForJournalAsync("Job J0010 clos"));
			await bench.Archive.WaitIdleAsync();
			Assert.Equal(1, CountPages(bench.PdfPath()));

			await client.SendAsync(Ticket("Ticket 11"));
			Assert.True(await bench.WaitForJournalAsync("Job J0011 clos"));
			await bench.Archive.WaitIdleAsync();

			Assert.Equal(2, CountPages(bench.PdfPath()));
			Assert.Single(Directory.GetFiles(bench.Archive.OutputDirectory, "tickets-*.pdf"));
			Assert.False(File.Exists(bench.PdfPath() + ".tmp"));
		}

		[Fact]
		public async Task T9_SettingsAreRestoredFromTheJsonFile()
		{
			String path = Path.Combine(Path.GetTempPath(), "MimaEmuPrinterTests", Guid.NewGuid().ToString("N"), "settings.json");
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			try
			{
				SettingsStore store = new SettingsStore(path);
				store.Save(new AppSettings { ListenAddress = "192.168.1.50", Port = 9101, Paper = PaperWidth.Mm58Col32, Theme = ThemePreference.Dark });

				AppSettings restored = new SettingsStore(path).Load();

				Assert.Equal("192.168.1.50", restored.ListenAddress);
				Assert.Equal(9101, restored.Port);
				Assert.Equal(PaperWidth.Mm58Col32, restored.Paper);
				Assert.Equal(ThemePreference.Dark, restored.Theme);
				Assert.Contains("Mm58Col32", File.ReadAllText(path));
			}
			finally
			{
				Directory.Delete(Path.GetDirectoryName(path)!, true);
			}

			await Task.CompletedTask;
		}

		[Fact]
		public void T9_MissingOrCorruptSettingsFallBackToTheDefaults()
		{
			String directory = Path.Combine(Path.GetTempPath(), "MimaEmuPrinterTests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				AppSettings missing = new SettingsStore(Path.Combine(directory, "none.json")).Load();
				String corruptPath = Path.Combine(directory, "corrupt.json");
				File.WriteAllText(corruptPath, "{ not json");
				AppSettings corrupt = new SettingsStore(corruptPath).Load();
				String invalidPath = Path.Combine(directory, "invalid.json");
				File.WriteAllText(invalidPath, "{\"Port\": 70000, \"Paper\": \"Mm80Col48\"}");
				AppSettings invalid = new SettingsStore(invalidPath).Load();

				Assert.Equal(9100, missing.Port);
				Assert.Equal(PaperWidth.Mm80Col42, corrupt.Paper);
				Assert.Equal(ThemePreference.System, missing.Theme);
				Assert.Equal(9100, invalid.Port);
				Assert.Equal(PaperWidth.Mm80Col48, invalid.Paper);
			}
			finally
			{
				Directory.Delete(directory, true);
			}
		}

		[Fact]
		public async Task T9_TicketsOfTheDaySurviveAnApplicationRestart()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using (PrinterClient client = await bench.ConnectAsync())
			{
				await client.SendAsync(Ticket("before restart 1"));
				await client.SendAsync(Ticket("before restart 2"));
				await bench.WaitForJournalAsync("Job J0002 clos");
			}

			await bench.Archive.WaitIdleAsync();
			await bench.Server.StopAsync();

			ArchiveService restarted = new ArchiveService(bench.Directory, bench.Journal);
			String nextId = restarted.AllocateJobId(DateTime.Now);
			Assert.Equal("J0003", nextId);

			PrinterServer server = new PrinterServer(new StatusEngine(), bench.Journal, restarted, () => PaperWidth.Mm80Col42);
			Assert.True(server.Start(IPAddress.Loopback, 0));
			try
			{
				using PrinterClient client = await PrinterClient.ConnectAsync(server.LocalEndPoint!.Port);
				await client.SendAsync(Ticket("after restart"));
				Assert.True(await bench.WaitForJournalAsync("Job J0004 clos"));
				await restarted.WaitIdleAsync();
			}
			finally
			{
				await server.StopAsync();
			}

			Assert.Equal(3, bench.ReadRecords().Count);
			Assert.Equal(1, CountPages(bench.PdfPath()));
		}
	}
}
