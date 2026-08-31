using CaelestiaWin.Core.Common;

namespace CaelestiaWin.Core.Models;

public sealed class SystemStatusModel : ObservableObjectBase
{
    private double _volumePercent = 72d;
    private bool _isMuted;
    private string _networkSummary = "Offline";
    private string _activeNetworkName = "Not connected";
    private int? _batteryPercent;
    private bool _isBatteryPresent;
    private bool _wifiEnabled = true;
    private bool _bluetoothAvailable;
    private bool _bluetoothEnabled;

    public double VolumePercent
    {
        get => _volumePercent;
        set => SetProperty(ref _volumePercent, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetProperty(ref _isMuted, value);
    }

    public string NetworkSummary
    {
        get => _networkSummary;
        set => SetProperty(ref _networkSummary, value);
    }

    public string ActiveNetworkName
    {
        get => _activeNetworkName;
        set => SetProperty(ref _activeNetworkName, value);
    }

    public int? BatteryPercent
    {
        get => _batteryPercent;
        set => SetProperty(ref _batteryPercent, value);
    }

    public bool IsBatteryPresent
    {
        get => _isBatteryPresent;
        set => SetProperty(ref _isBatteryPresent, value);
    }

    public bool WifiEnabled
    {
        get => _wifiEnabled;
        set => SetProperty(ref _wifiEnabled, value);
    }

    public bool BluetoothAvailable
    {
        get => _bluetoothAvailable;
        set => SetProperty(ref _bluetoothAvailable, value);
    }

    public bool BluetoothEnabled
    {
        get => _bluetoothEnabled;
        set => SetProperty(ref _bluetoothEnabled, value);
    }
}
