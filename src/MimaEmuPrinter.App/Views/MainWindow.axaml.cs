namespace MimaEmuPrinter.App.Views
{
	using System;
	using Avalonia;
	using Avalonia.Controls;
	using Avalonia.Interactivity;
	using Avalonia.Platform;
	using Avalonia.Threading;
	using ViewModels;

	public sealed partial class MainWindow : Window
	{
		private EventsWindow? eventsWindow;

		public MainWindow()
		{
			InitializeComponent();
			DataContextChanged += OnDataContextChanged;
		}

		/// <summary>Opens at 1280 x 800, or as large as the screen allows when it is smaller.</summary>
		protected override void OnOpened(EventArgs e)
		{
			base.OnOpened(e);
			Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
			if (screen == null)
			{
				return;
			}

			var scaling = screen.Scaling;
			var maxWidth = (screen.WorkingArea.Width / scaling) - 16;
			var maxHeight = (screen.WorkingArea.Height / scaling) - 40;

			if (Width <= maxWidth && Height <= maxHeight)
				return;

			Width = Math.Max(MinWidth, Math.Min(Width, maxWidth));
			Height = Math.Max(MinHeight, Math.Min(Height, maxHeight));
			var x = screen.WorkingArea.X + (Int32)Math.Max(0, (screen.WorkingArea.Width - (Width * scaling)) / 2);
			var y = screen.WorkingArea.Y + (Int32)Math.Max(0, (screen.WorkingArea.Height - ((Height + 32) * scaling)) / 2);
			Position = new PixelPoint(x, y);
		}

		private void OnDataContextChanged(Object? sender, EventArgs e)
		{
			if (DataContext is MainWindowViewModel viewModel)
			{
				viewModel.JournalChanged += (s, args) => Dispatcher.UIThread.Post(ScrollJournalToEnd, DispatcherPriority.Background);
				viewModel.TicketContentChanged += (s, args) => Dispatcher.UIThread.Post(ScrollTicketToEnd, DispatcherPriority.Background);
			}
		}

		private void OnShowEventsClick(Object? sender, RoutedEventArgs e)
		{
			if (eventsWindow != null)
			{
				eventsWindow.Activate();
				return;
			}

			EventsWindow window = new EventsWindow { DataContext = DataContext };
			window.Closed += (s, args) => eventsWindow = null;
			eventsWindow = window;
			window.Show(this);
			window.Position = new PixelPoint(Position.X + (Int32)(Bounds.Width * RenderScaling) + 8, Position.Y);
		}

		private void ScrollJournalToEnd()
		{
			var count = JournalList.ItemCount;
			if (count > 0)
				JournalList.ScrollIntoView(count - 1);
		}

		private void ScrollTicketToEnd()
		{
			TicketScroll.ScrollToEnd();
		}
	}
}