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

		private static readonly ConcurrentDictionary<Int32, Char[]> Cache = new();

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
			var codePage = GetWindowsCodePage(escTValue);
			return Cache.GetOrAdd(codePage, Build);
		}

		private static Int32 GetWindowsCodePage(Byte escTValue)
		{
			return escTValue switch
			{
				0 => 437,
				2 => 850,
				3 => 860,
				4 => 863,
				5 => 865,
				16 => 1252,
				17 => 866,
				18 => 852,
				19 => 858,
				_ => 1252,
			};
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

			var table = new Char[256];
			var single = new Byte[1];
			for (var index = 0; index < table.Length; index++)
			{
				if (index < 0x20 || index == 0x7F)
				{
					table[index] = UndecodedByte;
					continue;
				}

				single[0] = (Byte)index;
				var decoded = encoding.GetString(single);
				table[index] = decoded.Length == 1 && decoded[0] != '�' ? decoded[0] : UndecodedByte;
			}

			return table;
		}
	}
}