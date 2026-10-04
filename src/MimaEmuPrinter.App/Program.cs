namespace MimaEmuPrinter.App
{
	using System;
	using System.IO;
	using Avalonia;

	internal static class Program
	{
		[STAThread]
		public static void Main(String[] args)
		{
			AppOptions.Initialize(args);
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
		}

		public static AppBuilder BuildAvaloniaApp()
		{
			return AppBuilder.Configure<App>()
				.UsePlatformDetect()
				.LogToTrace();
		}
	}

	/// <summary>
	/// Locations of the settings file and of the output folder. By default both are next to the executable:
	/// the working directory of the application is its own folder, so the "pdf" folder does not depend on
	/// how the program was launched. <c>--output</c> and <c>--settings</c> override them, and <c>--start</c>
	/// begins listening at launch (automated tests, unattended bench).
	/// </summary>
	internal static class AppOptions
	{
		public const String SettingsFileName = "settings.json";

		public const String OutputFolderName = "pdf";

		public static String SettingsPath { get; private set; } = String.Empty;

		public static String OutputDirectory { get; private set; } = String.Empty;

		public static Boolean AutoStart { get; private set; }

		public static void Initialize(String[] args)
		{
			String? output = null;
			String? settings = null;

			for (var index = 0; index < args.Length; index++)
			{
				if (args[index] == "--start")
					AutoStart = true;
				else if (args[index] == "--output" && index + 1 < args.Length)
					output = args[index + 1];
				else if (args[index] == "--settings" && index + 1 < args.Length)
					settings = args[index + 1];
			}

			try
			{
				Directory.SetCurrentDirectory(AppContext.BaseDirectory);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				// Keep the inherited working directory.
			}

			SettingsPath = Path.GetFullPath(settings ?? Path.Combine(AppContext.BaseDirectory, SettingsFileName));
			OutputDirectory = Path.GetFullPath(output ?? Path.Combine(Environment.CurrentDirectory, OutputFolderName));
		}
	}
}