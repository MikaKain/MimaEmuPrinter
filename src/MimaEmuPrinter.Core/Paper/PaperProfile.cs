namespace MimaEmuPrinter.Core.Paper
{
	using System;
	using System.Collections.Generic;

	/// <summary>The three paper options selectable in the emulator.</summary>
	public enum PaperWidth
	{
		Mm58Col32,
		Mm80Col42,
		Mm80Col48,
	}

	/// <summary>Geometry of a paper option: roll width, text columns and print head dots.</summary>
	public sealed record PaperProfile(PaperWidth Width, String DisplayName, Int32 WidthMm, Int32 Columns, Int32 HeadDots, String Usage)
	{
		public static readonly PaperProfile Mm58Col32 = new(PaperWidth.Mm58Col32, "58 mm (32 caractères)", 58, 32, 384, "Ticket étroit, portable");

		public static readonly PaperProfile Mm80Col42 = new(PaperWidth.Mm80Col42, "80 mm (42 caractères)", 80, 42, 576, "Réglage cuisine le plus courant");

		public static readonly PaperProfile Mm80Col48 = new(PaperWidth.Mm80Col48, "80 mm (48 caractères)", 80, 48, 576, "Police condensée, additions");

		public static IReadOnlyList<PaperProfile> All { get; } = [Mm58Col32, Mm80Col42, Mm80Col48];

		public static PaperProfile For(PaperWidth width)
		{
			return width switch
			{
				PaperWidth.Mm58Col32 => Mm58Col32,
				PaperWidth.Mm80Col48 => Mm80Col48,
				_ => Mm80Col42,
			};
		}

		public override String ToString()
		{
			return DisplayName;
		}
	}
}