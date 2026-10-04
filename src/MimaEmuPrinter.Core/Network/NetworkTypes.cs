namespace MimaEmuPrinter.Core.Network
{
	using System;
	using System.Collections.Generic;
	using System.Net;
	using System.Net.NetworkInformation;
	using System.Net.Sockets;

	public enum ListenerState
	{
		/// <summary>"Arrêtée".</summary>
		Stopped,

		/// <summary>"En écoute": no cash register connected.</summary>
		Listening,

		/// <summary>"Occupée": a connection is open.</summary>
		Busy,
	}

	public enum ConnectionCloseReason
	{
		/// <summary>The cash register closed the socket.</summary>
		Peer,

		/// <summary>No byte received for the inactivity timeout.</summary>
		Timeout,

		/// <summary>The listener was stopped.</summary>
		Shutdown,
	}

	/// <summary>Timings of the emulator, as fixed by the specification unless a test shortens them.</summary>
	public sealed class PrinterServerOptions
	{
		/// <summary>A job is closed after this time without a new byte.</summary>
		public TimeSpan JobIdleTimeout { get; init; } = TimeSpan.FromMilliseconds(400);

		/// <summary>A connection is closed after this time without a byte.</summary>
		public TimeSpan InactivityTimeout { get; init; } = TimeSpan.FromSeconds(30);
	}

	/// <summary>Local IPv4 addresses, to propose the listening address.</summary>
	public static class NetworkAddressProvider
	{
		public const String LoopbackText = "127.0.0.1";

		public const String AnyText = "0.0.0.0";

		public static IReadOnlyList<IPAddress> GetLocalIPv4Addresses()
		{
			var addresses = new List<IPAddress>();
			try
			{
				foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
				{
					if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
						continue;

					foreach (var information in network.GetIPProperties().UnicastAddresses)
					{
						var address = information.Address;
						if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
							addresses.Add(address);
					}
				}
			}
			catch (Exception ex) when (ex is NetworkInformationException || ex is PlatformNotSupportedException)
			{
				// No network information available: the loopback address remains selectable.
			}

			return addresses;
		}

		/// <summary>The first non-loopback IPv4 address of the machine (link-local ones last), or 127.0.0.1.</summary>
		public static IPAddress GetDefaultListenAddress()
		{
			IPAddress? linkLocal = null;
			foreach (var address in GetLocalIPv4Addresses())
			{
				var bytes = address.GetAddressBytes();
				if (bytes[0] == 169 && bytes[1] == 254)
				{
					linkLocal ??= address;
					continue;
				}

				return address;
			}

			return linkLocal ?? IPAddress.Loopback;
		}

		/// <summary>Addresses offered in the selection list: local addresses, loopback, all interfaces.</summary>
		public static IReadOnlyList<String> GetSelectableAddresses()
		{
			var items = new List<String>();
			foreach (IPAddress address in GetLocalIPv4Addresses())
				items.Add(address.ToString());

			items.Add(LoopbackText);
			items.Add(AnyText);
			return items;
		}
	}
}