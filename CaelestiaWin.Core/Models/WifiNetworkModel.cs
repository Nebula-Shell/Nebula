namespace CaelestiaWin.Core.Models;

public sealed record WifiNetworkModel(
    string Ssid,
    int SignalQuality,
    bool IsSecure,
    bool IsConnected,
    bool IsSavedProfile,
    string Authentication,
    string Encryption);
