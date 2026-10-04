namespace MimaEmuPrinter.App.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Runtime.CompilerServices;
	using System.Threading.Tasks;
	using System.Windows.Input;

	public abstract class ViewModelBase : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;

		protected Boolean SetProperty<T>(ref T field, T value, [CallerMemberName] String? propertyName = null)
		{
			if (EqualityComparer<T>.Default.Equals(field, value))
			{
				return false;
			}

			field = value;
			OnPropertyChanged(propertyName);
			return true;
		}

		protected void OnPropertyChanged([CallerMemberName] String? propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}

	public sealed class RelayCommand : ICommand
	{
		private readonly Action execute;
		private readonly Func<Boolean>? canExecute;

		public RelayCommand(Action execute, Func<Boolean>? canExecute = null)
		{
			this.execute = execute;
			this.canExecute = canExecute;
		}

		public event EventHandler? CanExecuteChanged;

		public Boolean CanExecute(Object? parameter)
		{
			return canExecute?.Invoke() ?? true;
		}

		public void Execute(Object? parameter)
		{
			execute();
		}

		public void RaiseCanExecuteChanged()
		{
			CanExecuteChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	/// <summary>Command running an asynchronous action; it cannot be started again while it runs.</summary>
	public sealed class AsyncRelayCommand : ICommand
	{
		private readonly Func<Task> execute;
		private Boolean running;

		public AsyncRelayCommand(Func<Task> execute)
		{
			this.execute = execute;
		}

		public event EventHandler? CanExecuteChanged;

		public Boolean CanExecute(Object? parameter)
		{
			return !running;
		}

		public async void Execute(Object? parameter)
		{
			if (running)
			{
				return;
			}

			running = true;
			CanExecuteChanged?.Invoke(this, EventArgs.Empty);
			try
			{
				await execute();
			}
			finally
			{
				running = false;
				CanExecuteChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}
}
