namespace MimaEmuPrinter.Core.Archive
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.IO;
	using System.Text.Json;
	using System.Text.Json.Serialization;
	using System.Threading;
	using System.Threading.Tasks;
	using Logging;

	/// <summary>
	/// Archives the closed jobs. Output folder layout:
	/// <c>tickets-YYYY-MM-DD.pdf</c> (rewritten after every closed job), <c>journal.log</c> (written by the journal)
	/// and <c>brut/</c> holding, per job, the raw ESC/POS stream (<c>.bin</c>) and the ticket data (<c>.json</c>)
	/// from which the day's PDF is rebuilt after a restart.
	/// </summary>
	public sealed class ArchiveService
	{
		public const String RawFolderName = "brut";

		public const String JournalFileName = "journal.log";

		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			Converters = { new JsonStringEnumConverter() },
		};

		private readonly Object idLock = new();
		private readonly Object queueLock = new();
		private readonly JournalService journal;

		private Task queueTail = Task.CompletedTask;
		private Int32 pendingCount;

		private DateTime idDay = DateTime.MinValue;
		private Int32 lastJobNumber;

		private DateTime recordsDay = DateTime.MinValue;
		private List<JobRecord> dayRecords = [];

		public ArchiveService(String outputDirectory, JournalService journal)
		{
			OutputDirectory = outputDirectory;
			this.journal = journal;
		}

		/// <summary>The "pdf" folder.</summary>
		public String OutputDirectory { get; }

		public String RawDirectory
		{
			get { return Path.Combine(OutputDirectory, RawFolderName); }
		}

		public String JournalPath
		{
			get { return Path.Combine(OutputDirectory, JournalFileName); }
		}

		/// <summary>Creates the output folders when missing (called at start-up).</summary>
		public void EnsureDirectories()
		{
			Directory.CreateDirectory(OutputDirectory);
			Directory.CreateDirectory(RawDirectory);
		}

		public String GetPdfPath(DateTime day)
		{
			return Path.Combine(OutputDirectory, "tickets-" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".pdf");
		}

		/// <summary>Identifier of the next job of the day ("J0001"...). The counter restarts each calendar day.</summary>
		public String AllocateJobId(DateTime localNow)
		{
			lock (idLock)
			{
				if (idDay != localNow.Date)
				{
					idDay = localNow.Date;
					lastJobNumber = ReadLastJobNumber(idDay);
				}

				lastJobNumber++;
				return "J" + lastJobNumber.ToString("0000", CultureInfo.InvariantCulture);
			}
		}

		/// <summary>
		/// Queues the job: the raw stream is stored and the PDF of the job's day rewritten, in arrival order,
		/// off the calling thread. Never throws: a failure is journaled.
		/// </summary>
		public Task ArchiveAsync(JobRecord record, Byte[] rawStream)
		{
			lock (queueLock)
			{
				Interlocked.Increment(ref pendingCount);
				queueTail = queueTail.ContinueWith(
					_ => ArchiveSafely(record, rawStream),
					CancellationToken.None,
					TaskContinuationOptions.None,
					TaskScheduler.Default);
				return queueTail;
			}
		}

		/// <summary>Completes when every queued job has been archived.</summary>
		public async Task WaitIdleAsync()
		{
			while (Volatile.Read(ref pendingCount) > 0)
			{
				await Task.Delay(10).ConfigureAwait(false);
			}
		}

		private void ArchiveSafely(JobRecord record, Byte[] rawStream)
		{
			try
			{
				Archive(record, rawStream);
			}
			catch (Exception ex)
			{
				journal.Write("Erreur d'archivage du job " + record.Id + " : " + ex.Message);
			}
			finally
			{
				Interlocked.Decrement(ref pendingCount);
			}
		}

		private void Archive(JobRecord record, Byte[] rawStream)
		{
			EnsureDirectories();

			// The day's tickets are read before the new job files exist, so the job is not counted twice.
			List<JobRecord> records = GetDayRecords(record.StartedAt.Date);

			String stem = record.StartedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + record.Id;
			File.WriteAllBytes(Path.Combine(RawDirectory, stem + ".bin"), rawStream);
			File.WriteAllText(Path.Combine(RawDirectory, stem + ".json"), JsonSerializer.Serialize(record, JsonOptions));

			records.Add(record);

			String pdfPath = GetPdfPath(record.StartedAt);
			try
			{
				PdfDayWriter.Save(pdfPath, record.StartedAt.Date, records);
			}
			catch (IOException ex)
			{
				// Typically the PDF is open in a viewer that locks it: the next job retries with all the tickets.
				journal.Write("PDF du jour non remplacé (" + ex.Message + ") : nouvelle tentative au prochain job");
			}
		}

		private List<JobRecord> GetDayRecords(DateTime day)
		{
			if (recordsDay != day)
			{
				dayRecords = LoadDayRecords(day);
				recordsDay = day;
			}

			return dayRecords;
		}

		private List<JobRecord> LoadDayRecords(DateTime day)
		{
			var records = new List<JobRecord>();
			if (!Directory.Exists(RawDirectory))
				return records;

			var files = Directory.GetFiles(RawDirectory, day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-*.json");
			Array.Sort(files, StringComparer.Ordinal);
			foreach (String file in files)
			{
				try
				{
					var loaded = JsonSerializer.Deserialize<JobRecord>(File.ReadAllText(file), JsonOptions);
					if (loaded != null)
						records.Add(loaded);
				}
				catch (Exception ex) when (ex is IOException || ex is JsonException)
				{
					journal.Write("Fichier de ticket illisible ignoré : " + Path.GetFileName(file));
				}
			}

			return records;
		}

		private Int32 ReadLastJobNumber(DateTime day)
		{
			var last = 0;
			if (!Directory.Exists(RawDirectory))
				return last;

			var prefix = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-";
			foreach (String file in Directory.GetFiles(RawDirectory, prefix + "*.json"))
			{
				var name = Path.GetFileNameWithoutExtension(file);
				var separator = name.LastIndexOf("-J", StringComparison.Ordinal);
				if (separator >= 0 && Int32.TryParse(name.AsSpan(separator + 2), NumberStyles.None, CultureInfo.InvariantCulture, out Int32 number))
					last = Math.Max(last, number);
			}

			return last;
		}
	}
}