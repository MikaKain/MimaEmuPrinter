namespace MimaEmuPrinter.Core.Settings
{
	using System;
	using System.IO;
	using System.Text.Json;
	using System.Text.Json.Serialization;
	using MimaEmuPrinter.Core.Paper;

	/// <summary>Colour theme of the window: follows the system, or is forced.</summary>
	public enum ThemePreference
	{
		System,
		Light,
		Dark,
	}

	/// <summary>Settings restored at the next start: listening IP, port, paper and theme.</summary>
	public sealed class AppSettings
	{
		public const Int32 DefaultPort = 9100;

		public String ListenAddress { get; set; } = String.Empty;

		public Int32 Port { get; set; } = DefaultPort;

		public PaperWidth Paper { get; set; } = PaperWidth.Mm80Col42;

		public ThemePreference Theme { get; set; } = ThemePreference.System;
	}

	/// <summary>JSON settings file stored next to the executable.</summary>
	public sealed class SettingsStore
	{
		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			WriteIndented = true,
			Converters = { new JsonStringEnumConverter() },
		};

		private readonly String path;

		public SettingsStore(String path)
		{
			this.path = path;
		}

		public String Path
		{
			get { return path; }
		}

		/// <summary>Reads the settings; a missing or corrupt file gives the defaults.</summary>
		public AppSettings Load()
		{
			try
			{
				if (!File.Exists(path))
					return new AppSettings();

				var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
				return Sanitize(loaded ?? new AppSettings());
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
			{
				return new AppSettings();
			}
		}

		/// <summary>Writes the settings. Returns false when the file cannot be written.</summary>
		public Boolean Save(AppSettings settings)
		{
			try
			{
				File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
				return true;
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				return false;
			}
		}

		private static AppSettings Sanitize(AppSettings settings)
		{
			if (settings.Port < 1 || settings.Port > 65535)
				settings.Port = AppSettings.DefaultPort;

			if (!Enum.IsDefined(settings.Paper))
				settings.Paper = PaperWidth.Mm80Col42;

			if (!Enum.IsDefined(settings.Theme))
			{
				settings.Theme = ThemePreference.System;
			}

			settings.ListenAddress ??= String.Empty;
			return settings;
		}
	}
}