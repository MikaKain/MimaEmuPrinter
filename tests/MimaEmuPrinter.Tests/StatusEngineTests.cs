namespace MimaEmuPrinter.Tests
{
	using System;
	using System.Collections.Generic;
	using MimaEmuPrinter.Core.Status;
	using Xunit;

	public sealed class StatusEngineTests
	{
		[Fact]
		public void NominalStateReturnsEpsonFixedBitsOnly()
		{
			StatusEngine engine = new StatusEngine();

			for (Int32 n = 1; n <= 4; n++)
			{
				Assert.True(engine.TryGetRealTimeStatus(n, out Byte value));
				Assert.Equal(0x12, value);
			}

			Assert.False(engine.TryGetRealTimeStatus(5, out Byte _));
		}

		public static IEnumerable<Object[]> SingleFaultBytes()
		{
			// fault, DLE EOT n, expected byte
			yield return new Object[] { PrinterFaults.Offline, 1, 0x1A };
			yield return new Object[] { PrinterFaults.CoverOpen, 2, 0x16 };
			yield return new Object[] { PrinterFaults.PaperEndStop, 2, 0x32 };
			yield return new Object[] { PrinterFaults.Error, 2, 0x52 };
			yield return new Object[] { PrinterFaults.RecoverableError, 3, 0x16 };
			yield return new Object[] { PrinterFaults.CutterError, 3, 0x1A };
			yield return new Object[] { PrinterFaults.UnrecoverableError, 3, 0x32 };
			yield return new Object[] { PrinterFaults.PaperNearEnd, 4, 0x1E };
			yield return new Object[] { PrinterFaults.PaperOut, 4, 0x72 };
		}

		[Theory]
		[MemberData(nameof(SingleFaultBytes))]
		public void EachSwitchChangesTheDleEotByte(PrinterFaults fault, Int32 n, Int32 expected)
		{
			StatusEngine engine = new StatusEngine();

			engine.SetFault(fault, true);

			Assert.True(engine.TryGetRealTimeStatus(n, out Byte value));
			Assert.Equal(expected, value);
			Assert.True((value & 0x01) == 0 && (value & 0x02) != 0 && (value & 0x10) != 0 && (value & 0x80) == 0);
		}

		[Fact]
		public void PaperOutRaisesPaperEndStopErrorAndOffline()
		{
			StatusEngine engine = new StatusEngine();

			engine.SetFault(PrinterFaults.PaperOut, true);

			Assert.Equal(PrinterFaults.PaperOut | PrinterFaults.PaperEndStop | PrinterFaults.Error | PrinterFaults.Offline, engine.Effective);
			Assert.Equal(PrinterFaults.PaperEndStop | PrinterFaults.Error | PrinterFaults.Offline, engine.Locked);
		}

		[Fact]
		public void CoverOpenRaisesOffline()
		{
			StatusEngine engine = new StatusEngine();

			engine.SetFault(PrinterFaults.CoverOpen, true);

			Assert.Equal(PrinterFaults.CoverOpen | PrinterFaults.Offline, engine.Effective);
		}

		[Theory]
		[InlineData(PrinterFaults.CutterError)]
		[InlineData(PrinterFaults.RecoverableError)]
		[InlineData(PrinterFaults.UnrecoverableError)]
		public void ErrorKindsRaiseErrorAndOffline(PrinterFaults fault)
		{
			StatusEngine engine = new StatusEngine();

			engine.SetFault(fault, true);

			Assert.Equal(fault | PrinterFaults.Error | PrinterFaults.Offline, engine.Effective);
		}

		[Fact]
		public void NearEndStaysCompatibleWithOnline()
		{
			StatusEngine engine = new StatusEngine();

			engine.SetFault(PrinterFaults.PaperNearEnd, true);

			Assert.Equal(PrinterFaults.PaperNearEnd, engine.Effective);
			engine.TryGetRealTimeStatus(1, out Byte offline);
			Assert.Equal(0x12, offline);
		}

		[Fact]
		public void ImpliedSwitchCannotBeClearedWhileItsCauseIsActive()
		{
			StatusEngine engine = new StatusEngine();
			engine.SetFault(PrinterFaults.PaperOut, true);

			Boolean changed = engine.SetFault(PrinterFaults.Offline, false);

			Assert.False(changed);
			Assert.True((engine.Effective & PrinterFaults.Offline) != 0);
		}

		[Fact]
		public void ClearingTheCauseClearsTheImpliedSwitches()
		{
			StatusEngine engine = new StatusEngine();
			engine.SetFault(PrinterFaults.CoverOpen, true);

			engine.SetFault(PrinterFaults.CoverOpen, false);

			Assert.Equal(PrinterFaults.None, engine.Effective);
		}

		[Fact]
		public void ExplicitSwitchSurvivesTheRemovalOfAnotherCause()
		{
			StatusEngine engine = new StatusEngine();
			engine.SetFault(PrinterFaults.Offline, true);
			engine.SetFault(PrinterFaults.CoverOpen, true);

			engine.SetFault(PrinterFaults.CoverOpen, false);

			Assert.Equal(PrinterFaults.Offline, engine.Effective);
		}

		[Fact]
		public void SeveralEventsCanBeActiveTogether()
		{
			StatusEngine engine = new StatusEngine();

			engine.SetFault(PrinterFaults.PaperNearEnd, true);
			engine.SetFault(PrinterFaults.RecoverableError, true);

			engine.TryGetRealTimeStatus(4, out Byte paper);
			engine.TryGetRealTimeStatus(3, out Byte errors);
			Assert.Equal(0x1E, paper);
			Assert.Equal(0x16, errors);
		}

		[Fact]
		public void ClearAllReturnsToNominalOnline()
		{
			StatusEngine engine = new StatusEngine();
			engine.SetFault(PrinterFaults.PaperOut, true);
			engine.SetFault(PrinterFaults.PaperNearEnd, true);

			engine.ClearAll();

			Assert.Equal(PrinterFaults.None, engine.Effective);
			Assert.Equal(PrinterFaults.None, engine.Locked);
		}

		[Fact]
		public void ChangedEventReportsCauseAndNewState()
		{
			StatusEngine engine = new StatusEngine();
			FaultsChangedEventArgs? received = null;
			engine.Changed += (sender, e) => received = e;

			engine.SetFault(PrinterFaults.CoverOpen, true);

			Assert.NotNull(received);
			Assert.Equal(PrinterFaults.None, received!.Previous);
			Assert.Equal(PrinterFaults.CoverOpen | PrinterFaults.Offline, received.Current);
			Assert.Equal("Capot ouvert activé", received.Cause);
		}

		[Fact]
		public void NoEventWhenNothingChanges()
		{
			StatusEngine engine = new StatusEngine();
			Int32 count = 0;
			engine.Changed += (sender, e) => count++;

			engine.SetFault(PrinterFaults.CoverOpen, false);
			engine.ClearAll();

			Assert.Equal(0, count);
		}

		[Fact]
		public void AutomaticStatusHasFourBytesAndReflectsCoverAndOffline()
		{
			Byte[] nominal = StatusEngine.BuildAutomaticStatus(PrinterFaults.None);
			Byte[] cover = StatusEngine.BuildAutomaticStatus(PrinterFaults.CoverOpen | PrinterFaults.Offline);

			Assert.Equal(4, nominal.Length);
			Assert.Equal(0x10, nominal[0]);
			Assert.Equal(0x38, cover[0]);
		}

		[Fact]
		public void PaperSensorStatusReflectsNearEndAndPaperOut()
		{
			StatusEngine engine = new StatusEngine();
			Assert.Equal(0x00, engine.GetPaperSensorStatus());

			engine.SetFault(PrinterFaults.PaperNearEnd, true);
			Assert.Equal(0x03, engine.GetPaperSensorStatus());

			engine.SetFault(PrinterFaults.PaperOut, true);
			Assert.Equal(0x0F, engine.GetPaperSensorStatus());
		}
	}
}
