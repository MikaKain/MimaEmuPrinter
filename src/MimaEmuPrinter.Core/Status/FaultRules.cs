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
			return fault switch
			{
				PrinterFaults.PaperOut => PrinterFaults.PaperEndStop | PrinterFaults.Error | PrinterFaults.Offline,
				PrinterFaults.CoverOpen => PrinterFaults.Offline,
				PrinterFaults.CutterError or PrinterFaults.RecoverableError or PrinterFaults.UnrecoverableError => PrinterFaults.Error | PrinterFaults.Offline,
				_ => PrinterFaults.None,
			};
		}

		/// <summary>Union of the consequences of every fault set in <paramref name="faults"/>.</summary>
		public static PrinterFaults GetImplied(PrinterFaults faults)
		{
			PrinterFaults implied = PrinterFaults.None;
			foreach (PrinterFaults fault in All)
				if ((faults & fault) != 0)
					implied |= GetConsequences(fault);

			return implied;
		}

		public static String GetLabel(PrinterFaults fault)
		{
			return fault switch
			{
				PrinterFaults.Offline => "Offline",
				PrinterFaults.CoverOpen => "Capot ouvert",
				PrinterFaults.PaperEndStop => "Arrêt fin de papier",
				PrinterFaults.Error => "Erreur",
				PrinterFaults.RecoverableError => "Erreur récupérable",
				PrinterFaults.CutterError => "Erreur massicot",
				PrinterFaults.UnrecoverableError => "Erreur irrécupérable",
				PrinterFaults.PaperNearEnd => "Papier bientôt fini",
				PrinterFaults.PaperOut => "Plus de papier",
				_ => fault.ToString(),
			};
		}

		public static String GetEffect(PrinterFaults fault)
		{
			return fault switch
			{
				PrinterFaults.Offline => "Imprimante hors ligne",
				PrinterFaults.CoverOpen => "Capot papier ouvert",
				PrinterFaults.PaperEndStop => "Impression stoppée, plus de papier",
				PrinterFaults.Error => "Erreur présente",
				PrinterFaults.RecoverableError => "Surchauffe ou défaut rattrapable",
				PrinterFaults.CutterError => "Lame bloquée, assimilée bourrage",
				PrinterFaults.UnrecoverableError => "Défaut matériel",
				PrinterFaults.PaperNearEnd => "Near-end, impression encore possible",
				PrinterFaults.PaperOut => "Capteur fin de rouleau",
				_ => String.Empty,
			};
		}

		public static String GetCommandHint(PrinterFaults fault)
		{
			return fault switch
			{
				PrinterFaults.Offline => "DLE EOT 1, bit 3",
				PrinterFaults.CoverOpen => "DLE EOT 2, bit 2 ; ASB octet 1 bit 5",
				PrinterFaults.PaperEndStop => "DLE EOT 2, bit 5",
				PrinterFaults.Error => "DLE EOT 2, bit 6",
				PrinterFaults.RecoverableError => "DLE EOT 3, bit 2",
				PrinterFaults.CutterError => "DLE EOT 3, bit 3",
				PrinterFaults.UnrecoverableError => "DLE EOT 3, bit 5",
				PrinterFaults.PaperNearEnd => "DLE EOT 4, bits 2 et 3",
				PrinterFaults.PaperOut => "DLE EOT 4, bits 5 et 6",
				_ => String.Empty,
			};
		}

		/// <summary>Comma-separated labels of the faults set in <paramref name="faults"/>, or "aucun".</summary>
		public static String Describe(PrinterFaults faults)
		{
			var labels = new List<String>();
			foreach (PrinterFaults fault in All)
				if ((faults & fault) != 0)
					labels.Add(GetLabel(fault));

			return labels.Count == 0 ? "aucun" : String.Join(", ", labels);
		}

		/// <summary>
		/// Why the printer cannot print with <paramref name="faults"/> (short text for the PDF header),
		/// or null when printing is possible.
		/// </summary>
		public static String? GetBlockReason(PrinterFaults faults)
		{
			if ((faults & PrintingBlockers) == 0)
				return null;

			if ((faults & PrinterFaults.PaperOut) != 0)
				return "plus de papier";

			if ((faults & PrinterFaults.PaperEndStop) != 0)
				return "arrêt fin de papier";

			if ((faults & PrinterFaults.CoverOpen) != 0)
				return "capot ouvert";

			if ((faults & PrinterFaults.CutterError) != 0)
				return "massicot";

			if ((faults & PrinterFaults.UnrecoverableError) != 0)
				return "erreur irrécupérable";

			if ((faults & PrinterFaults.RecoverableError) != 0)
				return "erreur récupérable";

			return "hors ligne";
		}
	}
}