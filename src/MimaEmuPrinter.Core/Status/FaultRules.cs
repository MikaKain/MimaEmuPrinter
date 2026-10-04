namespace MimaEmuPrinter.Core.Status
{
	using System;
	using System.Collections.Generic;

	/// <summary>Consistency rules, labels and printing consequences of the fault events.</summary>
	public static class FaultRules
	{
		/// <summary>All faults, in the order of the specification table.</summary>
		public static IReadOnlyList<PrinterFaults> All { get; } = new PrinterFaults[]
		{
			PrinterFaults.Offline,
			PrinterFaults.CoverOpen,
			PrinterFaults.PaperEndStop,
			PrinterFaults.Error,
			PrinterFaults.RecoverableError,
			PrinterFaults.CutterError,
			PrinterFaults.UnrecoverableError,
			PrinterFaults.PaperNearEnd,
			PrinterFaults.PaperOut,
		};

		/// <summary>Faults that stop the virtual printer from printing.</summary>
		public const PrinterFaults PrintingBlockers = PrinterFaults.Offline | PrinterFaults.PaperEndStop | PrinterFaults.PaperOut;

		/// <summary>Faults that are switched on automatically when <paramref name="fault"/> is on.</summary>
		public static PrinterFaults GetConsequences(PrinterFaults fault)
		{
			switch (fault)
			{
				case PrinterFaults.PaperOut:
					return PrinterFaults.PaperEndStop | PrinterFaults.Error | PrinterFaults.Offline;
				case PrinterFaults.CoverOpen:
					return PrinterFaults.Offline;
				case PrinterFaults.CutterError:
				case PrinterFaults.RecoverableError:
				case PrinterFaults.UnrecoverableError:
					return PrinterFaults.Error | PrinterFaults.Offline;
				default:
					return PrinterFaults.None;
			}
		}

		/// <summary>Union of the consequences of every fault set in <paramref name="faults"/>.</summary>
		public static PrinterFaults GetImplied(PrinterFaults faults)
		{
			PrinterFaults implied = PrinterFaults.None;
			foreach (PrinterFaults fault in All)
			{
				if ((faults & fault) != 0)
				{
					implied |= GetConsequences(fault);
				}
			}

			return implied;
		}

		public static String GetLabel(PrinterFaults fault)
		{
			switch (fault)
			{
				case PrinterFaults.Offline:
					return "Offline";
				case PrinterFaults.CoverOpen:
					return "Capot ouvert";
				case PrinterFaults.PaperEndStop:
					return "Arrêt fin de papier";
				case PrinterFaults.Error:
					return "Erreur";
				case PrinterFaults.RecoverableError:
					return "Erreur récupérable";
				case PrinterFaults.CutterError:
					return "Erreur massicot";
				case PrinterFaults.UnrecoverableError:
					return "Erreur irrécupérable";
				case PrinterFaults.PaperNearEnd:
					return "Papier bientôt fini";
				case PrinterFaults.PaperOut:
					return "Plus de papier";
				default:
					return fault.ToString();
			}
		}

		public static String GetEffect(PrinterFaults fault)
		{
			switch (fault)
			{
				case PrinterFaults.Offline:
					return "Imprimante hors ligne";
				case PrinterFaults.CoverOpen:
					return "Capot papier ouvert";
				case PrinterFaults.PaperEndStop:
					return "Impression stoppée, plus de papier";
				case PrinterFaults.Error:
					return "Erreur présente";
				case PrinterFaults.RecoverableError:
					return "Surchauffe ou défaut rattrapable";
				case PrinterFaults.CutterError:
					return "Lame bloquée, assimilée bourrage";
				case PrinterFaults.UnrecoverableError:
					return "Défaut matériel";
				case PrinterFaults.PaperNearEnd:
					return "Near-end, impression encore possible";
				case PrinterFaults.PaperOut:
					return "Capteur fin de rouleau";
				default:
					return String.Empty;
			}
		}

		public static String GetCommandHint(PrinterFaults fault)
		{
			switch (fault)
			{
				case PrinterFaults.Offline:
					return "DLE EOT 1, bit 3";
				case PrinterFaults.CoverOpen:
					return "DLE EOT 2, bit 2 ; ASB octet 1 bit 5";
				case PrinterFaults.PaperEndStop:
					return "DLE EOT 2, bit 5";
				case PrinterFaults.Error:
					return "DLE EOT 2, bit 6";
				case PrinterFaults.RecoverableError:
					return "DLE EOT 3, bit 2";
				case PrinterFaults.CutterError:
					return "DLE EOT 3, bit 3";
				case PrinterFaults.UnrecoverableError:
					return "DLE EOT 3, bit 5";
				case PrinterFaults.PaperNearEnd:
					return "DLE EOT 4, bits 2 et 3";
				case PrinterFaults.PaperOut:
					return "DLE EOT 4, bits 5 et 6";
				default:
					return String.Empty;
			}
		}

		/// <summary>Comma-separated labels of the faults set in <paramref name="faults"/>, or "aucun".</summary>
		public static String Describe(PrinterFaults faults)
		{
			List<String> labels = new List<String>();
			foreach (PrinterFaults fault in All)
			{
				if ((faults & fault) != 0)
				{
					labels.Add(GetLabel(fault));
				}
			}

			return labels.Count == 0 ? "aucun" : String.Join(", ", labels);
		}

		/// <summary>
		/// Why the printer cannot print with <paramref name="faults"/> (short text for the PDF header),
		/// or null when printing is possible.
		/// </summary>
		public static String? GetBlockReason(PrinterFaults faults)
		{
			if ((faults & PrintingBlockers) == 0)
			{
				return null;
			}

			if ((faults & PrinterFaults.PaperOut) != 0)
			{
				return "plus de papier";
			}

			if ((faults & PrinterFaults.PaperEndStop) != 0)
			{
				return "arrêt fin de papier";
			}

			if ((faults & PrinterFaults.CoverOpen) != 0)
			{
				return "capot ouvert";
			}

			if ((faults & PrinterFaults.CutterError) != 0)
			{
				return "massicot";
			}

			if ((faults & PrinterFaults.UnrecoverableError) != 0)
			{
				return "erreur irrécupérable";
			}

			if ((faults & PrinterFaults.RecoverableError) != 0)
			{
				return "erreur récupérable";
			}

			return "hors ligne";
		}
	}
}
