namespace MimaEmuPrinter.App.Controls
{
	using System;
	using System.Globalization;
	using Avalonia;
	using Avalonia.Controls;
	using Avalonia.Media;
	using MimaEmuPrinter.Core.Rendering;

	/// <summary>
	/// Draws the virtual ticket: white paper strip with the proportion of the chosen paper width,
	/// monospaced text exactly `Columns` cells wide, double width / double height runs, bold,
	/// underline, grey logo bands, cut mark and the red truncation marker.
	/// </summary>
	public sealed class TicketView : Control
	{
		public static readonly StyledProperty<Ticket?> TicketProperty =
			AvaloniaProperty.Register<TicketView, Ticket?>(nameof(Ticket));

		public static readonly StyledProperty<Int32> ColumnsProperty =
			AvaloniaProperty.Register<TicketView, Int32>(nameof(Columns), 42);

		public static readonly StyledProperty<Int32> PaperWidthMmProperty =
			AvaloniaProperty.Register<TicketView, Int32>(nameof(PaperWidthMm), 80);

		private const Double PixelsPerMm = 4.6;
		private const Double PaddingX = 10;
		private const Double PaddingY = 12;
		private const Double MinimumPaperHeight = 140;
		private const Double LineHeightFactor = 1.25;

		private static readonly FontFamily MonoFamily = new FontFamily("avares://MimaEmuPrinter/Assets/Fonts#Liberation Mono");
		private static readonly Typeface RegularFace = new Typeface(MonoFamily);
		private static readonly Typeface BoldFace = new Typeface(MonoFamily, FontStyle.Normal, FontWeight.Bold);
		private static readonly Pen PaperBorder = new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 200)), 1);
		private static readonly Pen CutPen = new Pen(Brushes.Gray, 1, new DashStyle(new Double[] { 4, 3 }, 0));
		private static readonly IBrush BandBrush = new SolidColorBrush(Color.FromRgb(204, 204, 204));

		private Double advancePerEm;

		static TicketView()
		{
			AffectsMeasure<TicketView>(TicketProperty, ColumnsProperty, PaperWidthMmProperty);
			AffectsRender<TicketView>(TicketProperty, ColumnsProperty, PaperWidthMmProperty);
		}

		public Ticket? Ticket
		{
			get { return GetValue(TicketProperty); }
			set { SetValue(TicketProperty, value); }
		}

		public Int32 Columns
		{
			get { return GetValue(ColumnsProperty); }
			set { SetValue(ColumnsProperty, value); }
		}

		public Int32 PaperWidthMm
		{
			get { return GetValue(PaperWidthMmProperty); }
			set { SetValue(PaperWidthMmProperty, value); }
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			Double lineHeight = GetMetrics(out Double _, out Double _);
			Int32 units = 0;
			if (Ticket != null)
			{
				foreach (TicketLine line in Ticket.Lines)
				{
					units += line.HeightUnits;
				}
			}

			Double height = Math.Max(MinimumPaperHeight, (units * lineHeight) + (2 * PaddingY));
			return new Size(PaperWidthMm * PixelsPerMm, height);
		}

		public override void Render(DrawingContext context)
		{
			Double width = Bounds.Width;
			context.DrawRectangle(Brushes.White, PaperBorder, new Rect(0.5, 0.5, Math.Max(0, width - 1), Math.Max(0, Bounds.Height - 1)));
			if (Ticket == null)
			{
				return;
			}

			Double lineHeight = GetMetrics(out Double cellWidth, out Double fontSize);
			Double top = PaddingY;
			foreach (TicketLine line in Ticket.Lines)
			{
				DrawLine(context, line, top, width, cellWidth, fontSize, lineHeight);
				top += line.HeightUnits * lineHeight;
			}
		}

		// Returns the line height; cell width and font size come out so that `Columns` cells fill the paper.
		private Double GetMetrics(out Double cellWidth, out Double fontSize)
		{
			Int32 columns = Math.Max(1, Columns);
			cellWidth = ((PaperWidthMm * PixelsPerMm) - (2 * PaddingX)) / columns;
			if (advancePerEm <= 0)
			{
				FormattedText probe = CreateText("0000000000", RegularFace, 100, Brushes.Black);
				advancePerEm = probe.Width / 10 / 100;
			}

			fontSize = cellWidth / advancePerEm;
			return fontSize * LineHeightFactor;
		}

		private void DrawLine(DrawingContext context, TicketLine line, Double top, Double paperWidth, Double cellWidth, Double fontSize, Double lineHeight)
		{
			Double height = line.HeightUnits * lineHeight;
			if (line.Kind == TicketLineKind.Cut)
			{
				context.DrawLine(CutPen, new Point(0, top + (height / 2)), new Point(paperWidth, top + (height / 2)));
				return;
			}

			if (line.Kind == TicketLineKind.Band)
			{
				Rect band = new Rect(PaddingX, top + 1, paperWidth - (2 * PaddingX), height - 2);
				context.DrawRectangle(BandBrush, null, band);
				FormattedText label = CreateText(line.BandText ?? String.Empty, RegularFace, fontSize, Brushes.Black);
				context.DrawText(label, new Point(band.X + ((band.Width - label.Width) / 2), band.Y + ((band.Height - label.Height) / 2)));
				return;
			}

			Int32 columns = Math.Max(1, Columns);
			Int32 offsetColumns = 0;
			if (line.Alignment == TicketAlignment.Center)
			{
				offsetColumns = Math.Max(0, (columns - line.Columns) / 2);
			}
			else if (line.Alignment == TicketAlignment.Right)
			{
				offsetColumns = Math.Max(0, columns - line.Columns);
			}

			Double x = PaddingX + (offsetColumns * cellWidth);
			foreach (TicketRun run in line.Runs)
			{
				Int32 widthFactor = run.DoubleWidth ? 2 : 1;
				Int32 heightFactor = run.DoubleHeight ? 2 : 1;
				Double runTop = top + ((line.HeightUnits - heightFactor) * lineHeight);
				FormattedText text = CreateText(run.Text, run.Bold ? BoldFace : RegularFace, fontSize, run.IsMarker ? Brushes.Firebrick : Brushes.Black);

				Matrix transform = Matrix.CreateScale(widthFactor, heightFactor) * Matrix.CreateTranslation(x, runTop);
				using (context.PushTransform(transform))
				{
					context.DrawText(text, new Point(0, 0));
					if (run.Underline)
					{
						Double underlineY = text.Baseline + 2;
						context.DrawLine(new Pen(Brushes.Black, 1), new Point(0, underlineY), new Point(run.Text.Length * cellWidth, underlineY));
					}
				}

				x += run.Columns * cellWidth;
			}
		}

		private static FormattedText CreateText(String text, Typeface typeface, Double fontSize, IBrush brush)
		{
			return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, brush);
		}
	}
}
