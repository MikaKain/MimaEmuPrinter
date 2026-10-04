namespace MimaEmuPrinter.App.ViewModels
{
	using System;
	using MimaEmuPrinter.Core.Logging;

	/// <summary>A journal line with its severity, to colour the failures and the job lines.</summary>
	public sealed class JournalLineViewModel
	{
		private static readonly String[] ErrorMarkers = { "Échec", "Erreur", "refusée", "invalide", "non enregistrés", "inutilisable", "illisible" };

		private static readonly String[] WarningMarkers = { "Dépassement", "non imprimé", "tronqué", "non traité", "non remplacé" };

		public JournalLineViewModel(JournalEntry entry)
		{
			Text = entry.ToString();
			IsError = ContainsAny(entry.Message, ErrorMarkers);
			IsWarning = !IsError && ContainsAny(entry.Message, WarningMarkers);
			IsJob = !IsError && !IsWarning && entry.Message.StartsWith("Job ", StringComparison.Ordinal) && entry.Message.Contains(" clos", StringComparison.Ordinal);
		}

		public String Text { get; }

		public Boolean IsError { get; }

		public Boolean IsWarning { get; }

		public Boolean IsJob { get; }

		private static Boolean ContainsAny(String message, String[] markers)
		{
			foreach (String marker in markers)
			{
				if (message.Contains(marker, StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}
	}
}
