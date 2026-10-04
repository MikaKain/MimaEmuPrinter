namespace MimaEmuPrinter.App.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.Globalization;
	using System.Net;
	using System.Net.Sockets;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Windows.Input;
	using Avalonia.Media;
	using Avalonia.Threading;
	using MimaEmuPrinter.Core.Archive;
	using MimaEmuPrinter.Core.Logging;
	using MimaEmuPrinter.Core.Network;
	using MimaEmuPrinter.Core.Paper;
	using MimaEmuPrinter.Core.Rendering;
	using MimaEmuPrinter.Core.Settings;
	using MimaEmuPrinter.Core.Status;

	/// <summary>
	/// The only window: connection (top), paper / events / virtual ticket (middle), journal (bottom).
	/// It only displays the state of the core and sends it commands.
	/// </summary>
	public sealed class MainWindowViewModel : ViewModelBase
	{
		private const Int32 JournalScreenLines = JournalService.DefaultCapacity;

		private readonly SettingsStore settingsStore;
		private readonly AppSettings settings;
		private readonly StatusEngine status = new();
		private readonly JournalService journal;
		private readonly ArchiveService archive;
		private readonly PrinterServer server;
		private readonly TicketDisplay display = new();
		private readonly DispatcherTimer timer;
		private Int32 currentPaper;
		private Int32 refreshPending;

		private String listenAddress;
		private Decimal? portValue;
		private PaperProfile selectedPaper;
		private ListenerState listenerState = ListenerState.Stopped;

		private String displayTitle = "En attente";
		private String countdownText = String.Empty;
		private Ticket? displayedTicket;
		private Int32 displayedColumns;
		private Int32 displayedPaperMm;
		private String? noticeText;
		private String holdButtonText = "Garder";
		private Boolean canHold;

		public MainWindowViewModel(String settingsPath, String outputDirectory)
		{
			settingsStore = new SettingsStore(settingsPath);
			settings = settingsStore.Load();

			journal = new JournalService(System.IO.Path.Combine(outputDirectory, ArchiveService.JournalFileName));
			archive = new ArchiveService(outputDirectory, journal);
			EnsureOutputDirectories();

			selectedPaper = PaperProfile.For(settings.Paper);
			currentPaper = (Int32)selectedPaper.Width;
			displayedColumns = selectedPaper.Columns;
			displayedPaperMm = selectedPaper.WidthMm;

			listenAddress = String.IsNullOrWhiteSpace(settings.ListenAddress)
				? NetworkAddressProvider.GetDefaultListenAddress().ToString()
				: settings.ListenAddress.Trim();
			portValue = settings.Port;

			var choices = new List<String>(NetworkAddressProvider.GetSelectableAddresses());

			if (!choices.Contains(listenAddress))
				choices.Insert(0, listenAddress);

			AddressChoices = choices;
			PaperOptions = PaperProfile.All;

			foreach (PrinterFaults fault in FaultRules.All)
				Faults.Add(new FaultItemViewModel(fault, status));

			server = new PrinterServer(status, journal, archive, () => (PaperWidth)Volatile.Read(ref currentPaper));

			StartStopCommand = new AsyncRelayCommand(ToggleListeningAsync);
			ClearAllCommand = new RelayCommand(status.ClearAll);
			HoldCommand = new RelayCommand(ToggleHold);

			journal.EntryAdded += OnJournalEntry;
			status.Changed += OnStatusChanged;
			server.StateChanged += OnServerStateChanged;
			server.TicketUpdated += OnTicketUpdated;
			display.Changed += OnDisplayChanged;

			RefreshFaults();
			journal.Write("MimaEmuPrinter démarré, dossier de sortie : " + outputDirectory);

			timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, OnTimerTick);
			timer.Start();
		}

		/// <summary>Raised on the UI thread when a line was added to the screen journal.</summary>
		public event EventHandler? JournalChanged;

		/// <summary>Raised on the UI thread when the displayed ticket content changed.</summary>
		public event EventHandler? TicketContentChanged;

		public IReadOnlyList<String> AddressChoices { get; }

		public IReadOnlyList<PaperProfile> PaperOptions { get; }

		public ObservableCollection<FaultItemViewModel> Faults { get; } = [];

		public ObservableCollection<String> JournalLines { get; } = [];

		public ICommand StartStopCommand { get; }

		public ICommand ClearAllCommand { get; }

		public ICommand HoldCommand { get; }

		public String ListenAddress
		{
			get { return listenAddress; }
			set { SetProperty(ref listenAddress, value ?? String.Empty); }
		}

		public Decimal? PortValue
		{
			get { return portValue; }
			set { SetProperty(ref portValue, value); }
		}

		public PaperProfile SelectedPaper
		{
			get
			{
				return selectedPaper;
			}

			set
			{
				if (value == null || !SetProperty(ref selectedPaper, value))
				{
					return;
				}

				// The paper is chosen before the start; a job always keeps the paper it started with.
				Volatile.Write(ref currentPaper, (Int32)value.Width);
				settings.Paper = value.Width;

				SaveSettings();

				if (display.Current == null)
					RefreshDisplay();
			}
		}

		public Boolean IsRunning
		{
			get { return listenerState != ListenerState.Stopped; }
		}

		public Boolean CanEditConnection
		{
			get { return !IsRunning; }
		}

		/// <summary>Subtitle of the header: the listening endpoint and paper while running.</summary>
		public String ConnectionSummary
		{
			get
			{
				IPEndPoint? endPoint = server.LocalEndPoint;
				if (!IsRunning || endPoint == null)
				{
					return "Imprimante thermique virtuelle ESC/POS";
				}

				return String.Format(CultureInfo.InvariantCulture, "{0}:{1} · {2}", endPoint.Address, endPoint.Port, selectedPaper.DisplayName);
			}
		}

		public String StartStopText
		{
			get { return IsRunning ? "Arrêter" : "Démarrer"; }
		}

		public String StatusText
		{
			get
			{
				return listenerState switch
				{
					ListenerState.Listening => "En écoute",
					ListenerState.Busy => "Occupée",
					_ => "Arrêtée",
				};
			}
		}

		public IBrush StatusBrush
		{
			get
			{
				return listenerState switch
				{
					ListenerState.Listening => Brushes.SeaGreen,
					ListenerState.Busy => Brushes.DarkOrange,
					_ => Brushes.Gray,
				};
			}
		}

		public String DisplayTitle
		{
			get { return displayTitle; }
			private set { SetProperty(ref displayTitle, value); }
		}

		public String CountdownText
		{
			get { return countdownText; }
			private set { SetProperty(ref countdownText, value); }
		}

		public Ticket? DisplayedTicket
		{
			get { return displayedTicket; }
			private set { SetProperty(ref displayedTicket, value); }
		}

		public Int32 DisplayedColumns
		{
			get { return displayedColumns; }
			private set { SetProperty(ref displayedColumns, value); }
		}

		public Int32 DisplayedPaperMm
		{
			get { return displayedPaperMm; }
			private set { SetProperty(ref displayedPaperMm, value); }
		}

		public String? NoticeText
		{
			get
			{
				return noticeText;
			}

			private set
			{
				if (SetProperty(ref noticeText, value))
				{
					OnPropertyChanged(nameof(HasNotice));
				}
			}
		}

		public Boolean HasNotice
		{
			get { return !String.IsNullOrEmpty(noticeText); }
		}

		public Boolean HasCountdown
		{
			get { return !String.IsNullOrEmpty(countdownText); }
		}

		public String HoldButtonText
		{
			get { return holdButtonText; }
			private set { SetProperty(ref holdButtonText, value); }
		}

		public Boolean CanHold
		{
			get { return canHold; }
			private set { SetProperty(ref canHold, value); }
		}

		/// <summary>Stops the listener and saves the settings (window closing).</summary>
		public void Shutdown()
		{
			timer.Stop();
			SaveSettings();
			Task stopping = server.StopAsync();
			Task.WaitAny([stopping], TimeSpan.FromSeconds(3));
		}

		private void EnsureOutputDirectories()
		{
			try
			{
				archive.EnsureDirectories();
			}
			catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
			{
				journal.Write("Dossier de sortie inutilisable : " + ex.Message);
			}
		}

		private async Task ToggleListeningAsync()
		{
			if (IsRunning)
			{
				await server.StopAsync();
				return;
			}

			if (!IPAddress.TryParse(ListenAddress.Trim(), out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork)
			{
				journal.Write("Adresse IP d'écoute invalide : " + ListenAddress);
				return;
			}

			var port = (Int32)(PortValue ?? 0);
			if (port < 1 || port > 65535)
			{
				journal.Write("Port invalide (1 à 65535) : " + PortValue?.ToString(CultureInfo.InvariantCulture));
				return;
			}

			settings.ListenAddress = address.ToString();
			settings.Port = port;
			SaveSettings();
			archive.EnsureDirectories();
			server.Start(address, port);
		}

		private void ToggleHold()
		{
			if (display.IsHeld)
			{
				display.Release();
			}
			else
			{
				display.Hold();
			}
		}

		private void SaveSettings()
		{
			settings.Paper = selectedPaper.Width;
			if (!settingsStore.Save(settings))
			{
				journal.Write("Réglages non enregistrés : " + settingsStore.Path);
			}
		}

		// ----- Events from the core (any thread) -----

		private void OnJournalEntry(Object? sender, JournalEntry entry)
		{
			PostToUi(() =>
			{
				JournalLines.Add(entry.ToString());
				while (JournalLines.Count > JournalScreenLines)
				{
					JournalLines.RemoveAt(0);
				}

				JournalChanged?.Invoke(this, EventArgs.Empty);
			});
		}

		private void OnStatusChanged(Object? sender, FaultsChangedEventArgs e)
		{
			PostToUi(RefreshFaults);
		}

		private void OnServerStateChanged(Object? sender, EventArgs e)
		{
			PostToUi(() =>
			{
				listenerState = server.State;
				OnPropertyChanged(nameof(IsRunning));
				OnPropertyChanged(nameof(CanEditConnection));
				OnPropertyChanged(nameof(ConnectionSummary));
				OnPropertyChanged(nameof(StartStopText));
				OnPropertyChanged(nameof(StatusText));
				OnPropertyChanged(nameof(StatusBrush));
			});
		}

		private void OnTicketUpdated(Object? sender, TicketUpdate update)
		{
			display.Apply(update);
		}

		private void OnDisplayChanged(Object? sender, EventArgs e)
		{
			if (Interlocked.Exchange(ref refreshPending, 1) == 0)
			{
				Dispatcher.UIThread.Post(
					() =>
					{
						Interlocked.Exchange(ref refreshPending, 0);
						RefreshDisplay();
					},
					DispatcherPriority.Background);
			}
		}

		private void OnTimerTick(Object? sender, EventArgs e)
		{
			display.Tick();
			RefreshDisplay();
		}

		private static void PostToUi(Action action)
		{
			if (Dispatcher.UIThread.CheckAccess())
			{
				action();
			}
			else
			{
				Dispatcher.UIThread.Post(action);
			}
		}

		// ----- Refresh of the view state (UI thread) -----

		private void RefreshFaults()
		{
			PrinterFaults effective = status.Effective;
			PrinterFaults locked = status.Locked;
			foreach (FaultItemViewModel item in Faults)
			{
				item.Refresh(effective, locked);
			}
		}

		private void RefreshDisplay()
		{
			TicketUpdate? current = display.Current;
			TicketDisplayState state = display.State;
			Ticket? previousTicket = displayedTicket;

			if (current == null)
			{
				DisplayTitle = "En attente";
				CountdownText = String.Empty;
				DisplayedTicket = null;
				DisplayedColumns = selectedPaper.Columns;
				DisplayedPaperMm = selectedPaper.WidthMm;
				NoticeText = null;
				HoldButtonText = "Garder";
				CanHold = false;
			}
			else
			{
				var profile = PaperProfile.For(current.Paper);
				DisplayedColumns = profile.Columns;
				DisplayedPaperMm = profile.WidthMm;
				DisplayedTicket = current.Ticket;
				NoticeText = current.Notice;
				CanHold = true;
				HoldButtonText = state == TicketDisplayState.Held ? "Relâcher" : "Garder";
				switch (state)
				{
					case TicketDisplayState.Printing:
						DisplayTitle = "Impression en cours";
						CountdownText = String.Empty;
						break;
					case TicketDisplayState.Held:
						DisplayTitle = "Ticket gardé";
						CountdownText = String.Empty;
						break;
					default:
						DisplayTitle = "Ticket clos";
						CountdownText = display.SecondsRemaining.ToString(CultureInfo.InvariantCulture);
						break;
				}
			}

			OnPropertyChanged(nameof(HasCountdown));
			if (!ReferenceEquals(previousTicket, displayedTicket))
			{
				TicketContentChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}
}