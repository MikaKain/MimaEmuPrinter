namespace MimaEmuPrinter.Core.Protocol
{
	using System;
	using System.Collections.Concurrent;
	using System.Text;

	/// <summary>
	/// Byte to character tables for the ESC t code pages. PC858 is the default, Windows-1252 the fallback.
	/// Bytes that cannot be decoded are shown as a middle dot.
	/// </summary>
	public static class CodePageTable
	{
		public const Char UndecodedByte = '·';

		public const Byte Pc858 = 19;

		private static readonly ConcurrentDictionary<Int32, Char[]> Cache = new ConcurrentDictionary<Int32, Char[]>();

		static CodePageTable()
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		}

		public static Char[] Default
		{
			get { return Get(Pc858); }
		}

		/// <summary>Table of the 256 characters for an ESC t value; unknown values fall back to Windows-1252.</summary>
		public static Char[] Get(Byte escTValue)
		{
			Int32 codePage = GetWindowsCodePage(escTValue);
			return Cache.GetOrAdd(codePage, Build);
		}

		private static Int32 GetWindowsCodePage(Byte escTValue)
		{
			switch (escTValue)
			{
				case 0:
					return 437;
				case 2:
					return 850;
				case 3:
					return 860;
				case 4:
					return 863;
				case 5:
					return 865;
				case 16:
					return 1252;
				case 17:
					return 866;
				case 18:
					return 852;
				case 19:
					return 858;
				default:
					return 1252;
			}
		}

		private static Char[] Build(Int32 codePage)
		{
			Encoding encoding;
			try
			{
				encoding = Encoding.GetEncoding(codePage, EncoderFallback.ReplacementFallback, new DecoderReplacementFallback(UndecodedByte.ToString()));
			}
			catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
			{
				encoding = Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, new DecoderReplacementFallback(UndecodedByte.ToString()));
			}

			Char[] table = new Char[256];
			Byte[] single = new Byte[1];
			for (Int32 index = 0; index < table.Length; index++)
			{
				if (index < 0x20 || index == 0x7F)
				{
					table[index] = UndecodedByte;
					continue;
				}

				single[0] = (Byte)index;
				String decoded = encoding.GetString(single);
				table[index] = decoded.Length == 1 && decoded[0] != '�' ? decoded[0] : UndecodedByte;
			}

			return table;
		}
	}
}
