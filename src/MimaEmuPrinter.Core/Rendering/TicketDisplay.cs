namespace MimaEmuPrinter.Core.Rendering
{
	using System;

	public enum TicketDisplayState
	{
		/// <summary>Empty strip, "En attente".</summary>
		Waiting,

		/// <summary>A job is being received, "Impression en cours".</summary>
		Printing,

		/// <summary>The job is closed and stays visible during the countdown.</summary>
		Closed,

		/// <summary>The closed ticket is frozen by the "Garder" button.</summary>
		Held,
	}

	/// <summary>
	/// State machine behind the virtual ticket strip: live ticket, 5 second countdown after the job end,
	/// immediate replacement by a new job, and the Garder / Relâcher freeze.
	/// </summary>
	public sealed class TicketDisplay
	{
		public static readonly TimeSpan DefaultRetention = TimeSpan.FromSeconds(5);

		private readonly Object syncRoot = new();
		private readonly TimeProvider time;
		private readonly TimeSpan retention;
		private TicketUpdate? current;
		private DateTimeOffset deadline;
		private Boolean holdRequested;

		public TicketDisplay(TimeProvider? time = null, TimeSpan? retention = null)
		{
			this.time = time ?? TimeProvider.System;
			this.retention = retention ?? DefaultRetention;
		}

		/// <summary>Raised (on any thread) when the displayed content or state changed.</summary>
		public event EventHandler? Changed;

		public TicketUpdate? Current
		{
			get
			{
				lock (syncRoot)
				{
					return current;
				}
			}
		}

		public Boolean IsHeld
		{
			get
			{
				lock (syncRoot)
				{
					return current != null && holdRequested;
				}
			}
		}

		public TicketDisplayState State
		{
			get
			{
				lock (syncRoot)
				{
					if (current == null)
						return TicketDisplayState.Waiting;

					if (!current.IsClosed)
						return TicketDisplayState.Printing;

					return holdRequested ? TicketDisplayState.Held : TicketDisplayState.Closed;
				}
			}
		}

		/// <summary>Countdown value shown while the closed ticket is displayed (5, 4, 3, 2, 1), 0 otherwise.</summary>
		public Int32 SecondsRemaining
		{
			get
			{
				lock (syncRoot)
				{
					if (current == null || !current.IsClosed || holdRequested)
						return 0;

					var remaining = (deadline - time.GetUtcNow()).TotalSeconds;
					return remaining <= 0 ? 0 : (Int32)Math.Ceiling(remaining);
				}
			}
		}

		/// <summary>A job started or progressed: a new job replaces the display immediately.</summary>
		public void Apply(TicketUpdate update)
		{
			lock (syncRoot)
			{
				if (current == null || current.JobId != update.JobId)
					holdRequested = false;

				current = update;
				if (update.IsClosed)
					deadline = time.GetUtcNow() + retention;
			}

			RaiseChanged();
		}

		/// <summary>Freezes the current ticket beyond the countdown.</summary>
		public void Hold()
		{
			lock (syncRoot)
			{
				if (current == null || holdRequested)
					return;

				holdRequested = true;
			}

			RaiseChanged();
		}

		/// <summary>Releases the freeze: the display clears as soon as the countdown has elapsed.</summary>
		public void Release()
		{
			lock (syncRoot)
			{
				if (!holdRequested)
					return;

				holdRequested = false;
			}

			RaiseChanged();
			Tick();
		}

		/// <summary>Call periodically: clears the closed ticket once the countdown is over. Returns true if it cleared.</summary>
		public Boolean Tick()
		{
			lock (syncRoot)
			{
				if (current == null || !current.IsClosed || holdRequested || time.GetUtcNow() < deadline)
					return false;

				current = null;
			}

			RaiseChanged();
			return true;
		}

		private void RaiseChanged()
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}
}