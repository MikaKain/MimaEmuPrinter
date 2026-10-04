namespace MimaEmuPrinter.Tests
{
	using System;
	using System.Linq;
	using System.Text;
	using System.Threading.Tasks;
	using MimaEmuPrinter.Core.Archive;
	using MimaEmuPrinter.Core.Network;
	using MimaEmuPrinter.Core.Paper;
	using MimaEmuPrinter.Core.Rendering;
	using MimaEmuPrinter.Core.Status;
	using Xunit;

	/// <summary>Timeouts, job closing triggers and resilience of the connection handling.</summary>
	public sealed class ConnectionBehaviorTests
	{
		[Fact]
		public async Task JobIsClosedAfter400MillisecondsWithoutNewByte()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendTextAsync("no cut here\n");
			Assert.True(await PrinterTestBench.WaitUntilAsync(() => bench.JournalContains("Job J0001 démarré")));
			await Task.Delay(150);
			Assert.False(bench.JournalContains("Job J0001 clos"));

			Assert.True(await bench.WaitForJournalAsync("Job J0001 clos (inactivité 400 ms)", 2000));
			await bench.Archive.WaitIdleAsync();
			JobRecord record = Assert.Single(bench.ReadRecords());
			Assert.Equal("no cut here", record.Lines.Single().Text);
		}

		[Fact]
		public async Task JobIsClosedWhenTheSocketCloses()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			PrinterClient client = await bench.ConnectAsync();

			await client.SendTextAsync("closing the socket\n");
			client.Dispose();

			Assert.True(await bench.WaitForJournalAsync("Job J0001 clos"));
			Assert.True(await bench.WaitForJournalAsync("motif : caisse"));
		}

		[Fact]
		public async Task BytesAfterTheCutStartANewJob()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendAsync(Encoding.ASCII.GetBytes("one\n").Concat(new Byte[] { 0x1D, 0x56, 0x00 }).Concat(Encoding.ASCII.GetBytes("two\n")).Concat(new Byte[] { 0x1D, 0x56, 0x00 }).ToArray());

			Assert.True(await bench.WaitForJournalAsync("Job J0002 clos"));
			await bench.Archive.WaitIdleAsync();
			Assert.Equal(new[] { "one", "two" }, bench.ReadRecords().Select(r => r.Lines.First().Text).ToArray());
		}

		[Fact]
		public async Task StatusIsAnsweredImmediatelyWhilePrintDataIsStillPending()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendAsync(Encoding.ASCII.GetBytes("printing, no line feed yet").Concat(new Byte[] { 0x10, 0x04, 0x01 }).ToArray());
			Byte[] answer = await client.ReadAsync(1, 300);

			Assert.Equal(0x12, answer[0]);
			Assert.False(bench.JournalContains("Job J0001 clos"));
		}

		[Fact]
		public async Task InactiveConnectionIsClosedAfterTheTimeout()
		{
			await using PrinterTestBench bench = new PrinterTestBench(new PrinterServerOptions { InactivityTimeout = TimeSpan.FromMilliseconds(400) });
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			Boolean closed = await client.IsClosedByPrinterAsync(3000);

			Assert.True(closed);
			Assert.True(await bench.WaitForJournalAsync("motif : timeout"));
			Assert.True(await PrinterTestBench.WaitUntilAsync(() => bench.Server.State == ListenerState.Listening));
		}

		[Fact]
		public async Task ActivityKeepsTheConnectionOpen()
		{
			await using PrinterTestBench bench = new PrinterTestBench(new PrinterServerOptions { InactivityTimeout = TimeSpan.FromMilliseconds(600) });
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			for (Int32 index = 0; index < 4; index++)
			{
				await Task.Delay(250);
				await client.SendAsync(0x10, 0x04, 0x01);
				Byte[] answer = await client.ReadAsync(1);
				Assert.Equal(0x12, answer[0]);
			}

			Assert.False(bench.JournalContains("motif : timeout"));
		}

		[Fact]
		public async Task StoppingTheListenerClosesTheOpenConnectionAndTheJob()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();
			await client.SendTextAsync("interrupted\n");
			await PrinterTestBench.WaitUntilAsync(() => bench.JournalContains("Job J0001 démarré"));

			await bench.Server.StopAsync();

			Assert.Equal(ListenerState.Stopped, bench.Server.State);
			Assert.True(bench.JournalContains("Job J0001 clos"));
			Assert.True(bench.JournalContains("motif : arrêt"));
			Assert.True(bench.JournalContains("Listener arrêté (127.0.0.1:"));
		}

		[Fact]
		public async Task GarbageAndUnknownCommandsNeverKillTheServer()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			Random random = new Random(42);
			Byte[] garbage = new Byte[50000];
			random.NextBytes(garbage);

			using (PrinterClient client = await bench.ConnectAsync())
			{
				// Random bytes may swallow a pending command or trigger status answers: only survival matters here.
				await client.SendAsync(garbage);
				await Task.Delay(300);
			}

			Assert.True(await PrinterTestBench.WaitUntilAsync(() => bench.Server.State == ListenerState.Listening));
			using PrinterClient second = await bench.ConnectAsync();
			await second.SendAsync(0x10, 0x04, 0x02);
			Assert.Equal(0x12, (await second.ReadAsync(1))[0]);
		}

		[Fact]
		public async Task UnknownEscPosSequencesAreCountedInTheJobLine()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendAsync(Encoding.ASCII.GetBytes("text\n").Concat(new Byte[] { 0x1B, 0x4D, 0x01, 0x1D, 0x42, 0x01, 0x1D, 0x56, 0x00 }).ToArray());

			Assert.True(await bench.WaitForJournalAsync("2 commandes ignorées"));
		}

		[Fact]
		public async Task DrawerPulseIsJournaledButNotDrawn()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendAsync(Encoding.ASCII.GetBytes("x\n").Concat(new Byte[] { 0x1B, 0x70, 0x00, 0x19, 0xFA, 0x1D, 0x56, 0x00 }).ToArray());
			await bench.WaitForJournalAsync("Job J0001 clos");
			await bench.Archive.WaitIdleAsync();

			Assert.True(bench.JournalContains("Pulse tiroir-caisse"));
			Assert.Equal(new[] { "x" }, bench.ReadRecords().Single().Lines.Where(l => l.Kind == TicketLineKind.Text).Select(l => l.Text).ToArray());
		}

		[Fact]
		public async Task RasterBitmapBecomesAGreyLogoBand()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();
			Byte[] bitmap = Enumerable.Repeat((Byte)0x10, 48 * 60).ToArray();

			await client.SendAsync(new Byte[] { 0x1D, 0x76, 0x30, 0x00, 48, 0x00, 60, 0x00 }.Concat(bitmap).Concat(new Byte[] { 0x1D, 0x56, 0x00 }).ToArray());
			await bench.WaitForJournalAsync("Job J0001 clos");
			await bench.Archive.WaitIdleAsync();

			TicketLine band = bench.ReadRecords().Single().Lines.First();
			Assert.Equal(TicketLineKind.Band, band.Kind);
			Assert.Equal("logo 384 × 60", band.BandText);
		}

		[Fact]
		public async Task ServerStopsAndRestartsOnTheSameInstance()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			await bench.Server.StopAsync();

			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();
			await client.SendAsync(0x10, 0x04, 0x01);

			Assert.Equal(0x12, (await client.ReadAsync(1))[0]);
		}

		[Fact]
		public async Task StateGoesFromListeningToBusyToListening()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			Assert.Equal(ListenerState.Listening, bench.Server.State);

			PrinterClient client = await bench.ConnectAsync();
			Assert.True(await PrinterTestBench.WaitUntilAsync(() => bench.Server.State == ListenerState.Busy));
			client.Dispose();

			Assert.True(await PrinterTestBench.WaitUntilAsync(() => bench.Server.State == ListenerState.Listening));
		}

		[Fact]
		public async Task NearEndAndPaperOutAreReportedByGsR()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendAsync(0x1D, 0x72, 0x01);
			Byte nominal = (await client.ReadAsync(1))[0];
			bench.Status.SetFault(PrinterFaults.PaperOut, true);
			await client.SendAsync(0x1D, 0x72, 0x31);
			Byte empty = (await client.ReadAsync(1))[0];
			await client.SendAsync(0x1D, 0x72, 0x02);
			Byte drawer = (await client.ReadAsync(1))[0];

			Assert.Equal(0x00, nominal);
			Assert.Equal(0x0C, empty & 0x0C);
			Assert.Equal(0x00, drawer);
		}

		[Fact]
		public async Task IdentityAnswersCarryModelAndVersion()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendAsync(0x1D, 0x49, 67);
			Byte[] model = await client.ReadAsync(1 + "MimaEmuPrinter".Length + 1);
			await client.SendAsync(0x1D, 0x49, 65);
			Byte[] version = await client.ReadAsync(1 + "1.0".Length + 1);

			Assert.Equal("MimaEmuPrinter", Encoding.ASCII.GetString(model, 1, model.Length - 2));
			Assert.Equal("1.0", Encoding.ASCII.GetString(version, 1, version.Length - 2));
			Assert.Equal(0, model[^1]);
		}

		[Fact]
		public async Task FaultRaisedAfterTheFirstPrintableByteTruncatesWithTheCutterReason()
		{
			await using PrinterTestBench bench = new PrinterTestBench();
			bench.Start();
			using PrinterClient client = await bench.ConnectAsync();

			await client.SendTextAsync("kept line\n");
			await PrinterTestBench.WaitUntilAsync(() => bench.Updates.Any(u => u.Ticket.Lines.Any(l => l.Text == "kept line")));
			bench.Status.SetFault(PrinterFaults.CutterError, true);
			await client.SendAsync(Encoding.ASCII.GetBytes("lost line\n").Concat(new Byte[] { 0x1D, 0x56, 0x00 }).ToArray());
			await bench.WaitForJournalAsync("Job J0001 clos");
			await bench.Archive.WaitIdleAsync();

			JobRecord record = bench.ReadRecords().Single();
			Assert.Equal(JobOutcome.Truncated, record.Outcome);
			Assert.Equal("tronqué : massicot", record.Notice);
			Assert.Equal(new[] { "kept line" }, record.Lines.Select(l => l.Text).ToArray());
			Assert.Equal(PaperWidth.Mm80Col42, record.Paper);
		}
	}
}
