namespace MimaEmuPrinter.Core.Archive
{
	using System;
	using System.Collections.Generic;
	using MimaEmuPrinter.Core.Paper;
	using MimaEmuPrinter.Core.Rendering;

	/// <summary>Everything the PDF needs to draw one ticket cartridge; also stored as JSON next to the raw stream.</summary>
	public sealed class JobRecord
	{
		public String Id { get; init; } = String.Empty;

		/// <summary>Arrival time, local time.</summary>
		public DateTime StartedAt { get; init; }

		public DateTime ClosedAt { get; init; }

		/// <summary>IP address of the cash register.</summary>
		public String Source { get; init; } = String.Empty;

		public PaperWidth Paper { get; init; }

		public Int32 Columns { get; init; }

		public JobOutcome Outcome { get; init; }

		public String? Reason { get; init; }

		public Int32 ByteCount { get; init; }

		public Int32 IgnoredCommands { get; init; }

		public String ActiveFaults { get; init; } = String.Empty;

		public IReadOnlyList<TicketLine> Lines { get; init; } = Array.Empty<TicketLine>();

		/// <summary>Header notice of the cartridge ("non imprimé : plus de papier"), or null.</summary>
		public String? Notice
		{
			get
			{
				switch (Outcome)
				{
					case JobOutcome.NotPrinted:
						return "non imprimé : " + Reason;
					case JobOutcome.Truncated:
						return "tronqué : " + Reason;
					default:
						return null;
				}
			}
		}
	}
}
