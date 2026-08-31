namespace CaelestiaWin.Core.Models;

public sealed record WifiConnectionRequest(
    string Ssid,
    string? Password,
    string Authentication,
    string Encryption,
    bool IsSavedProfile);
