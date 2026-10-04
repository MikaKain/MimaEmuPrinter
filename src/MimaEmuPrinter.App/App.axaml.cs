namespace MimaEmuPrinter.App
{
	using Avalonia;
	using Avalonia.Controls.ApplicationLifetimes;
	using Avalonia.Markup.Xaml;
	using MimaEmuPrinter.App.ViewModels;
	using MimaEmuPrinter.App.Views;

	public sealed partial class App : Application
	{
		public override void Initialize()
		{
			AvaloniaXamlLoader.Load(this);
		}

		public override void OnFrameworkInitializationCompleted()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				MainWindowViewModel viewModel = new MainWindowViewModel(AppOptions.SettingsPath, AppOptions.OutputDirectory);
				MainWindow window = new MainWindow { DataContext = viewModel };
				window.Closing += (sender, e) => viewModel.Shutdown();
				desktop.MainWindow = window;
				if (AppOptions.AutoStart)
				{
					viewModel.StartStopCommand.Execute(null);
				}
			}

			base.OnFrameworkInitializationCompleted();
		}
	}
}
