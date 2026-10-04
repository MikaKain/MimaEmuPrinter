namespace MimaEmuPrinter.Tests
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using MimaEmuPrinter.Core.Paper;
	using MimaEmuPrinter.Core.Protocol;
	using MimaEmuPrinter.Core.Rendering;
	using Xunit;

	public sealed class TicketBuilderTests
	{
		[Fact]
		public void CenteredLineKeepsItsAlignmentAndText()
		{
			Ticket ticket = Render(PaperProfile.Mm80Col42, 0x1B, 0x61, 0x01, "TABLE 12\n");

			TicketLine line = Assert.Single(ticket.Lines);
			Assert.Equal(TicketAlignment.Center, line.Alignment);
			Assert.Equal("TABLE 12", line.Text);
		}

		[Fact]
		public void DoubleWidthCharactersOccupyTwoColumns()
		{
			Ticket ticket = Render(PaperProfile.Mm80Col42, 0x1D, 0x21, 0x10, "AB\n");

			TicketLine line = Assert.Single(ticket.Lines);
			Assert.Equal(4, line.Columns);
			Assert.True(line.Runs[0].DoubleWidth);
			Assert.False(line.Runs[0].DoubleHeight);
		}

		[Fact]
		public void EscExclamationSetsBoldDoubleAndUnderline()
		{
			Ticket ticket = Render(PaperProfile.Mm80Col42, 0x1B, 0x21, 0xB8, "X\n");

			TicketRun run = Assert.Single(Assert.Single(ticket.Lines).Runs);
			Assert.True(run.Bold);
			Assert.True(run.DoubleHeight);
			Assert.True(run.DoubleWidth);
			Assert.True(run.Underline);
		}

		[Fact]
		public void LongTextIsTruncatedWithAMarkerAndReported()
		{
			TicketBuilder builder = NewBuilder(PaperProfile.Mm58Col32);
			List<ColumnOverflowEventArgs> overflows = new List<ColumnOverflowEventArgs>();
			builder.ColumnOverflow += (sender, e) => overflows.Add(e);

			Feed(builder, new String('A', 40) + "\n");

			TicketLine line = Assert.Single(builder.Snapshot().Lines);
			Assert.Equal(32, line.Columns);
			Assert.True(line.Runs[^1].IsMarker);
			Assert.Equal(new String('A', 31), line.Runs[0].Text);
			ColumnOverflowEventArgs overflow = Assert.Single(overflows);
			Assert.Equal(1, overflow.LineNumber);
			Assert.Equal(40, overflow.Length);
		}

		[Theory]
		[InlineData(32, 31)]
		[InlineData(42, 41)]
		[InlineData(48, 47)]
		public void TheSameStreamTruncatesDifferentlyOnEachPaper(Int32 columns, Int32 kept)
		{
			PaperProfile profile = PaperProfile.All.Single(p => p.Columns == columns);

			Ticket ticket = Render(profile, new String('x', 60) + "\n");

			TicketLine line = Assert.Single(ticket.Lines);
			Assert.Equal(kept, line.Runs[0].Text.Length);
			Assert.Equal(columns, line.Columns);
		}

		[Fact]
		public void TextExactlyAsWideAsThePaperIsNotTruncated()
		{
			TicketBuilder builder = NewBuilder(PaperProfile.Mm80Col42);
			Int32 overflows = 0;
			builder.ColumnOverflow += (sender, e) => overflows++;

			Feed(builder, new String('B', 42) + "\n");

			Assert.Equal(0, overflows);
			Assert.Equal(42, Assert.Single(builder.Snapshot().Lines).Columns);
		}

		[Fact]
		public void UrlLinesWrapInsteadOfBeingTruncated()
		{
			String url = "https://portail.mimacafe.example/commande/table/12?token=abcdef0123456789";
			TicketBuilder builder = NewBuilder(PaperProfile.Mm58Col32);
			Int32 overflows = 0;
			builder.ColumnOverflow += (sender, e) => overflows++;

			Feed(builder, url + "\n");

			IReadOnlyList<TicketLine> lines = builder.Snapshot().Lines;
			Assert.True(lines.Count >= 3);
			Assert.All(lines, line => Assert.True(line.Columns <= 32));
			Assert.Equal(url, String.Concat(lines.Select(line => line.Text)));
			Assert.Equal(0, overflows);
		}

		[Fact]
		public void CarriageReturnLineFeedIsASingleLineBreak()
		{
			Ticket ticket = Render(PaperProfile.Mm80Col42, "one\r\ntwo\r\n");

			Assert.Equal(new[] { "one", "two" }, ticket.Lines.Select(line => line.Text).ToArray());
		}

		[Fact]
		public void BlankLinesAreKept()
		{
			Ticket ticket = Render(PaperProfile.Mm80Col42, "a\n\nb\n");

			Assert.Equal(new[] { "a", String.Empty, "b" }, ticket.Lines.Select(line => line.Text).ToArray());
		}

		[Fact]
		public void Pc858DecodesAccentsAndEuro()
		{
			TicketBuilder builder = NewBuilder(PaperProfile.Mm80Col42);
			builder.SetCodePage(19);

			foreach (Byte value in new Byte[] { 0x82, 0x85, 0xD5, 0x0A })
			{
				if (value == 0x0A)
				{
					builder.LineFeed();
				}
				else
				{
					builder.AppendByte(value);
				}
			}

			Assert.Equal("éà€", Assert.Single(builder.Snapshot().Lines).Text);
		}

		[Fact]
		public void UndecodedBytesAreShownAsMiddleDot()
		{
			TicketBuilder builder = NewBuilder(PaperProfile.Mm80Col42);

			builder.AppendByte(0x7F);
			builder.LineFeed();

			Assert.Equal("·", Assert.Single(builder.Snapshot().Lines).Text);
		}

		[Fact]
		public void UnknownCodePageFallsBackToWindows1252()
		{
			TicketBuilder builder = NewBuilder(PaperProfile.Mm80Col42);
			builder.SetCodePage(200);

			builder.AppendByte(0x80);
			builder.LineFeed();

			Assert.Equal("€", Assert.Single(builder.Snapshot().Lines).Text);
		}

		[Fact]
		public void LineBeingTypedIsVisibleInTheSnapshot()
		{
			TicketBuilder builder = NewBuilder(PaperProfile.Mm80Col42);

			Feed(builder, "partial");

			Assert.Equal("partial", Assert.Single(builder.Snapshot().Lines).Text);
		}

		[Fact]
		public void CutMarkAndBandsAreSeparateLineKinds()
		{
			TicketBuilder builder = NewBuilder(PaperProfile.Mm80Col42);

			Feed(builder, "x\n");
			builder.AddBand("logo 384 × 120", 4);
			builder.AddCutMark();

			IReadOnlyList<TicketLine> lines = builder.Snapshot().Lines;
			Assert.Equal(new[] { TicketLineKind.Text, TicketLineKind.Band, TicketLineKind.Cut }, lines.Select(line => line.Kind).ToArray());
			Assert.Equal(4, lines[1].HeightUnits);
		}

		[Fact]
		public void NoticeLinesAreWrappedToThePaperWidth()
		{
			TicketBuilder builder = NewBuilder(PaperProfile.Mm58Col32);

			builder.AddNotice("[QR] " + new String('q', 60));

			Assert.All(builder.Snapshot().Lines, line => Assert.True(line.Columns <= 32));
			Assert.True(builder.Snapshot().Lines.Count >= 2);
		}

		[Fact]
		public void FeedAddsBlankLines()
		{
			TicketBuilder builder = NewBuilder(PaperProfile.Mm80Col42);

			Feed(builder, "a");
			builder.FeedLines(3);

			Assert.Equal(4, builder.Snapshot().Lines.Count);
		}

		[Fact]
		public void AlignmentIsIgnoredInTheMiddleOfALine()
		{
			TicketBuilder builder = NewBuilder(PaperProfile.Mm80Col42);

			Feed(builder, "abc");
			builder.SetAlignment(2);
			Feed(builder, "def\n");

			Assert.Equal(TicketAlignment.Left, Assert.Single(builder.Snapshot().Lines).Alignment);
		}

		private static TicketBuilder NewBuilder(PaperProfile profile)
		{
			TicketBuilder builder = new TicketBuilder();
			builder.BeginTicket(profile);
			return builder;
		}

		private static void Feed(TicketBuilder builder, String text)
		{
			foreach (Byte value in Encoding.ASCII.GetBytes(text))
			{
				switch (value)
				{
					case 0x0A:
						builder.LineFeed();
						break;
					case 0x0D:
						builder.CarriageReturn();
						break;
					default:
						builder.AppendByte(value);
						break;
				}
			}
		}

		private static Ticket Render(PaperProfile profile, String text)
		{
			TicketBuilder builder = NewBuilder(profile);
			Feed(builder, text);
			return builder.Snapshot();
		}

		private static Ticket Render(PaperProfile profile, Int32 first, Int32 second, Int32 third, String text)
		{
			TicketBuilder builder = NewBuilder(profile);
			EscPosParser parser = new EscPosParser(new BuilderHandler(builder));
			parser.Feed(new Byte[] { (Byte)first, (Byte)second, (Byte)third });
			parser.Feed(Encoding.ASCII.GetBytes(text));
			return builder.Snapshot();
		}

		// Minimal handler wiring the parser to a builder, to test style commands end to end.
		private sealed class BuilderHandler : IEscPosHandler
		{
			private readonly TicketBuilder builder;

			public BuilderHandler(TicketBuilder builder)
			{
				this.builder = builder;
			}

			public void PrintableByte(Byte value)
			{
				builder.AppendByte(value);
			}

			public void LineFeed()
			{
				builder.LineFeed();
			}

			public void CarriageReturn()
			{
				builder.CarriageReturn();
			}

			public void HorizontalTab()
			{
				builder.Tab();
			}

			public void Initialize()
			{
				builder.Initialize();
			}

			public void SetPrintMode(Byte value)
			{
				builder.SetPrintMode(value);
			}

			public void SetCharacterSize(Byte value)
			{
				builder.SetCharacterSize(value);
			}

			public void SetAlignment(Byte value)
			{
				builder.SetAlignment(value);
			}

			public void SetEmphasis(Byte value)
			{
				builder.SetEmphasis(value);
			}

			public void SetUnderline(Byte value)
			{
				builder.SetUnderline(value);
			}

			public void SetCodePage(Byte value)
			{
				builder.SetCodePage(value);
			}

			public void FeedLines(Int32 lines)
			{
				builder.FeedLines(lines);
			}

			public void FeedDots(Int32 dots)
			{
				builder.FeedDots(dots);
			}

			public void Cut()
			{
				builder.AddCutMark();
			}

			public void PrintBarcode(String data)
			{
				builder.AddNotice("[BAR] " + data);
			}

			public void PrintQrCode(String data)
			{
				builder.AddNotice("[QR] " + data);
			}

			public void PrintRaster(Int32 widthDots, Int32 heightDots)
			{
				builder.AddBand("logo", 1);
			}

			public void PulseDrawer(Byte pin)
			{
			}

			public void IgnoreCommand(String name)
			{
			}

			public void RealTime(StatusRequest request)
			{
			}
		}
	}
}
