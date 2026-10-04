namespace MimaEmuPrinter.Core.Protocol
{
	using System;

	public enum StatusRequestKind
	{
		/// <summary>DLE EOT n: real-time status transmission.</summary>
		DleEot,

		/// <summary>DLE ENQ n: real-time request to the printer.</summary>
		DleEnq,

		/// <summary>GS r n: transmit status.</summary>
		GsR,

		/// <summary>GS I n: transmit printer identity.</summary>
		GsI,

		/// <summary>GS a n: enable or disable automatic status back.</summary>
		GsA,
	}

	public readonly record struct StatusRequest(StatusRequestKind Kind, Byte Value);

	/// <summary>Receives the semantic commands extracted from the ESC/POS byte stream.</summary>
	public interface IEscPosHandler
	{
		/// <summary>A printable byte (0x20 and above), still to be decoded with the current code page.</summary>
		void PrintableByte(Byte value);

		void LineFeed();

		void CarriageReturn();

		void HorizontalTab();

		/// <summary>ESC @.</summary>
		void Initialize();

		/// <summary>ESC ! n.</summary>
		void SetPrintMode(Byte value);

		/// <summary>GS ! n.</summary>
		void SetCharacterSize(Byte value);

		/// <summary>ESC a n.</summary>
		void SetAlignment(Byte value);

		/// <summary>ESC E n.</summary>
		void SetEmphasis(Byte value);

		/// <summary>ESC - n.</summary>
		void SetUnderline(Byte value);

		/// <summary>ESC t n.</summary>
		void SetCodePage(Byte value);

		/// <summary>ESC d n.</summary>
		void FeedLines(Int32 lines);

		/// <summary>ESC J n.</summary>
		void FeedDots(Int32 dots);

		/// <summary>GS V: cut mark, end of ticket.</summary>
		void Cut();

		/// <summary>GS k.</summary>
		void PrintBarcode(String data);

		/// <summary>GS ( k, print of the stored QR symbol.</summary>
		void PrintQrCode(String data);

		/// <summary>GS v 0.</summary>
		void PrintRaster(Int32 widthDots, Int32 heightDots);

		/// <summary>ESC p.</summary>
		void PulseDrawer(Byte pin);

		/// <summary>Any sequence the emulator does not interpret.</summary>
		void IgnoreCommand(String name);

		/// <summary>DLE EOT, DLE ENQ, GS r, GS I, GS a: answered immediately.</summary>
		void RealTime(StatusRequest request);
	}
}