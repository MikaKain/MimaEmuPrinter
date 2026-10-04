namespace MimaEmuPrinter.Core.Rendering
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text.Json.Serialization;
	using MimaEmuPrinter.Core.Paper;

	public enum TicketAlignment
	{
		Left,
		Center,
		Right,
	}

	public enum TicketLineKind
	{
		/// <summary>A text line made of styled runs.</summary>
		Text,

		/// <summary>A grey band standing for a raster bitmap (logo).</summary>
		Band,

		/// <summary>The cut mark (GS V).</summary>
		Cut,
	}

	/// <summary>How a job ended up on the virtual paper.</summary>
	public enum JobOutcome
	{
		Printed,
		NotPrinted,
		Truncated,
	}

	/// <summary>A piece of a line sharing one style. A double width character occupies two columns.</summary>
	public sealed record TicketRun(
		String Text,
		Boolean Bold = false,
		Boolean Underline = false,
		Boolean DoubleWidth = false,
		Boolean DoubleHeight = false,
		Boolean IsMarker = false)
	{
		[JsonIgnore]
		public Int32 Columns
		{
			get { return Text.Length * (DoubleWidth ? 2 : 1); }
		}
	}

	public sealed record TicketLine(
		TicketLineKind Kind,
		TicketAlignment Alignment,
		IReadOnlyList<TicketRun> Runs,
		String? BandText = null,
		Int32 BandHeightLines = 1)
	{
		/// <summary>Logical width of the line, in columns.</summary>
		[JsonIgnore]
		public Int32 Columns
		{
			get
			{
				Int32 total = 0;
				foreach (TicketRun run in Runs)
				{
					total += run.Columns;
				}

				return total;
			}
		}

		/// <summary>Height of the line in text line units (2 when a double height run is present).</summary>
		[JsonIgnore]
		public Int32 HeightUnits
		{
			get
			{
				switch (Kind)
				{
					case TicketLineKind.Band:
						return Math.Max(1, BandHeightLines);
					case TicketLineKind.Cut:
						return 1;
					default:
						foreach (TicketRun run in Runs)
						{
							if (run.DoubleHeight)
							{
								return 2;
							}
						}

						return 1;
				}
			}
		}

		/// <summary>Plain text of the line (no padding), mostly for tests and logs.</summary>
		[JsonIgnore]
		public String Text
		{
			get
			{
				if (Kind == TicketLineKind.Band)
				{
					return BandText ?? String.Empty;
				}

				return String.Concat(Runs.Select(run => run.Text));
			}
		}
	}

	public sealed record Ticket(Int32 Columns, IReadOnlyList<TicketLine> Lines);

	/// <summary>Snapshot of the ticket being printed (or just closed), published to the display.</summary>
	public sealed record TicketUpdate(
		String JobId,
		Ticket Ticket,
		PaperWidth Paper,
		Boolean IsClosed,
		JobOutcome Outcome,
		String? Reason)
	{
		/// <summary>Text for the "not printed" / "truncated" notice, or null for a normal ticket.</summary>
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
