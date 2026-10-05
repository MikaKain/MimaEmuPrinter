namespace MimaEmuPrinter.App.Controls
{
	using System;
	using Avalonia;
	using Avalonia.Controls;
	using Avalonia.Input;
	using Avalonia.Interactivity;
	using Avalonia.Media;
	using Avalonia.Platform;

	/// <summary>
	/// Title bar drawn by the application, so it follows the light / dark theme (the system one cannot).
	/// Used on Windows only: macOS and Linux keep their native window decorations.
	/// </summary>
	public sealed partial class TitleBar : UserControl
	{
		public static readonly StyledProperty<String> TitleProperty =
			AvaloniaProperty.Register<TitleBar, String>(nameof(Title), String.Empty);

		public static readonly StyledProperty<Boolean> CanMaximizeProperty =
			AvaloniaProperty.Register<TitleBar, Boolean>(nameof(CanMaximize), true);

		private static readonly Geometry MaximizeGeometry = Geometry.Parse("M0.5,0.5 L9.5,0.5 L9.5,9.5 L0.5,9.5 Z");
		private static readonly Geometry RestoreGeometry = Geometry.Parse("M2.5,2.5 L2.5,0.5 L9.5,0.5 L9.5,7.5 L7.5,7.5 M0.5,2.5 L7.5,2.5 L7.5,9.5 L0.5,9.5 Z");

		private Window? window;

		public TitleBar()
		{
			InitializeComponent();
			IsVisible = OperatingSystem.IsWindows();
			Bar.PointerPressed += OnBarPointerPressed;
			Bar.DoubleTapped += OnBarDoubleTapped;
		}

		/// <summary>True when the platform needs the application to draw its own title bar.</summary>
		public static Boolean IsRequired
		{
			get { return OperatingSystem.IsWindows(); }
		}

		/// <summary>Hides the system title bar of <paramref name="window"/> when the application draws its own.</summary>
		public static void Apply(Window window)
		{
			if (!IsRequired)
			{
				return;
			}

			window.ExtendClientAreaToDecorationsHint = true;
			window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
			window.ExtendClientAreaTitleBarHeightHint = -1;
		}

		public String Title
		{
			get { return GetValue(TitleProperty); }
			set { SetValue(TitleProperty, value); }
		}

		public Boolean CanMaximize
		{
			get { return GetValue(CanMaximizeProperty); }
			set { SetValue(CanMaximizeProperty, value); }
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			window = TopLevel.GetTopLevel(this) as Window;
			if (window != null)
			{
				TitleText.Text = String.IsNullOrEmpty(Title) ? window.Title : Title;
				window.PropertyChanged += OnWindowPropertyChanged;
				MaximizeButton.IsVisible = CanMaximize && window.CanResize;
				UpdateMaximizeGlyph();
			}
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			if (window != null)
			{
				window.PropertyChanged -= OnWindowPropertyChanged;
			}

			base.OnDetachedFromVisualTree(e);
		}

		private void OnWindowPropertyChanged(Object? sender, AvaloniaPropertyChangedEventArgs e)
		{
			if (e.Property == Window.WindowStateProperty)
			{
				UpdateMaximizeGlyph();
			}
			else if (e.Property == Window.TitleProperty && window != null && String.IsNullOrEmpty(Title))
			{
				TitleText.Text = window.Title;
			}
		}

		private void UpdateMaximizeGlyph()
		{
			Boolean maximized = window?.WindowState == WindowState.Maximized;
			MaximizeGlyph.Data = maximized ? RestoreGeometry : MaximizeGeometry;
			ToolTip.SetTip(MaximizeButton, maximized ? "Restaurer" : "Agrandir");
		}

		private void OnBarPointerPressed(Object? sender, PointerPressedEventArgs e)
		{
			if (window != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				window.BeginMoveDrag(e);
			}
		}

		private void OnBarDoubleTapped(Object? sender, TappedEventArgs e)
		{
			if (window != null && window.CanResize && CanMaximize)
			{
				ToggleMaximize();
			}
		}

		private void OnMinimize(Object? sender, RoutedEventArgs e)
		{
			if (window != null)
			{
				window.WindowState = WindowState.Minimized;
			}
		}

		private void OnMaximize(Object? sender, RoutedEventArgs e)
		{
			ToggleMaximize();
		}

		private void OnClose(Object? sender, RoutedEventArgs e)
		{
			window?.Close();
		}

		private void ToggleMaximize()
		{
			if (window != null)
			{
				window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
			}
		}
	}
}
