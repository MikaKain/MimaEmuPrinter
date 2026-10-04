namespace MimaEmuPrinter.Core.Archive
{
	using System;
	using System.Collections.Concurrent;
	using System.IO;
	using System.Reflection;
	using PdfSharp.Fonts;

	/// <summary>
	/// Serves the Liberation fonts embedded in the assembly to PDFsharp: no system font, no system binary.
	/// Liberation Mono is the ticket font, Liberation Sans the text font of the page cartridges.
	/// </summary>
	public sealed class EmbeddedFontResolver : IFontResolver
	{
		public const String MonoFamily = "Liberation Mono";

		public const String SansFamily = "Liberation Sans";

		private static readonly Object InstallLock = new Object();
		private static Boolean installed;

		private readonly ConcurrentDictionary<String, Byte[]> cache = new ConcurrentDictionary<String, Byte[]>();

		/// <summary>Registers the resolver once for the process.</summary>
		public static void Install()
		{
			lock (InstallLock)
			{
				if (installed)
				{
					return;
				}

				GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
				installed = true;
			}
		}

		public FontResolverInfo? ResolveTypeface(String familyName, Boolean isBold, Boolean isItalic)
		{
			String baseName = String.Equals(familyName, SansFamily, StringComparison.OrdinalIgnoreCase) ? "LiberationSans" : "LiberationMono";
			return new FontResolverInfo(baseName + (isBold ? "-Bold" : "-Regular"));
		}

		public Byte[]? GetFont(String faceName)
		{
			return cache.GetOrAdd(faceName, LoadFont);
		}

		private static Byte[] LoadFont(String faceName)
		{
			String resourceName = "MimaEmuPrinter.Core.Fonts." + faceName + ".ttf";
			using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
			if (stream == null)
			{
				throw new InvalidOperationException("Embedded font not found: " + resourceName);
			}

			using MemoryStream buffer = new MemoryStream();
			stream.CopyTo(buffer);
			return buffer.ToArray();
		}
	}
}
