namespace MimaEmuPrinter.Core.Archive
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using MimaEmuPrinter.Core.Paper;
	using MimaEmuPrinter.Core.Rendering;
	using PdfSharp;
	using PdfSharp.Drawing;
	using PdfSharp.Pdf;

	/// <summary>
	/// Draws the PDF of a day: A4 portrait, two columns of five cartridges (ten tickets per page).
	/// Each cartridge is a thin-bordered box with a grey band (time, source IP, paper, job id, notice)
	/// and the ticket in a monospaced font, centred, at the proportion of the 58 or 80 mm paper.
	/// A ticket longer than its slot is scaled down as a whole, never cut.
	/// </summary>
	public static class PdfDayWriter
	{
		public const Int32 ColumnsPerPage = 2;

		public const Int32 SlotsPerColumn = 5;

		public const Int32 TicketsPerPage = ColumnsPerPage * SlotsPerColumn;

		private const Double PageWidth = 595.276;
		private const Double PageHeight = 841.89;
		private const Double Margin = 28;
		private const Double Gutter = 14;
		private const Double SlotGap = 8;
		private const Double BandHeight = 22;
		private const Double FooterHeight = 14;
		private const Double SlotPadding = 4;
		private const Double PaperPadding = 6;
		private const Double MmToPoint = 72.0 / 25.4;
		private const Double MonoAdvanceProbeSize = 10;

		/// <summary>Writes the PDF of <paramref name="day"/> atomically: temporary file, then replacement.</summary>
		public static void Save(String path, DateTime day, IReadOnlyList<JobRecord> records)
		{
			EmbeddedFontResolver.Install();

			String? directory = Path.GetDirectoryName(path);
			if (!String.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}

			Int32 pageCount = Math.Max(1, (records.Count + TicketsPerPage - 1) / TicketsPerPage);
			String temporaryPath = path + ".tmp";
			using (PdfDocument document = new PdfDocument())
			{
				document.Info.Title = "MimaEmuPrinter - tickets du " + day.ToString("yyyy-MM-dd");
				document.Info.Creator = "MimaEmuPrinter";

				for (Int32 pageIndex = 0; pageIndex < pageCount; pageIndex++)
				{
					PdfPage page = document.AddPage();
					page.Size = PageSize.A4;
					using XGraphics graphics = XGraphics.FromPdfPage(page);
					DrawPage(graphics, day, records, pageIndex, pageCount);
				}

				document.Save(temporaryPath);
			}

			File.Move(temporaryPath, path, true);
		}

		private static void DrawPage(XGraphics graphics, DateTime day, IReadOnlyList<JobRecord> records, Int32 pageIndex, Int32 pageCount)
		{
			Double slotWidth = (PageWidth - (2 * Margin) - Gutter) / ColumnsPerPage;
			Double slotHeight = (PageHeight - (2 * Margin) - FooterHeight - ((SlotsPerColumn - 1) * SlotGap)) / SlotsPerColumn;

			for (Int32 slot = 0; slot < TicketsPerPage; slot++)
			{
				Int32 recordIndex = (pageIndex * TicketsPerPage) + slot;
				if (recordIndex >= records.Count)
				{
					break;
				}

				Int32 column = slot / SlotsPerColumn;
				Int32 row = slot % SlotsPerColumn;
				XRect rectangle = new XRect(
					Margin + (column * (slotWidth + Gutter)),
					Margin + (row * (slotHeight + SlotGap)),
					slotWidth,
					slotHeight);
				DrawSlot(graphics, rectangle, records[recordIndex]);
			}

			XFont footerFont = new XFont(EmbeddedFontResolver.SansFamily, 7, XFontStyleEx.Regular);
			XRect footer = new XRect(Margin, PageHeight - Margin - FooterHeight + 4, PageWidth - (2 * Margin), FooterHeight);
			String footerText = String.Format("MimaEmuPrinter — tickets du {0:yyyy-MM-dd} — page {1}/{2}", day, pageIndex + 1, pageCount);
			graphics.DrawString(footerText, footerFont, XBrushes.Gray, footer, XStringFormats.Center);
		}

		private static void DrawSlot(XGraphics graphics, XRect rectangle, JobRecord record)
		{
			XFont sans = new XFont(EmbeddedFontResolver.SansFamily, 6.5, XFontStyleEx.Regular);
			XFont sansBold = new XFont(EmbeddedFontResolver.SansFamily, 6.5, XFontStyleEx.Bold);

			XSolidBrush bandBrush = new XSolidBrush(XColor.FromArgb(222, 222, 222));
			graphics.DrawRectangle(bandBrush, new XRect(rectangle.X, rectangle.Y, rectangle.Width, BandHeight));
			graphics.DrawRectangle(new XPen(XColors.Gray, 0.5), rectangle);

			PaperProfile profile = PaperProfile.For(record.Paper);
			String header = String.Format(
				"{0:yyyy-MM-dd HH:mm:ss}  ·  {1}  ·  {2} mm / {3} col  ·  {4}",
				record.StartedAt,
				record.Source,
				profile.WidthMm,
				record.Columns,
				record.Id);
			graphics.DrawString(header, sans, XBrushes.Black, new XPoint(rectangle.X + SlotPadding, rectangle.Y + 9), XStringFormats.BaseLineLeft);

			String? notice = record.Notice;
			if (notice != null)
			{
				graphics.DrawString(notice, sansBold, XBrushes.Firebrick, new XPoint(rectangle.X + SlotPadding, rectangle.Y + 18), XStringFormats.BaseLineLeft);
			}

			XRect area = new XRect(
				rectangle.X + SlotPadding,
				rectangle.Y + BandHeight + SlotPadding,
				rectangle.Width - (2 * SlotPadding),
				rectangle.Height - BandHeight - (2 * SlotPadding));
			DrawTicket(graphics, area, record, profile);
		}

		private static void DrawTicket(XGraphics graphics, XRect area, JobRecord record, PaperProfile profile)
		{
			Double paperWidth = profile.WidthMm * MmToPoint;
			Int32 columns = Math.Max(1, record.Columns);

			XFont probe = new XFont(EmbeddedFontResolver.MonoFamily, MonoAdvanceProbeSize, XFontStyleEx.Regular);
			Double advancePerEm = graphics.MeasureString("MMMMMMMMMM", probe).Width / 10 / MonoAdvanceProbeSize;
			Double cellWidth = (paperWidth - (2 * PaperPadding)) / columns;
			Double fontSize = cellWidth / advancePerEm;
			Double lineHeight = fontSize * 1.2;

			Int32 heightUnits = 0;
			foreach (TicketLine line in record.Lines)
			{
				heightUnits += line.HeightUnits;
			}

			Double paperHeight = (Math.Max(heightUnits, 2) * lineHeight) + (2 * PaperPadding);
			Double scale = Math.Min(1.0, Math.Min(area.Width / paperWidth, area.Height / paperHeight));
			Double left = area.X + ((area.Width - (paperWidth * scale)) / 2);

			XGraphicsState state = graphics.Save();
			try
			{
				graphics.TranslateTransform(left, area.Y);
				graphics.ScaleTransform(scale);

				graphics.DrawRectangle(new XPen(XColor.FromArgb(190, 190, 190), 0.5), XBrushes.White, new XRect(0, 0, paperWidth, paperHeight));

				XFont regular = new XFont(EmbeddedFontResolver.MonoFamily, fontSize, XFontStyleEx.Regular);
				XFont bold = new XFont(EmbeddedFontResolver.MonoFamily, fontSize, XFontStyleEx.Bold);
				Double top = PaperPadding;
				foreach (TicketLine line in record.Lines)
				{
					DrawLine(graphics, line, top, paperWidth, columns, cellWidth, fontSize, lineHeight, regular, bold);
					top += line.HeightUnits * lineHeight;
				}
			}
			finally
			{
				graphics.Restore(state);
			}
		}

		private static void DrawLine(
			XGraphics graphics,
			TicketLine line,
			Double top,
			Double paperWidth,
			Int32 columns,
			Double cellWidth,
			Double fontSize,
			Double lineHeight,
			XFont regular,
			XFont bold)
		{
			Double height = line.HeightUnits * lineHeight;
			if (line.Kind == TicketLineKind.Cut)
			{
				XPen dashed = new XPen(XColors.Gray, 0.6) { DashStyle = XDashStyle.Dash };
				graphics.DrawLine(dashed, 0, top + (height / 2), paperWidth, top + (height / 2));
				return;
			}

			if (line.Kind == TicketLineKind.Band)
			{
				XRect band = new XRect(PaperPadding, top + 1, paperWidth - (2 * PaperPadding), height - 2);
				graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(200, 200, 200)), band);
				graphics.DrawString(line.BandText ?? String.Empty, regular, XBrushes.Black, band, XStringFormats.Center);
				return;
			}

			Int32 width = line.Columns;
			Int32 offsetColumns = 0;
			if (line.Alignment == TicketAlignment.Center)
			{
				offsetColumns = Math.Max(0, (columns - width) / 2);
			}
			else if (line.Alignment == TicketAlignment.Right)
			{
				offsetColumns = Math.Max(0, columns - width);
			}

			Double x = PaperPadding + (offsetColumns * cellWidth);
			foreach (TicketRun run in line.Runs)
			{
				Int32 widthFactor = run.DoubleWidth ? 2 : 1;
				Int32 heightFactor = run.DoubleHeight ? 2 : 1;
				Double runTop = top + ((line.HeightUnits - heightFactor) * lineHeight);
				Double baseline = fontSize * 0.87;

				XGraphicsState runState = graphics.Save();
				graphics.TranslateTransform(x, runTop);
				graphics.ScaleTransform(widthFactor, heightFactor);
				graphics.DrawString(
					run.Text,
					run.Bold ? bold : regular,
					run.IsMarker ? XBrushes.Firebrick : XBrushes.Black,
					new XPoint(0, baseline),
					XStringFormats.BaseLineLeft);
				if (run.Underline)
				{
					Double underlineY = baseline + (fontSize * 0.1);
					graphics.DrawLine(new XPen(XColors.Black, fontSize * 0.06), 0, underlineY, run.Text.Length * cellWidth, underlineY);
				}

				graphics.Restore(runState);
				x += run.Columns * cellWidth;
			}
		}
	}
}
