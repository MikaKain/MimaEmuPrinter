namespace MimaEmuPrinter.Tests
{
	using System;
	using System.Collections.Generic;
	using MimaEmuPrinter.Core.Paper;
	using MimaEmuPrinter.Core.Rendering;
	using Xunit;

	public sealed class ManualTimeProvider : TimeProvider
	{
		private DateTimeOffset now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow()
		{
			return now;
		}

		public void Advance(TimeSpan duration)
		{
			now += duration;
		}
	}

	public sealed class TicketDisplayTests
	{
		private readonly ManualTimeProvider time = new ManualTimeProvider();

		[Fact]
		public void StartsWaiting()
		{
			TicketDisplay display = new TicketDisplay(time);

			Assert.Equal(TicketDisplayState.Waiting, display.State);
			Assert.Null(display.Current);
		}

		[Fact]
		public void LiveTicketIsShownWhilePrinting()
		{
			TicketDisplay display = new TicketDisplay(time);

			display.Apply(Update("J0001", false));

			Assert.Equal(TicketDisplayState.Printing, display.State);
		}

		[Fact]
		public void ClosedTicketStaysFiveSecondsWithACountdown()
		{
			TicketDisplay display = new TicketDisplay(time);
			display.Apply(Update("J0001", true));

			List<Int32> seen = new List<Int32>();
			for (Int32 second = 0; second < 5; second++)
			{
				Assert.Equal(TicketDisplayState.Closed, display.State);
				seen.Add(display.SecondsRemaining);
				time.Advance(TimeSpan.FromSeconds(1));
				display.Tick();
			}

			Assert.Equal(new[] { 5, 4, 3, 2, 1 }, seen);
			Assert.Equal(TicketDisplayState.Waiting, display.State);
		}

		[Fact]
		public void TicketIsStillThereJustBeforeTheDelay()
		{
			TicketDisplay display = new TicketDisplay(time);
			display.Apply(Update("J0001", true));

			time.Advance(TimeSpan.FromMilliseconds(4900));

			Assert.False(display.Tick());
			Assert.Equal(TicketDisplayState.Closed, display.State);
		}

		[Fact]
		public void NewJobReplacesTheDisplayImmediately()
		{
			TicketDisplay display = new TicketDisplay(time);
			display.Apply(Update("J0001", true));
			time.Advance(TimeSpan.FromSeconds(2));

			display.Apply(Update("J0002", false));

			Assert.Equal(TicketDisplayState.Printing, display.State);
			Assert.Equal("J0002", display.Current!.JobId);
		}

		[Fact]
		public void HoldFreezesTheTicketBeyondTheDelay()
		{
			TicketDisplay display = new TicketDisplay(time);
			display.Apply(Update("J0001", true));

			display.Hold();
			time.Advance(TimeSpan.FromSeconds(30));
			display.Tick();

			Assert.Equal(TicketDisplayState.Held, display.State);
			Assert.True(display.IsHeld);
		}

		[Fact]
		public void ReleaseAfterTheDelayClearsTheDisplay()
		{
			TicketDisplay display = new TicketDisplay(time);
			display.Apply(Update("J0001", true));
			display.Hold();
			time.Advance(TimeSpan.FromSeconds(30));

			display.Release();

			Assert.Equal(TicketDisplayState.Waiting, display.State);
		}

		[Fact]
		public void ReleaseBeforeTheDelayResumesTheCountdown()
		{
			TicketDisplay display = new TicketDisplay(time);
			display.Apply(Update("J0001", true));
			display.Hold();
			time.Advance(TimeSpan.FromSeconds(2));

			display.Release();

			Assert.Equal(TicketDisplayState.Closed, display.State);
			Assert.Equal(3, display.SecondsRemaining);
		}

		[Fact]
		public void NewJobEndsTheFreeze()
		{
			TicketDisplay display = new TicketDisplay(time);
			display.Apply(Update("J0001", true));
			display.Hold();

			display.Apply(Update("J0002", false));
			display.Apply(Update("J0002", true));
			time.Advance(TimeSpan.FromSeconds(6));
			display.Tick();

			Assert.Equal(TicketDisplayState.Waiting, display.State);
		}

		[Fact]
		public void HoldPressedWhilePrintingKeepsTheTicketAfterItCloses()
		{
			TicketDisplay display = new TicketDisplay(time);
			display.Apply(Update("J0001", false));

			display.Hold();
			display.Apply(Update("J0001", true));
			time.Advance(TimeSpan.FromSeconds(60));
			display.Tick();

			Assert.Equal(TicketDisplayState.Held, display.State);
		}

		private static TicketUpdate Update(String id, Boolean closed)
		{
			return new TicketUpdate(id, new Ticket(42, Array.Empty<TicketLine>()), PaperWidth.Mm80Col42, closed, JobOutcome.Printed, null);
		}
	}
}
