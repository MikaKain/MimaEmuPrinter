namespace MimaEmuPrinter.Tests
{
	using System;
	using System.Collections.Generic;
	using System.Text;
	using MimaEmuPrinter.Core.Protocol;
	using Xunit;

	public sealed class EscPosParserTests
	{
		[Fact]
		public void TextAndLineFeedsAreForwarded()
		{
			RecordingHandler handler = Parse(Bytes("Hi\n"));

			Assert.Equal(new[] { "char:H", "char:i", "LF" }, handler.Events);
		}

		[Fact]
		public void CommandsSplitAcrossChunksAreStillRecognised()
		{
			RecordingHandler handler = new RecordingHandler();
			EscPosParser parser = new EscPosParser(handler);

			parser.Feed(new Byte[] { 0x1B });
			parser.Feed(new Byte[] { 0x61 });
			parser.Feed(new Byte[] { 0x01, 0x41 });

			Assert.Equal(new[] { "align:1", "char:A" }, handler.Events);
		}

		[Fact]
		public void StyleCommandsCarryTheirParameter()
		{
			RecordingHandler handler = Parse(0x1B, 0x40, 0x1B, 0x21, 0x38, 0x1D, 0x21, 0x11, 0x1B, 0x45, 0x01, 0x1B, 0x2D, 0x02, 0x1B, 0x74, 19);

			Assert.Equal(new[] { "init", "mode:56", "size:17", "emph:1", "under:2", "codepage:19" }, handler.Events);
		}

		[Fact]
		public void FeedAndDrawerCommands()
		{
			RecordingHandler handler = Parse(0x1B, 0x64, 3, 0x1B, 0x4A, 60, 0x1B, 0x70, 0, 25, 250);

			Assert.Equal(new[] { "feedlines:3", "feeddots:60", "drawer:0" }, handler.Events);
		}

		[Theory]
		[InlineData(1)]
		[InlineData(2)]
		[InlineData(3)]
		[InlineData(4)]
		public void DleEotIsExtractedAndReported(Int32 n)
		{
			RecordingHandler handler = Parse(0x10, 0x04, (Byte)n);

			Assert.Equal(new[] { "rt:DleEot:" + n }, handler.Events);
		}

		[Fact]
		public void DleEotIsExtractedFromTheMiddleOfPrintData()
		{
			RecordingHandler handler = Parse(Bytes("A"), new Byte[] { 0x10, 0x04, 0x02 }, Bytes("B"));

			Assert.Equal(new[] { "char:A", "rt:DleEot:2", "char:B" }, handler.Events);
		}

		[Fact]
		public void DleEnqGsRGsIAndGsAAreReported()
		{
			RecordingHandler handler = Parse(0x10, 0x05, 0x02, 0x1D, 0x72, 0x01, 0x1D, 0x49, 0x43, 0x1D, 0x61, 0xFF);

			Assert.Equal(new[] { "rt:DleEnq:2", "rt:GsR:1", "rt:GsI:67", "rt:GsA:255" }, handler.Events);
		}

		[Theory]
		[InlineData(new Byte[] { 0x1D, 0x56, 0x00 })]
		[InlineData(new Byte[] { 0x1D, 0x56, 0x31 })]
		[InlineData(new Byte[] { 0x1D, 0x56, 0x42, 0x03 })]
		public void CutVariantsConsumeTheirParameters(Byte[] cut)
		{
			RecordingHandler handler = Parse(cut, Bytes("X"));

			Assert.Equal(new[] { "cut", "char:X" }, handler.Events);
		}

		[Fact]
		public void QrCodeIsStoredThenPrinted()
		{
			String url = "https://portail.example/t/12";
			List<Byte> bytes = new List<Byte>();
			bytes.AddRange(new Byte[] { 0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 });
			bytes.AddRange(new Byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x06 });
			bytes.AddRange(new Byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31 });
			Int32 length = url.Length + 3;
			bytes.AddRange(new Byte[] { 0x1D, 0x28, 0x6B, (Byte)(length & 0xFF), (Byte)(length >> 8), 0x31, 0x50, 0x30 });
			bytes.AddRange(Encoding.ASCII.GetBytes(url));
			bytes.AddRange(new Byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30 });

			RecordingHandler handler = Parse(bytes.ToArray());

			Assert.Equal(new[] { "qr:" + url }, handler.Events);
		}

		[Fact]
		public void BarcodeFormatsAAndB()
		{
			List<Byte> bytes = new List<Byte>();
			bytes.AddRange(new Byte[] { 0x1D, 0x6B, 0x04 });
			bytes.AddRange(Encoding.ASCII.GetBytes("12345"));
			bytes.Add(0x00);
			bytes.AddRange(new Byte[] { 0x1D, 0x6B, 0x49, 0x05 });
			bytes.AddRange(Encoding.ASCII.GetBytes("ABCDE"));
			bytes.AddRange(Bytes("Z"));

			RecordingHandler handler = Parse(bytes.ToArray());

			Assert.Equal(new[] { "bar:12345", "bar:ABCDE", "char:Z" }, handler.Events);
		}

		[Fact]
		public void RasterBitmapIsReportedAndItsDataSkippedEvenIfItLooksLikeCommands()
		{
			Int32 bytesPerRow = 4;
			Int32 rows = 3;
			List<Byte> bytes = new List<Byte> { 0x1D, 0x76, 0x30, 0x00, (Byte)bytesPerRow, 0x00, (Byte)rows, 0x00 };
			for (Int32 index = 0; index < bytesPerRow * rows; index++)
			{
				bytes.Add(index % 2 == 0 ? (Byte)0x10 : (Byte)0x04);
			}

			bytes.AddRange(Bytes("K"));

			RecordingHandler handler = Parse(bytes.ToArray());

			Assert.Equal(new[] { "raster:32x3", "char:K" }, handler.Events);
		}

		[Fact]
		public void UnknownSequencesAreConsumedAndCountedAsIgnored()
		{
			RecordingHandler handler = Parse(0x1B, 0x4D, 0x01, 0x1D, 0x42, 0x01, 0x1B, 0x7A, 0x1D, 0x28, 0x41, 0x02, 0x00, 0x30, 0x01, 0x18);

			Assert.Equal(new[] { "ignored:ESC M", "ignored:GS B", "ignored:ESC z", "ignored:GS ( A", "ignored:0x18" }, handler.Events);
		}

		[Fact]
		public void RandomGarbageNeverThrows()
		{
			Random random = new Random(1234);
			Byte[] garbage = new Byte[20000];
			random.NextBytes(garbage);
			RecordingHandler handler = new RecordingHandler();
			EscPosParser parser = new EscPosParser(handler);

			Exception? failure = Record.Exception(() => parser.Feed(garbage));

			Assert.Null(failure);
		}

		[Fact]
		public void NulBytesAreSilentlySkipped()
		{
			RecordingHandler handler = Parse(0x00, 0x00, 0x41);

			Assert.Equal(new[] { "char:A" }, handler.Events);
		}

		private static Byte[] Bytes(String text)
		{
			return Encoding.ASCII.GetBytes(text);
		}

		private static RecordingHandler Parse(params Byte[] data)
		{
			RecordingHandler handler = new RecordingHandler();
			new EscPosParser(handler).Feed(data);
			return handler;
		}

		private static RecordingHandler Parse(params Byte[][] chunks)
		{
			RecordingHandler handler = new RecordingHandler();
			EscPosParser parser = new EscPosParser(handler);
			foreach (Byte[] chunk in chunks)
			{
				parser.Feed(chunk);
			}

			return handler;
		}

		private sealed class RecordingHandler : IEscPosHandler
		{
			public List<String> Events { get; } = new List<String>();

			public void PrintableByte(Byte value)
			{
				Events.Add("char:" + (Char)value);
			}

			public void LineFeed()
			{
				Events.Add("LF");
			}

			public void CarriageReturn()
			{
				Events.Add("CR");
			}

			public void HorizontalTab()
			{
				Events.Add("HT");
			}

			public void Initialize()
			{
				Events.Add("init");
			}

			public void SetPrintMode(Byte value)
			{
				Events.Add("mode:" + value);
			}

			public void SetCharacterSize(Byte value)
			{
				Events.Add("size:" + value);
			}

			public void SetAlignment(Byte value)
			{
				Events.Add("align:" + value);
			}

			public void SetEmphasis(Byte value)
			{
				Events.Add("emph:" + value);
			}

			public void SetUnderline(Byte value)
			{
				Events.Add("under:" + value);
			}

			public void SetCodePage(Byte value)
			{
				Events.Add("codepage:" + value);
			}

			public void FeedLines(Int32 lines)
			{
				Events.Add("feedlines:" + lines);
			}

			public void FeedDots(Int32 dots)
			{
				Events.Add("feeddots:" + dots);
			}

			public void Cut()
			{
				Events.Add("cut");
			}

			public void PrintBarcode(String data)
			{
				Events.Add("bar:" + data);
			}

			public void PrintQrCode(String data)
			{
				Events.Add("qr:" + data);
			}

			public void PrintRaster(Int32 widthDots, Int32 heightDots)
			{
				Events.Add("raster:" + widthDots + "x" + heightDots);
			}

			public void PulseDrawer(Byte pin)
			{
				Events.Add("drawer:" + pin);
			}

			public void IgnoreCommand(String name)
			{
				Events.Add("ignored:" + name);
			}

			public void RealTime(StatusRequest request)
			{
				Events.Add("rt:" + request.Kind + ":" + request.Value);
			}
		}
	}
}
