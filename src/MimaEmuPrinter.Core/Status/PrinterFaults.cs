namespace MimaEmuPrinter.Core.Status
{
	using System;

	/// <summary>The nine fault events the tester can raise on the virtual printer.</summary>
	[Flags]
	public enum PrinterFaults
	{
		None = 0,
		Offline = 1 << 0,
		CoverOpen = 1 << 1,
		PaperEndStop = 1 << 2,
		Error = 1 << 3,
		RecoverableError = 1 << 4,
		CutterError = 1 << 5,
		UnrecoverableError = 1 << 6,
		PaperNearEnd = 1 << 7,
		PaperOut = 1 << 8,
	}
}
