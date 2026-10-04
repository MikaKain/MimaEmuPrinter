namespace MimaEmuPrinter.App.ViewModels
{
	using System;
	using MimaEmuPrinter.Core.Status;

	/// <summary>One of the nine fault switches. A switch implied by another active one is shown on and locked.</summary>
	public sealed class FaultItemViewModel : ViewModelBase
	{
		private readonly StatusEngine engine;
		private Boolean isChecked;
		private Boolean isLocked;
		private Boolean updating;

		public FaultItemViewModel(PrinterFaults fault, StatusEngine engine)
		{
			Fault = fault;
			this.engine = engine;
			Label = FaultRules.GetLabel(fault);
			Effect = FaultRules.GetEffect(fault);
			CommandHint = FaultRules.GetCommandHint(fault);
		}

		public PrinterFaults Fault { get; }

		public String Label { get; }

		public String Effect { get; }

		public String CommandHint { get; }

		public Boolean IsChecked
		{
			get
			{
				return isChecked;
			}

			set
			{
				if (updating || value == isChecked)
				{
					return;
				}

				engine.SetFault(Fault, value);

				// The engine may refuse (switch implied by another one): make the view show the real state.
				OnPropertyChanged(nameof(IsChecked));
			}
		}

		public Boolean IsLocked
		{
			get { return isLocked; }
		}

		public Boolean CanToggle
		{
			get { return !isLocked; }
		}

		/// <summary>Aligns the switch with the engine state (called on the UI thread after each change).</summary>
		public void Refresh(PrinterFaults effective, PrinterFaults locked)
		{
			updating = true;
			try
			{
				Boolean newChecked = (effective & Fault) != 0;
				Boolean newLocked = (locked & Fault) != 0;
				if (isChecked != newChecked)
				{
					isChecked = newChecked;
					OnPropertyChanged(nameof(IsChecked));
				}

				if (isLocked != newLocked)
				{
					isLocked = newLocked;
					OnPropertyChanged(nameof(IsLocked));
					OnPropertyChanged(nameof(CanToggle));
				}
			}
			finally
			{
				updating = false;
			}
		}
	}
}