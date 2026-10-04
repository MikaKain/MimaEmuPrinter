namespace MimaEmuPrinter.Core.Protocol
{
	using System;
	using System.Collections.Generic;
	using System.Text;

	/// <summary>
	/// Incremental ESC/POS parser. Bytes can arrive in any chunking; every recognised command is
	/// forwarded to the <see cref="IEscPosHandler"/> as soon as its last byte is received, so the
	/// real-time commands (DLE EOT, DLE ENQ, GS r...) are answered without waiting for the job end.
	/// Unknown sequences are consumed and reported as ignored, never an error.
	/// </summary>
	public sealed class EscPosParser
	{
		private const Byte Nul = 0x00;
		private const Byte HorizontalTabCode = 0x09;
		private const Byte LineFeedCode = 0x0A;
		private const Byte CarriageReturnCode = 0x0D;
		private const Byte Dle = 0x10;
		private const Byte Esc = 0x1B;
		private const Byte Fs = 0x1C;
		private const Byte Gs = 0x1D;

		private const Int32 MaxPendingLength = 70000;
		private const Int32 MaxTabStopsLength = 34;

		private static readonly Dictionary<Byte, Int32> EscParameterCounts = new()
		{
			{ (Byte)'@', 0 }, { (Byte)'!', 1 }, { (Byte)'a', 1 }, { (Byte)'E', 1 }, { (Byte)'-', 1 },
			{ (Byte)'t', 1 }, { (Byte)'d', 1 }, { (Byte)'J', 1 }, { (Byte)'p', 3 }, { (Byte)'2', 0 },
			{ (Byte)'3', 1 }, { (Byte)'M', 1 }, { (Byte)'G', 1 }, { (Byte)'R', 1 }, { (Byte)'{', 1 },
			{ (Byte)'V', 1 }, { (Byte)' ', 1 }, { (Byte)'$', 2 }, { (Byte)'\\', 2 }, { (Byte)'=', 1 },
			{ (Byte)'L', 0 }, { (Byte)'S', 0 }, { (Byte)'T', 1 }, { (Byte)'W', 8 }, { (Byte)'%', 1 },
			{ (Byte)'u', 1 }, { (Byte)'v', 1 }, { (Byte)'r', 1 }, { (Byte)'<', 0 }, { (Byte)'?', 1 },
			{ (Byte)'U', 1 }, { (Byte)'e', 1 }, { (Byte)'B', 2 }, { (Byte)'C', 1 }, { (Byte)'i', 0 },
			{ (Byte)'m', 0 },
		};

		private static readonly Dictionary<Byte, Int32> GsParameterCounts = new()
		{
			{ (Byte)'!', 1 }, { (Byte)'a', 1 }, { (Byte)'r', 1 }, { (Byte)'I', 1 }, { (Byte)'B', 1 },
			{ (Byte)'H', 1 }, { (Byte)'f', 1 }, { (Byte)'h', 1 }, { (Byte)'w', 1 }, { (Byte)'/', 1 },
			{ (Byte)'L', 2 }, { (Byte)'W', 2 }, { (Byte)'P', 2 }, { (Byte)'$', 2 }, { (Byte)'\\', 2 },
			{ (Byte)'^', 3 }, { (Byte)'E', 1 }, { (Byte)'C', 1 },
		};

		private readonly IEscPosHandler handler;
		private readonly List<Byte> pending = new(64);
		private Int64 skipRemaining;
		private String qrData = String.Empty;

		public EscPosParser(IEscPosHandler handler)
		{
			this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
		}

		public void Feed(ReadOnlySpan<Byte> data)
		{
			for (var index = 0; index < data.Length; index++)
				Feed(data[index]);
		}

		public void Feed(Byte value)
		{
			if (skipRemaining > 0)
			{
				skipRemaining--;
				return;
			}

			if (pending.Count == 0)
			{
				FeedGround(value);
				return;
			}

			pending.Add(value);
			if (pending.Count >= GetNeededLength() || pending.Count >= MaxPendingLength)
			{
				try
				{
					Dispatch();
				}
				finally
				{
					pending.Clear();
				}
			}
		}

		/// <summary>Drops any half-received command (used when a connection ends).</summary>
		public void Reset()
		{
			pending.Clear();
			skipRemaining = 0;
		}

		private void FeedGround(Byte value)
		{
			switch (value)
			{
				case Nul:
					return;
				case LineFeedCode:
					handler.LineFeed();
					return;
				case CarriageReturnCode:
					handler.CarriageReturn();
					return;
				case HorizontalTabCode:
					handler.HorizontalTab();
					return;
				case Dle:
				case Esc:
				case Fs:
				case Gs:
					pending.Add(value);
					return;
			}

			if (value < 0x20)
			{
				handler.IgnoreCommand(String.Format("0x{0:X2}", value));
				return;
			}

			handler.PrintableByte(value);
		}

		// Total number of bytes (prefix included) the pending command needs, as far as it can be known now.
		private Int32 GetNeededLength()
		{
			if (pending.Count < 2)
				return 2;

			var op = pending[1];
			return pending[0] switch
			{
				Esc => GetEscLength(op),
				Gs => GetGsLength(op),
				Dle => GetDleLength(op),
				Fs => GetFsLength(op),
				_ => pending.Count,
			};
		}

		private Int32 GetEscLength(Byte op)
		{
			return op switch
			{
				(Byte)'*' => 5,
				(Byte)'D' => pending[pending.Count - 1] == Nul || pending.Count >= MaxTabStopsLength ? pending.Count : pending.Count + 1,
				(Byte)'c' => 4,
				_ => 2 + (EscParameterCounts.TryGetValue(op, out Int32 count) ? count : 0),
			};
		}

		private Int32 GetGsLength(Byte op)
		{
			switch (op)
			{
				case (Byte)'V':
					if (pending.Count < 3)
						return 3;
					return pending[2] == 65 || pending[2] == 66 || pending[2] == 97 || pending[2] == 98 ? 4 : 3;
				case (Byte)'k':
					return GetBarcodeLength();
				case (Byte)'(':
					return pending.Count < 5 ? 5 : 5 + ReadLength16(3);
				case (Byte)'8':
					return 7;
				case (Byte)'v':
					if (pending.Count < 3)
						return 3;
					return pending[2] == (Byte)'0' ? 8 : 3;
				case (Byte)'*':
					return 4;
				default:
					return 2 + (GsParameterCounts.TryGetValue(op, out Int32 count) ? count : 0);
			}
		}

		private Int32 GetBarcodeLength()
		{
			if (pending.Count < 3)
				return 3;

			var format = pending[2];
			if (format <= 6)
				return pending[pending.Count - 1] == Nul && pending.Count > 3 ? pending.Count : Math.Min(pending.Count + 1, MaxPendingLength);

			if (format >= 65 && format <= 73)
				return pending.Count < 4 ? 4 : 4 + pending[3];

			return 3;
		}

		private static Int32 GetDleLength(Byte op)
		{
			return op switch
			{
				0x04 or 0x05 => 3,
				_ => 2,
			};
		}

		private Int32 GetFsLength(Byte op)
		{
			return op switch
			{
				(Byte)'(' => pending.Count < 5 ? 5 : 5 + ReadLength16(3),
				(Byte)'S' => 4,
				(Byte)'!' or (Byte)'-' or (Byte)'C' or (Byte)'W' => 3,
				_ => 2,
			};
		}

		private Int32 ReadLength16(Int32 index)
		{
			return pending[index] | (pending[index + 1] << 8);
		}

		private void Dispatch()
		{
			switch (pending[0])
			{
				case Esc:
					DispatchEsc();
					break;
				case Gs:
					DispatchGs();
					break;
				case Dle:
					DispatchDle();
					break;
				default:
					handler.IgnoreCommand(Describe());
					break;
			}
		}

		private void DispatchEsc()
		{
			switch (pending[1])
			{
				case (Byte)'@':
					handler.Initialize();
					break;
				case (Byte)'!':
					handler.SetPrintMode(pending[2]);
					break;
				case (Byte)'a':
					handler.SetAlignment(pending[2]);
					break;
				case (Byte)'E':
					handler.SetEmphasis(pending[2]);
					break;
				case (Byte)'-':
					handler.SetUnderline(pending[2]);
					break;
				case (Byte)'t':
					handler.SetCodePage(pending[2]);
					break;
				case (Byte)'d':
					handler.FeedLines(pending[2]);
					break;
				case (Byte)'J':
					handler.FeedDots(pending[2]);
					break;
				case (Byte)'p':
					handler.PulseDrawer(pending[2]);
					break;
				case (Byte)'*':
					// ESC * m nL nH d1...dk: bit image, data skipped.
					skipRemaining = (pending[3] | (pending[4] << 8)) * (pending[2] == 32 || pending[2] == 33 ? 3 : 1);
					handler.IgnoreCommand(Describe());
					break;
				default:
					handler.IgnoreCommand(Describe());
					break;
			}
		}

		private void DispatchGs()
		{
			switch (pending[1])
			{
				case (Byte)'V':
					handler.Cut();
					break;
				case (Byte)'!':
					handler.SetCharacterSize(pending[2]);
					break;
				case (Byte)'a':
					handler.RealTime(new StatusRequest(StatusRequestKind.GsA, pending[2]));
					break;
				case (Byte)'r':
					handler.RealTime(new StatusRequest(StatusRequestKind.GsR, pending[2]));
					break;
				case (Byte)'I':
					handler.RealTime(new StatusRequest(StatusRequestKind.GsI, pending[2]));
					break;
				case (Byte)'k':
					DispatchBarcode();
					break;
				case (Byte)'(':
					DispatchGsParenthesis();
					break;
				case (Byte)'v':
					if (pending[2] == (Byte)'0')
					{
						Int32 bytesPerRow = pending[4] | (pending[5] << 8);
						Int32 rows = pending[6] | (pending[7] << 8);
						skipRemaining = (Int64)bytesPerRow * rows;
						handler.PrintRaster(bytesPerRow * 8, rows);
					}
					else
					{
						handler.IgnoreCommand(Describe());
					}

					break;
				case (Byte)'8':
					// GS 8 L p1 p2 p3 p4 ...: graphics data, skipped.
					skipRemaining = (Int64)pending[3] | ((Int64)pending[4] << 8) | ((Int64)pending[5] << 16) | ((Int64)pending[6] << 24);
					handler.IgnoreCommand(Describe());
					break;
				case (Byte)'*':
					skipRemaining = pending[2] * pending[3] * 8;
					handler.IgnoreCommand(Describe());
					break;
				default:
					handler.IgnoreCommand(Describe());
					break;
			}
		}

		private void DispatchBarcode()
		{
			var format = pending[2];
			if (format <= 6)
			{
				// Format A: m d1...dk NUL.
				Int32 end = pending[pending.Count - 1] == Nul ? pending.Count - 1 : pending.Count;
				handler.PrintBarcode(DecodeText(3, end));
			}
			else if (format >= 65 && format <= 73)
			{
				// Format B: m n d1...dn.
				handler.PrintBarcode(DecodeText(4, pending.Count));
			}
			else
			{
				handler.IgnoreCommand(Describe());
			}
		}

		private void DispatchGsParenthesis()
		{
			var length = ReadLength16(3);
			if (pending[2] != (Byte)'k' || length < 2)
			{
				handler.IgnoreCommand(Describe());
				return;
			}

			var function = pending[6];
			if (pending[5] != 49)
			{
				handler.IgnoreCommand(Describe());
				return;
			}

			switch (function)
			{
				case 80:
					// GS ( k pL pH 49 80 48 d1...dk: store QR data.
					qrData = length >= 3 ? DecodeText(8, 5 + length) : String.Empty;
					break;
				case 81:
					handler.PrintQrCode(qrData);
					break;
				case 65:
				case 67:
				case 69:
				case 82:
					// Model, module size, error correction level, size query: no effect on the text rendering.
					break;
				default:
					handler.IgnoreCommand(Describe());
					break;
			}
		}

		private void DispatchDle()
		{
			switch (pending[1])
			{
				case 0x04:
					handler.RealTime(new StatusRequest(StatusRequestKind.DleEot, pending[2]));
					break;
				case 0x05:
					handler.RealTime(new StatusRequest(StatusRequestKind.DleEnq, pending[2]));
					break;
				default:
					handler.IgnoreCommand(Describe());
					break;
			}
		}

		private String DecodeText(Int32 start, Int32 end)
		{
			var count = Math.Max(0, Math.Min(end, pending.Count) - start);
			var bytes = new Byte[count];
			pending.CopyTo(start, bytes, 0, count);
			return Encoding.UTF8.GetString(bytes);
		}

		private String Describe()
		{
			String prefix = pending[0] switch
			{
				Esc => "ESC",
				Gs => "GS",
				Dle => "DLE",
				_ => "FS",
			};
			var op = pending[1];
			var opText = op >= 0x21 && op <= 0x7E ? ((Char)op).ToString() : String.Format("0x{0:X2}", op);
			if (pending[0] == Gs && op == (Byte)'(' && pending.Count > 2)
				opText += " " + (Char)pending[2];

			return prefix + " " + opText;
		}
	}
}