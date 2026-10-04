namespace MimaEmuPrinter.TestClient
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Net.Sockets;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	/// Command line cash register for MimaEmuPrinter, like "nc ip 9100" but with canned ESC/POS streams
	/// and status queries. Exit code 0 on success, 1 when an expectation fails, 2 on a connection error.
	/// </summary>
	internal static class Program
	{
		private const String Usage = @"MimaEmuPrinter.TestClient [--host H] [--port P] [--columns N] <command> [options]

Commands:
  ticket [--logo]        reference table ticket (sign, table, URL, QR, covers, date), ends with GS V
  receipt                counter receipt (bold, underline, price columns, barcode)
  long                   a line wider than the paper (truncation marker + journal entry)
  text <string>          the text and a line feed, no cut (job closes after 400 ms)
  status <1-4>           DLE EOT n, prints the byte returned (--expect 0x1A to check it)
  paper                  GS r 1, paper sensor byte
  id                     GS I 67 and GS I 65, identity strings
  enq <n>                DLE ENQ n (journaled, never clears a fault)
  asb [seconds]          GS a 255 then prints each 4 byte ASB received (default 30 s)
  raw <file>             sends a file as is
  hold <seconds>         keeps the connection open without sending (to test the busy state)";

		private static async Task<Int32> Main(String[] args)
		{
			String host = "127.0.0.1";
			Int32 port = 9100;
			Int32 columns = 42;
			Int32? expected = null;
			Boolean logo = false;
			List<String> positional = new List<String>();

			for (Int32 index = 0; index < args.Length; index++)
			{
				switch (args[index])
				{
					case "--host":
						host = args[++index];
						break;
					case "--port":
						port = Int32.Parse(args[++index]);
						break;
					case "--columns":
						columns = Int32.Parse(args[++index]);
						break;
					case "--expect":
						expected = Convert.ToInt32(args[++index], args[index].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
						break;
					case "--logo":
						logo = true;
						break;
					default:
						positional.Add(args[index]);
						break;
				}
			}

			if (positional.Count == 0)
			{
				Console.WriteLine(Usage);
				return 1;
			}

			try
			{
				using TcpClient client = new();
				await client.ConnectAsync(host, port);
				var stream = client.GetStream();
				return await RunAsync(stream, positional, columns, logo, expected);
			}
			catch (Exception ex) when (ex is SocketException || ex is IOException)
			{
				Console.Error.WriteLine("Connection error: " + ex.Message);
				return 2;
			}
		}

		private static async Task<Int32> RunAsync(NetworkStream stream, List<String> command, Int32 columns, Boolean logo, Int32? expected)
		{
			switch (command[0])
			{
				case "ticket":
					await stream.WriteAsync(SampleTickets.Reference(columns, logo));
					return 0;
				case "receipt":
					await stream.WriteAsync(SampleTickets.Receipt(columns));
					return 0;
				case "long":
					await stream.WriteAsync(SampleTickets.LongLine(columns));
					return 0;
				case "text":
					await stream.WriteAsync(Encoding.Latin1.GetBytes(String.Join(" ", command.GetRange(1, command.Count - 1)) + "\n"));
					return 0;
				case "status":
					return await QueryAsync(stream, [0x10, 0x04, Byte.Parse(command[1])], "DLE EOT " + command[1], expected);
				case "paper":
					return await QueryAsync(stream, [0x1D, 0x72, 0x01], "GS r 1", expected);
				case "id":
					return await ReadIdentityAsync(stream);
				case "enq":
					await stream.WriteAsync(new Byte[] { 0x10, 0x05, Byte.Parse(command[1]) });
					Console.WriteLine("DLE ENQ " + command[1] + " sent");
					return 0;
				case "asb":
					return await ListenAsbAsync(stream, command.Count > 1 ? Int32.Parse(command[1]) : 30);
				case "raw":
					await stream.WriteAsync(await File.ReadAllBytesAsync(command[1]));
					return 0;
				case "hold":
					await Task.Delay(TimeSpan.FromSeconds(Int32.Parse(command[1])));
					return 0;
				default:
					Console.WriteLine(Usage);
					return 1;
			}
		}

		private static async Task<Int32> QueryAsync(NetworkStream stream, Byte[] request, String label, Int32? expected)
		{
			await stream.WriteAsync(request);
			var answer = new Byte[1];
			using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
			var read = await stream.ReadAsync(answer.AsMemory(), timeout.Token);
			if (read != 1)
			{
				Console.Error.WriteLine(label + ": no answer");
				return 1;
			}

			Console.WriteLine("{0}: 0x{1:X2} ({2})", label, answer[0], Convert.ToString(answer[0], 2).PadLeft(8, '0'));
			if (expected.HasValue && expected.Value != answer[0])
			{
				Console.Error.WriteLine("Expected 0x{0:X2}", expected.Value);
				return 1;
			}

			return 0;
		}

		private static async Task<Int32> ReadIdentityAsync(NetworkStream stream)
		{
			foreach (var n in new Byte[] { 67, 65 })
			{
				await stream.WriteAsync(new Byte[] { 0x1D, 0x49, n });
				var text = new List<Byte>();
				var one = new Byte[1];
				using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
				while (true)
				{
					var read = await stream.ReadAsync(one.AsMemory(), timeout.Token);
					if (read == 0 || one[0] == 0)
						break;

					text.Add(one[0]);
				}

				Console.WriteLine("GS I {0}: {1}", n, Encoding.ASCII.GetString(text.ToArray()).TrimStart('_'));
			}

			return 0;
		}

		private static async Task<Int32> ListenAsbAsync(NetworkStream stream, Int32 seconds)
		{
			await stream.WriteAsync(new Byte[] { 0x1D, 0x61, 0xFF });
			Console.WriteLine("ASB armed, waiting {0} s for status changes...", seconds);
			using CancellationTokenSource limit = new(TimeSpan.FromSeconds(seconds));
			var buffer = new Byte[4];
			try
			{
				while (!limit.IsCancellationRequested)
				{
					var total = 0;
					while (total < 4)
					{
						var read = await stream.ReadAsync(buffer.AsMemory(total, 4 - total), limit.Token);
						if (read == 0)
							return 0;

						total += read;
					}

					Console.WriteLine("ASB: {0:X2} {1:X2} {2:X2} {3:X2}", buffer[0], buffer[1], buffer[2], buffer[3]);
				}
			}
			catch (OperationCanceledException)
			{
				// End of the listening period.
			}

			return 0;
		}
	}
}