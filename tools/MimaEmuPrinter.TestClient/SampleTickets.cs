namespace MimaEmuPrinter.TestClient
{
	using System;
	using System.IO;
	using System.Text;

	/// <summary>ESC/POS streams a cash register would send, built byte by byte.</summary>
	internal static class SampleTickets
	{
		private const String PortalUrl = "https://portail.mimacafe.example/t/12?token=8f3a1c9d2b7e";

		private static readonly Encoding Pc858 = CreatePc858();

		/// <summary>The reference table ticket: sign, table number, portal URL, QR, covers, service date.</summary>
		public static Byte[] Reference(Int32 columns, Boolean withLogo)
		{
			using MemoryStream stream = new();
			stream.Write([0x1B, 0x40, 0x1B, 0x74, 19]);
			if (withLogo)
			{
				WriteLogo(stream);
			}

			stream.Write([0x1B, 0x61, 0x01, 0x1B, 0x21, 0x30]);
			WriteLine(stream, "MIMA CAFÉ");
			stream.Write([0x1B, 0x21, 0x00]);
			WriteLine(stream, new String('-', columns));
			stream.Write([0x1D, 0x21, 0x11]);
			WriteLine(stream, "TABLE 12");
			stream.Write([0x1D, 0x21, 0x00]);
			WriteLine(stream, new String('-', columns));
			WriteLine(stream, "Scannez pour commander et payer");
			WriteQr(stream, PortalUrl);
			WriteLine(stream, PortalUrl);
			WriteLine(stream, new String('-', columns));
			stream.Write([0x1B, 0x45, 0x01]);
			WriteLine(stream, "Couverts : 4");
			stream.Write([0x1B, 0x45, 0x00]);
			WriteLine(stream, "Service du " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
			stream.Write([0x1B, 0x64, 3]);
			stream.Write([0x1D, 0x56, 0x00]);
			return stream.ToArray();
		}

		/// <summary>A ticket with a left aligned line longer than the paper, to check the truncation marker.</summary>
		public static Byte[] LongLine(Int32 columns)
		{
			using MemoryStream stream = new();
			stream.Write([0x1B, 0x40, 0x1B, 0x74, 19]);
			WriteLine(stream, "Ligne trop longue pour la laize :");
			WriteLine(stream, new String('X', columns + 10));
			WriteLine(stream, "Ligne normale");
			stream.Write([0x1D, 0x56, 0x00]);
			return stream.ToArray();
		}

		/// <summary>Counter ticket with price columns, bold, underline, accents and a barcode.</summary>
		public static Byte[] Receipt(Int32 columns)
		{
			using MemoryStream stream = new();
			stream.Write([0x1B, 0x40, 0x1B, 0x74, 19]);
			stream.Write([0x1B, 0x61, 0x01, 0x1B, 0x45, 0x01]);
			WriteLine(stream, "ADDITION");
			stream.Write([0x1B, 0x45, 0x00, 0x1B, 0x61, 0x00]);
			String[][] items =
			{
				["2 x Café crème", "5,60"],
				["1 x Croque-monsieur", "9,50"],
				["1 x Jus d'orange pressé", "4,80"],
				["1 x Tarte aux pommes", "6,20"],
			};
			foreach (String[] item in items)
			{
				WriteLine(stream, item[0].PadRight(columns - item[1].Length) + item[1]);
			}

			WriteLine(stream, new String('=', columns));
			stream.Write([0x1B, 0x2D, 0x01, 0x1B, 0x21, 0x08]);
			WriteLine(stream, "TOTAL TTC".PadRight(columns - 7) + "26,10 €".PadLeft(7));
			stream.Write([0x1B, 0x2D, 0x00, 0x1B, 0x21, 0x00]);
			stream.Write([0x1D, 0x6B, 0x04]);
			stream.Write(Encoding.ASCII.GetBytes("12345678"));
			stream.WriteByte(0x00);
			WriteLine(stream, "Merci de votre visite");
			stream.Write([0x1B, 0x64, 3]);
			stream.Write([0x1D, 0x56, 0x00]);
			return stream.ToArray();
		}

		private static void WriteLogo(MemoryStream stream)
		{
			var bytesPerRow = 48;
			var rows = 90;
			stream.Write([0x1D, 0x76, 0x30, 0x00, (Byte)bytesPerRow, 0x00, (Byte)rows, 0x00]);
			var bitmap = new Byte[bytesPerRow * rows];
			Array.Fill(bitmap, (Byte)0xAA);
			stream.Write(bitmap);
		}

		private static void WriteQr(MemoryStream stream, String data)
		{
			stream.Write([0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00]);
			stream.Write([0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x06]);
			stream.Write([0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31]);
			var length = data.Length + 3;
			stream.Write([0x1D, 0x28, 0x6B, (Byte)(length & 0xFF), (Byte)(length >> 8), 0x31, 0x50, 0x30]);
			stream.Write(Encoding.ASCII.GetBytes(data));
			stream.Write([0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30]);
		}

		private static void WriteLine(MemoryStream stream, String text)
		{
			stream.Write(Pc858.GetBytes(text));
			stream.WriteByte(0x0A);
		}

		private static Encoding CreatePc858()
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			return Encoding.GetEncoding(858);
		}
	}
}