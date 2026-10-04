namespace MimaEmuPrinter.Core.Status
{
	using System;

	/// <summary>Raised when the effective fault state changes.</summary>
	public sealed class FaultsChangedEventArgs : EventArgs
	{
		public FaultsChangedEventArgs(PrinterFaults previous, PrinterFaults current, PrinterFaults changed, Boolean enabled)
		{
			Previous = previous;
			Current = current;
			Changed = changed;
			Enabled = enabled;
		}

		public PrinterFaults Previous { get; }

		public PrinterFaults Current { get; }

		/// <summary>The switch the tester flipped (None when the state was cleared).</summary>
		public PrinterFaults Changed { get; }

		public Boolean Enabled { get; }

		public String Cause
		{
			get
			{
				if (Changed == PrinterFaults.None)
					return "Tout effacer";

				return FaultRules.GetLabel(Changed) + (Enabled ? " activé" : " désactivé");
			}
		}
	}

	/// <summary>
	/// Holds the fault switches, applies the consistency rules and builds the ESC/POS status bytes.
	/// A switch implied by another active switch is locked: it cannot be cleared on its own.
	/// </summary>
	public sealed class StatusEngine
	{
		/// <summary>Epson fixed bits of a real-time status byte: bit 1 = 1, bit 4 = 1, bits 0 and 7 = 0.</summary>
		private const Byte FixedBits = 0x12;

		private readonly Object syncRoot = new();
		private PrinterFaults explicitFaults = PrinterFaults.None;

		public event EventHandler<FaultsChangedEventArgs>? Changed;

		/// <summary>Active faults, including the ones implied by the consistency rules.</summary>
		public PrinterFaults Effective
		{
			get
			{
				lock (syncRoot)
				{
					return Combine(explicitFaults);
				}
			}
		}

		/// <summary>Faults that are on only because another switch implies them (cannot be cleared by hand).</summary>
		public PrinterFaults Locked
		{
			get
			{
				lock (syncRoot)
				{
					return FaultRules.GetImplied(explicitFaults);
				}
			}
		}

		/// <summary>Flips one switch. Returns true when the effective state changed.</summary>
		public Boolean SetFault(PrinterFaults fault, Boolean enabled)
		{
			FaultsChangedEventArgs args;
			lock (syncRoot)
			{
				var before = Combine(explicitFaults);
				if (enabled)
				{
					explicitFaults |= fault;
				}
				else
				{
					var impliedByOthers = FaultRules.GetImplied(explicitFaults & ~fault);
					if ((impliedByOthers & fault) != 0)
						return false;

					explicitFaults &= ~fault;
				}

				var after = Combine(explicitFaults);
				if (after == before)
					return false;

				args = new FaultsChangedEventArgs(before, after, fault, enabled);
			}

			Changed?.Invoke(this, args);
			return true;
		}

		/// <summary>Back to the nominal online state.</summary>
		public void ClearAll()
		{
			FaultsChangedEventArgs args;
			lock (syncRoot)
			{
				PrinterFaults before = Combine(explicitFaults);
				explicitFaults = PrinterFaults.None;
				if (before == PrinterFaults.None)
					return;

				args = new FaultsChangedEventArgs(before, PrinterFaults.None, PrinterFaults.None, false);
			}

			Changed?.Invoke(this, args);
		}

		/// <summary>Answer to DLE EOT n (n = 1 to 4).</summary>
		public Boolean TryGetRealTimeStatus(Int32 n, out Byte value)
		{
			return TryGetRealTimeStatus(Effective, n, out value);
		}

		public static Boolean TryGetRealTimeStatus(PrinterFaults faults, Int32 n, out Byte value)
		{
			Int32 bits = FixedBits;
			switch (n)
			{
				case 1:
					bits |= Has(faults, PrinterFaults.Offline) ? 0x08 : 0;
					break;
				case 2:
					bits |= Has(faults, PrinterFaults.CoverOpen) ? 0x04 : 0;
					bits |= Has(faults, PrinterFaults.PaperEndStop) ? 0x20 : 0;
					bits |= Has(faults, PrinterFaults.Error) ? 0x40 : 0;
					break;
				case 3:
					bits |= Has(faults, PrinterFaults.RecoverableError) ? 0x04 : 0;
					bits |= Has(faults, PrinterFaults.CutterError) ? 0x08 : 0;
					bits |= Has(faults, PrinterFaults.UnrecoverableError) ? 0x20 : 0;
					break;
				case 4:
					bits |= Has(faults, PrinterFaults.PaperNearEnd) ? 0x0C : 0;
					bits |= Has(faults, PrinterFaults.PaperOut) ? 0x60 : 0;
					break;
				default:
					value = 0;
					return false;
			}

			value = (Byte)bits;
			return true;
		}

		/// <summary>Answer to GS r 1: paper sensors (bits 0-1 near end, bits 2-3 paper end).</summary>
		public Byte GetPaperSensorStatus()
		{
			var faults = Effective;
			var bits = 0;
			bits |= Has(faults, PrinterFaults.PaperNearEnd) ? 0x03 : 0;
			bits |= Has(faults, PrinterFaults.PaperOut) ? 0x0C : 0;
			return (Byte)bits;
		}

		/// <summary>The 4 bytes of an automatic status back (ASB).</summary>
		public Byte[] BuildAutomaticStatus()
		{
			return BuildAutomaticStatus(Effective);
		}

		public static Byte[] BuildAutomaticStatus(PrinterFaults faults)
		{
			var first = 0x10;
			first |= Has(faults, PrinterFaults.Offline) ? 0x08 : 0;
			first |= Has(faults, PrinterFaults.CoverOpen) ? 0x20 : 0;

			var second = 0;
			second |= Has(faults, PrinterFaults.CutterError) ? 0x08 : 0;
			second |= Has(faults, PrinterFaults.UnrecoverableError) ? 0x20 : 0;
			second |= Has(faults, PrinterFaults.RecoverableError) ? 0x40 : 0;

			var third = 0;
			third |= Has(faults, PrinterFaults.PaperNearEnd) ? 0x03 : 0;
			third |= Has(faults, PrinterFaults.PaperOut) ? 0x0C : 0;

			return [(Byte)first, (Byte)second, (Byte)third, 0x00];
		}

		private static PrinterFaults Combine(PrinterFaults explicitFaults)
		{
			return explicitFaults | FaultRules.GetImplied(explicitFaults);
		}

		private static Boolean Has(PrinterFaults faults, PrinterFaults fault)
		{
			return (faults & fault) != 0;
		}
	}
}