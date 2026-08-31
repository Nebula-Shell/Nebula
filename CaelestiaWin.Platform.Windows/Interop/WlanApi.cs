using System.Runtime.InteropServices;
using System.Text;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Platform.Windows.Interop;

internal static class WlanApi
{
    private const uint WlanClientVersion = 2;
    private const uint WlanAvailableNetworkConnected = 0x00000001;
    private const uint WlanAvailableNetworkHasProfile = 0x00000002;

    public static IReadOnlyList<WifiNetworkModel> GetAvailableNetworks(IReadOnlyList<string> savedProfiles, string activeNetwork)
    {
        var openResult = WlanOpenHandle(WlanClientVersion, nint.Zero, out _, out var clientHandle);
        if (openResult != 0)
        {
            return [];
        }

        try
        {
            var interfaces = EnumerateInterfaces(clientHandle);
            if (interfaces.Count == 0)
            {
                return [];
            }

            var requestedScan = false;
            foreach (var wlanInterface in interfaces)
            {
                // WlanScan is asynchronous. Request it first, then read the refreshed cached list below.
                var interfaceGuid = wlanInterface.InterfaceGuid;
                requestedScan |= WlanScan(clientHandle, ref interfaceGuid, nint.Zero, nint.Zero, nint.Zero) == 0;
            }

            if (requestedScan)
            {
                Thread.Sleep(2400);
            }

            var networks = new List<WifiNetworkModel>();
            foreach (var wlanInterface in interfaces)
            {
                networks.AddRange(ReadAvailableNetworks(clientHandle, wlanInterface.InterfaceGuid, savedProfiles, activeNetwork));
                networks.AddRange(ReadBssNetworks(clientHandle, wlanInterface.InterfaceGuid, savedProfiles, activeNetwork));
            }

            return networks
                .GroupBy(network => network.Ssid, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(network => network.IsConnected)
                    .ThenByDescending(network => network.SignalQuality)
                    .First())
                .OrderByDescending(network => network.IsConnected)
                .ThenByDescending(network => network.SignalQuality)
                .ThenBy(network => network.Ssid, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _ = WlanCloseHandle(clientHandle, nint.Zero);
        }
    }

    private static IReadOnlyList<WlanInterfaceInfo> EnumerateInterfaces(nint clientHandle)
    {
        var result = WlanEnumInterfaces(clientHandle, nint.Zero, out var listPointer);
        if (result != 0 || listPointer == nint.Zero)
        {
            return [];
        }

        try
        {
            var itemCount = Marshal.ReadInt32(listPointer);
            var headerSize = sizeof(int) * 2;
            var itemSize = Marshal.SizeOf<WlanInterfaceInfo>();
            var interfaces = new List<WlanInterfaceInfo>(Math.Max(itemCount, 0));

            for (var index = 0; index < itemCount; index++)
            {
                var itemPointer = nint.Add(listPointer, headerSize + itemSize * index);
                interfaces.Add(Marshal.PtrToStructure<WlanInterfaceInfo>(itemPointer));
            }

            return interfaces;
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    private static IReadOnlyList<WifiNetworkModel> ReadAvailableNetworks(
        nint clientHandle,
        Guid interfaceGuid,
        IReadOnlyList<string> savedProfiles,
        string activeNetwork)
    {
        var result = WlanGetAvailableNetworkList(clientHandle, ref interfaceGuid, 0, nint.Zero, out var listPointer);
        if (result != 0 || listPointer == nint.Zero)
        {
            return [];
        }

        try
        {
            var itemCount = Marshal.ReadInt32(listPointer);
            var headerSize = sizeof(int) * 2;
            var itemSize = Marshal.SizeOf<WlanAvailableNetwork>();
            var networks = new List<WifiNetworkModel>(Math.Max(itemCount, 0));

            for (var index = 0; index < itemCount; index++)
            {
                var itemPointer = nint.Add(listPointer, headerSize + itemSize * index);
                var availableNetwork = Marshal.PtrToStructure<WlanAvailableNetwork>(itemPointer);
                var ssid = DecodeSsid(availableNetwork.Dot11Ssid);
                if (string.IsNullOrWhiteSpace(ssid))
                {
                    continue;
                }

                var isConnected = (availableNetwork.Flags & WlanAvailableNetworkConnected) != 0
                                  || ssid.Equals(activeNetwork, StringComparison.OrdinalIgnoreCase);
                var isSavedProfile = (availableNetwork.Flags & WlanAvailableNetworkHasProfile) != 0
                                     || savedProfiles.Contains(ssid, StringComparer.OrdinalIgnoreCase);

                networks.Add(new WifiNetworkModel(
                    ssid,
                    Math.Clamp((int)availableNetwork.SignalQuality, 0, 100),
                    availableNetwork.SecurityEnabled,
                    isConnected,
                    isSavedProfile,
                    FormatAuthentication(availableNetwork.DefaultAuthAlgorithm),
                    FormatCipher(availableNetwork.DefaultCipherAlgorithm)));
            }

            return networks;
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    private static IReadOnlyList<WifiNetworkModel> ReadBssNetworks(
        nint clientHandle,
        Guid interfaceGuid,
        IReadOnlyList<string> savedProfiles,
        string activeNetwork)
    {
        var result = WlanGetNetworkBssList(clientHandle, ref interfaceGuid, nint.Zero, Dot11BssType.Any, false, nint.Zero, out var listPointer);
        if (result != 0 || listPointer == nint.Zero)
        {
            return [];
        }

        try
        {
            var itemCount = Marshal.ReadInt32(listPointer);
            var headerSize = sizeof(int) * 2;
            var itemSize = Marshal.SizeOf<WlanBssEntry>();
            var networks = new List<WifiNetworkModel>(Math.Max(itemCount, 0));

            for (var index = 0; index < itemCount; index++)
            {
                var itemPointer = nint.Add(listPointer, headerSize + itemSize * index);
                var bssEntry = Marshal.PtrToStructure<WlanBssEntry>(itemPointer);
                var ssid = DecodeSsid(bssEntry.Dot11Ssid);
                if (string.IsNullOrWhiteSpace(ssid))
                {
                    continue;
                }

                var isSecure = (bssEntry.CapabilityInformation & 0x0010) != 0;
                networks.Add(new WifiNetworkModel(
                    ssid,
                    Math.Clamp((int)bssEntry.LinkQuality, 0, 100),
                    isSecure,
                    ssid.Equals(activeNetwork, StringComparison.OrdinalIgnoreCase),
                    savedProfiles.Contains(ssid, StringComparer.OrdinalIgnoreCase),
                    isSecure ? "Secured" : "Open",
                    isSecure ? "Unknown" : "None"));
            }

            return networks;
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    private static string DecodeSsid(Dot11Ssid ssid)
    {
        if (ssid.SsidLength == 0 || ssid.Ssid is null || ssid.Ssid.Length == 0)
        {
            return string.Empty;
        }

        var length = (int)Math.Min(ssid.SsidLength, (uint)Math.Min(ssid.Ssid.Length, 32));
        if (length <= 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(ssid.Ssid, 0, length).TrimEnd('\0');
    }

    private static string FormatAuthentication(Dot11AuthAlgorithm authentication)
    {
        return authentication switch
        {
            Dot11AuthAlgorithm.Open => "Open",
            Dot11AuthAlgorithm.SharedKey => "Shared",
            Dot11AuthAlgorithm.Wpa => "WPA-Enterprise",
            Dot11AuthAlgorithm.WpaPsk => "WPA-Personal",
            Dot11AuthAlgorithm.WpaNone => "WPA-None",
            Dot11AuthAlgorithm.Rsna => "WPA2-Enterprise",
            Dot11AuthAlgorithm.RsnaPsk => "WPA2-Personal",
            Dot11AuthAlgorithm.Wpa3 => "WPA3-Enterprise",
            Dot11AuthAlgorithm.Wpa3Sae => "WPA3-Personal",
            Dot11AuthAlgorithm.Owe => "Enhanced Open",
            _ => authentication.ToString()
        };
    }

    private static string FormatCipher(Dot11CipherAlgorithm cipher)
    {
        return cipher switch
        {
            Dot11CipherAlgorithm.None => "None",
            Dot11CipherAlgorithm.Wep40 => "WEP-40",
            Dot11CipherAlgorithm.Tkip => "TKIP",
            Dot11CipherAlgorithm.Ccmp => "AES",
            Dot11CipherAlgorithm.Wep104 => "WEP-104",
            Dot11CipherAlgorithm.Bip => "BIP",
            Dot11CipherAlgorithm.Gcmp => "GCMP",
            Dot11CipherAlgorithm.WpaUseGroup => "Group",
            Dot11CipherAlgorithm.Wep => "WEP",
            _ => cipher.ToString()
        };
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint clientVersion, nint reserved, out uint negotiatedVersion, out nint clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(nint clientHandle, nint reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(nint clientHandle, nint reserved, out nint interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanScan(nint clientHandle, ref Guid interfaceGuid, nint dot11Ssid, nint ieData, nint reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetAvailableNetworkList(nint clientHandle, ref Guid interfaceGuid, uint flags, nint reserved, out nint availableNetworkList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetNetworkBssList(
        nint clientHandle,
        ref Guid interfaceGuid,
        nint dot11Ssid,
        Dot11BssType dot11BssType,
        [MarshalAs(UnmanagedType.Bool)] bool securityEnabled,
        nint reserved,
        out nint wlanBssList);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(nint memory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string InterfaceDescription;

        public uint State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint SsidLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Ssid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanAvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;

        public Dot11Ssid Dot11Ssid;
        public uint BssType;
        public uint NumberOfBssids;

        [MarshalAs(UnmanagedType.Bool)]
        public bool NetworkConnectable;

        public uint NotConnectableReason;
        public uint NumberOfPhyTypes;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public uint[] PhyTypes;

        [MarshalAs(UnmanagedType.Bool)]
        public bool MorePhyTypes;

        public uint SignalQuality;

        [MarshalAs(UnmanagedType.Bool)]
        public bool SecurityEnabled;

        public Dot11AuthAlgorithm DefaultAuthAlgorithm;
        public Dot11CipherAlgorithm DefaultCipherAlgorithm;
        public uint Flags;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanBssEntry
    {
        public Dot11Ssid Dot11Ssid;
        public uint PhyId;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Bssid;

        public Dot11BssType BssType;
        public uint PhyType;
        public int Rssi;
        public uint LinkQuality;

        [MarshalAs(UnmanagedType.U1)]
        public bool InRegDomain;

        public ushort BeaconPeriod;
        public ulong Timestamp;
        public ulong HostTimestamp;
        public ushort CapabilityInformation;
        public uint ChCenterFrequency;
        public WlanRateSet RateSet;
        public uint IeOffset;
        public uint IeSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanRateSet
    {
        public uint RateSetLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)]
        public ushort[] RateSet;
    }

    private enum Dot11BssType : uint
    {
        Infrastructure = 1,
        Independent = 2,
        Any = 3
    }

    private enum Dot11AuthAlgorithm : uint
    {
        Open = 1,
        SharedKey = 2,
        Wpa = 3,
        WpaPsk = 4,
        WpaNone = 5,
        Rsna = 6,
        RsnaPsk = 7,
        Wpa3 = 8,
        Wpa3Sae = 9,
        Owe = 10
    }

    private enum Dot11CipherAlgorithm : uint
    {
        None = 0x00,
        Wep40 = 0x01,
        Tkip = 0x02,
        Ccmp = 0x04,
        Wep104 = 0x05,
        Bip = 0x06,
        Gcmp = 0x08,
        WpaUseGroup = 0x100,
        RsnUseGroup = 0x100,
        Wep = 0x101
    }
}
