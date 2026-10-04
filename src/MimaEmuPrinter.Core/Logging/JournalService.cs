namespace MimaEmuPrinter.Core.Logging
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Text;

	public sealed record JournalEntry(DateTime Timestamp, String Message)
	{
		public override String ToString()
		{
			return Timestamp.ToString("yyyy-MM-dd HH:mm:ss") + "  " + Message;
		}
	}

	/// <summary>
	/// Journal of the print arrivals: the last 500 lines are kept in memory for the screen, the whole
	/// content is appended to a log file. A failure to write the file never reaches the caller.
	/// </summary>
	public sealed class JournalService
	{
		public const Int32 DefaultCapacity = 500;

		private readonly Object syncRoot = new Object();
		private readonly Queue<JournalEntry> entries = new Queue<JournalEntry>();
		private readonly String? filePath;
		private readonly TimeProvider time;
		private readonly Int32 capacity;

		public JournalService(String? filePath, TimeProvider? time = null, Int32 capacity = DefaultCapacity)
		{
			this.filePath = filePath;
			this.time = time ?? TimeProvider.System;
			this.capacity = capacity;
		}

		/// <summary>Raised (on the writer's thread) for each new line.</summary>
		public event EventHandler<JournalEntry>? EntryAdded;

		public void Write(String message)
		{
			JournalEntry entry = new JournalEntry(time.GetLocalNow().DateTime, message);
			lock (syncRoot)
			{
				entries.Enqueue(entry);
				while (entries.Count > capacity)
				{
					entries.Dequeue();
				}

				AppendToFile(entry);
			}

			EntryAdded?.Invoke(this, entry);
		}

		/// <summary>The lines kept in memory, oldest first.</summary>
		public IReadOnlyList<JournalEntry> Snapshot()
		{
			lock (syncRoot)
			{
				return entries.ToArray();
			}
		}

		private void AppendToFile(JournalEntry entry)
		{
			if (String.IsNullOrEmpty(filePath))
			{
				return;
			}

			try
			{
				String? directory = Path.GetDirectoryName(filePath);
				if (!String.IsNullOrEmpty(directory))
				{
					Directory.CreateDirectory(directory);
				}

				File.AppendAllText(filePath, entry.ToString() + Environment.NewLine, new UTF8Encoding(false));
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				// The on-screen journal stays the reference when the file cannot be written.
			}
		}
	}
}
