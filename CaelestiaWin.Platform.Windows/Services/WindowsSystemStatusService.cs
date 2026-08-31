using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.Platform.Windows.Interop;
using Microsoft.Win32;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsSystemStatusService(IUiDispatcher uiDispatcher, IDiagnosticLogService logService) : ISystemStatusService, IDisposable
{
    private static readonly HashSet<char> InvalidFileNameChars = new(Path.GetInvalidFileNameChars());
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(5));
    private readonly SemaphoreSlim _wifiQueryLock = new(1, 1);
    private readonly SemaphoreSlim _brightnessLock = new(1, 1);
    private readonly Dictionary<string, string> _audioSessionIconCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _loopCts;

    public SystemStatusModel CurrentStatus { get; } = new();

    public void Start()
    {
        if (_loopCts is not null)
        {
            return;
        }

        _loopCts = new CancellationTokenSource();
        _ = RefreshAsync();
        _ = PollAsync(_loopCts.Token);
    }

    public void Stop()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
    }

    public void SetVolume(double volumePercent)
    {
        var normalized = Math.Clamp(volumePercent / 100d, 0d, 1d);

        try
        {
            if (!TryGetEndpoint(out var endpoint))
            {
                return;
            }

            try
            {
                _ = endpoint!.SetMasterVolumeLevelScalar((float)normalized, Guid.Empty);
            }
            finally
            {
                ReleaseEndpoint(endpoint!);
            }

            CurrentStatus.VolumePercent = Math.Round(normalized * 100d, 0);
        }
        catch (Exception exception)
        {
            logService.Error("Failed to set system volume.", exception);
        }
    }

    public void AdjustVolume(double deltaPercent)
    {
        SetVolume(CurrentStatus.VolumePercent + deltaPercent);
    }

    public void ToggleMute()
    {
        try
        {
            if (!TryGetEndpoint(out var endpoint))
            {
                return;
            }

            bool isMuted;
            try
            {
                _ = endpoint!.GetMute(out isMuted);
                _ = endpoint.SetMute(!isMuted, Guid.Empty);
            }
            finally
            {
                ReleaseEndpoint(endpoint!);
            }

            CurrentStatus.IsMuted = !isMuted;
        }
        catch (Exception exception)
        {
            logService.Error("Failed to toggle system mute.", exception);
        }
    }

    public void AdjustBrightness(int deltaPercent)
    {
        _ = Task.Run(async () =>
        {
            await _brightnessLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var current = TryReadDisplayBrightness();
                if (current is null)
                {
                    logService.Warn("Brightness function key was pressed, but no WMI brightness-capable display was found.");
                    return;
                }

                var next = Math.Clamp(current.Value + deltaPercent, 0, 100);
                if (!TrySetDisplayBrightness(next))
                {
                    logService.Warn("Brightness function key was pressed, but Windows rejected the brightness change.", new Dictionary<string, object?>
                    {
                        ["requestedBrightness"] = next
                    });
                }
            }
            catch (Exception exception)
            {
                logService.Warn("Failed to adjust display brightness.", new Dictionary<string, object?>
                {
                    ["error"] = exception.Message
                });
            }
            finally
            {
                _brightnessLock.Release();
            }
        });
    }

    public void ToggleWifi()
    {
        try
        {
            var enableWifi = !CurrentStatus.WifiEnabled;
            foreach (var interfaceName in GetWirelessInterfaceNames())
            {
                var state = enableWifi ? "ENABLED" : "DISABLED";
                _ = ExecuteNetsh($"interface set interface name=\"{interfaceName}\" admin={state}");
            }

            CurrentStatus.WifiEnabled = enableWifi;
            _ = RefreshAsync();
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to toggle Wi-Fi state.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
    }

    public void ToggleBluetooth()
    {
        if (!CurrentStatus.BluetoothAvailable)
        {
            logService.Info("Bluetooth toggle ignored because no Bluetooth radio was detected.");
            return;
        }

        CurrentStatus.BluetoothEnabled = !CurrentStatus.BluetoothEnabled;
    }

    public Task<IReadOnlyList<AudioDeviceModel>> GetAudioDevicesAsync(AudioDeviceKind kind, CancellationToken cancellationToken = default)
    {
        return RunStaOperationAsync(() => EnumerateAudioDevices(kind), cancellationToken);
    }

    public Task<IReadOnlyList<AppVolumeSessionModel>> GetAppVolumeSessionsAsync(CancellationToken cancellationToken = default)
    {
        return RunStaOperationAsync(EnumerateAppVolumeSessions, cancellationToken);
    }

    public Task<SystemInformationSnapshot> GetSystemInformationAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(ReadSystemInformation, cancellationToken);
    }

    public async Task<bool> SetDefaultAudioDeviceAsync(AudioDeviceModel device, CancellationToken cancellationToken = default)
    {
        try
        {
            var changed = await RunStaOperationAsync(() => TrySetDefaultAudioDevice(device.Id), cancellationToken).ConfigureAwait(false);
            if (changed)
            {
                await RefreshAsync().ConfigureAwait(false);
            }

            return changed;
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to change the default audio device.", new Dictionary<string, object?>
            {
                ["device"] = device.Name,
                ["kind"] = device.Kind,
                ["error"] = exception.Message
            });
            return false;
        }
    }

    public void SetAppVolume(string sessionId, double volumePercent)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            TrySetAppSessionVolume(sessionId, Math.Clamp(volumePercent / 100d, 0d, 1d));
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to set per-app volume.", new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId,
                ["error"] = exception.Message
            });
        }
    }

    public async Task<IReadOnlyList<WifiNetworkModel>> GetAvailableWifiNetworksAsync(CancellationToken cancellationToken = default)
    {
        await _wifiQueryLock.WaitAsync(cancellationToken);
        try
        {
            var profilesOutput = await ExecuteNetshAsync("wlan show profiles", cancellationToken).ConfigureAwait(false);
            var savedProfiles = ParseProfiles(profilesOutput);
            var activeNetwork = CurrentStatus.ActiveNetworkName;
            var wirelessInterfaceNames = GetWirelessInterfaceNames();
            IReadOnlyList<WifiNetworkModel> nativeNetworks = [];
            // Keep startup stable first: bad WLAN marshalling can terminate the CLR before
            // managed exception handling runs. The netsh path below is slower, but isolated
            // in a child process and has proven to return the full network list on Windows 10.

            var netshNetworks = new List<WifiNetworkModel>();

            foreach (var interfaceName in wirelessInterfaceNames)
            {
                var output = await ExecuteNetshAsync($"wlan show networks mode=bssid interface=\"{EscapeArgument(interfaceName)}\"", cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    netshNetworks.AddRange(ParseAvailableNetworks(output, savedProfiles, activeNetwork));
                }
            }

            if (netshNetworks.Count == 0)
            {
                var output = await ExecuteNetshAsync("wlan show networks mode=bssid", cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    netshNetworks.AddRange(ParseAvailableNetworks(output, savedProfiles, activeNetwork));
                }
            }

            var networks = nativeNetworks
                .Concat(netshNetworks)
                .GroupBy(network => network.Ssid, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(network => network.IsConnected)
                    .ThenByDescending(network => network.SignalQuality)
                    .First())
                .OrderByDescending(network => network.IsConnected)
                .ThenByDescending(network => network.SignalQuality)
                .ThenBy(network => network.Ssid, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            logService.Info("Wi-Fi network scan completed.", new Dictionary<string, object?>
            {
                ["interfaceCount"] = wirelessInterfaceNames.Count,
                ["nativeNetworkCount"] = nativeNetworks.Count,
                ["netshNetworkCount"] = netshNetworks.Count,
                ["networkCount"] = networks.Length
            });

            return networks;
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to enumerate available Wi-Fi networks.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
            return [];
        }
        finally
        {
            _wifiQueryLock.Release();
        }
    }

    public async Task<bool> ConnectToWifiAsync(WifiConnectionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Ssid))
        {
            return false;
        }

        try
        {
            if (!request.IsSavedProfile && !string.IsNullOrWhiteSpace(request.Password))
            {
                var profileAdded = await AddOrReplaceWifiProfileAsync(request, cancellationToken).ConfigureAwait(false);
                if (!profileAdded)
                {
                    return false;
                }
            }

            var output = await ExecuteNetshAsync($"wlan connect name=\"{EscapeArgument(request.Ssid)}\"", cancellationToken).ConfigureAwait(false);
            var connected = output.Contains("completed successfully", StringComparison.OrdinalIgnoreCase)
                            || output.Contains("Connection request was completed successfully", StringComparison.OrdinalIgnoreCase);

            await RefreshAsync().ConfigureAwait(false);
            return connected;
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to connect to a Wi-Fi network.", new Dictionary<string, object?>
            {
                ["ssid"] = request.Ssid,
                ["error"] = exception.Message
            });
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
        _brightnessLock.Dispose();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAsync()
    {
        var snapshot = ReadSnapshot();
        await uiDispatcher.InvokeAsync(() =>
        {
            CurrentStatus.VolumePercent = snapshot.VolumePercent;
            CurrentStatus.IsMuted = snapshot.IsMuted;
            CurrentStatus.NetworkSummary = snapshot.NetworkSummary;
            CurrentStatus.ActiveNetworkName = snapshot.ActiveNetworkName;
            CurrentStatus.BatteryPercent = snapshot.BatteryPercent;
            CurrentStatus.IsBatteryPresent = snapshot.IsBatteryPresent;
            CurrentStatus.WifiEnabled = snapshot.WifiEnabled;
            CurrentStatus.BluetoothAvailable = snapshot.BluetoothAvailable;
            CurrentStatus.BluetoothEnabled = snapshot.BluetoothEnabled;
        });
    }

    private SystemStatusModel ReadSnapshot()
    {
        var snapshot = new SystemStatusModel();

        try
        {
            PopulateVolume(snapshot);
        }
        catch (Exception exception)
        {
            logService.Warn("Unable to read audio endpoint volume.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }

        PopulateNetwork(snapshot);
        PopulateBattery(snapshot);
        snapshot.BluetoothAvailable = IsBluetoothRadioAvailable();
        snapshot.BluetoothEnabled = snapshot.BluetoothAvailable && CurrentStatus.BluetoothEnabled;

        return snapshot;
    }

    private static void PopulateBattery(SystemStatusModel snapshot)
    {
        if (!Kernel32.GetSystemPowerStatus(out var status))
        {
            snapshot.IsBatteryPresent = false;
            snapshot.BatteryPercent = null;
            return;
        }

        snapshot.IsBatteryPresent = status.BatteryFlag != 128;
        snapshot.BatteryPercent = status.BatteryLifePercent is >= 0 and <= 100
            ? status.BatteryLifePercent
            : null;
    }

    private static void PopulateNetwork(SystemStatusModel snapshot)
    {
        var wirelessInterfaceNames = GetWirelessInterfaceNames();
        var active = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .ToArray();

        var wifiInterface = active.FirstOrDefault(networkInterface => networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        snapshot.WifiEnabled = GetWifiEnabledState(wirelessInterfaceNames);

        if (wifiInterface is not null)
        {
            var activeWifi = TryReadActiveWifiName();
            snapshot.NetworkSummary = "Connected";
            snapshot.ActiveNetworkName = string.IsNullOrWhiteSpace(activeWifi) ? wifiInterface.Name : activeWifi;
            return;
        }

        var names = active.Select(networkInterface => networkInterface.Name)
            .ToArray();

        snapshot.NetworkSummary = names.Length switch
        {
            0 => snapshot.WifiEnabled ? "Not connected" : "Wi-Fi off",
            1 => names[0],
            _ => $"{names[0]} +{names.Length - 1}"
        };
        snapshot.ActiveNetworkName = snapshot.WifiEnabled ? "Not connected" : "Wi-Fi disabled";
    }

    private static void PopulateVolume(SystemStatusModel snapshot)
    {
        if (!TryGetEndpoint(out var endpoint))
        {
            return;
        }

        try
        {
            _ = endpoint!.GetMasterVolumeLevelScalar(out var volume);
            _ = endpoint.GetMute(out var isMuted);
            snapshot.VolumePercent = Math.Round(volume * 100d, 0);
            snapshot.IsMuted = isMuted;
        }
        finally
        {
            ReleaseEndpoint(endpoint!);
        }
    }

    private static bool TryGetEndpoint(out IAudioEndpointVolume? endpointVolume)
    {
        return TryGetEndpoint(EDataFlow.Render, out endpointVolume);
    }

    private static bool TryGetEndpoint(EDataFlow dataFlow, out IAudioEndpointVolume? endpointVolume)
    {
        endpointVolume = null;
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDevice? device = null;
        object? volumeObject = null;

        try
        {
            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(AudioEndpointInterop.MmDeviceEnumeratorClsid)!)!;
            _ = deviceEnumerator.GetDefaultAudioEndpoint(dataFlow, ERole.Multimedia, out device);
            var iid = AudioEndpointInterop.AudioEndpointVolumeIid;
            _ = device.Activate(ref iid, AudioEndpointInterop.ClsctxAll, nint.Zero, out volumeObject);
            endpointVolume = (IAudioEndpointVolume)volumeObject;
            volumeObject = null;
            Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(deviceEnumerator);
            return true;
        }
        catch
        {
            if (volumeObject is not null)
            {
                Marshal.ReleaseComObject(volumeObject);
            }

            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (deviceEnumerator is not null)
            {
                Marshal.ReleaseComObject(deviceEnumerator);
            }

            return false;
        }
    }

    private static void ReleaseEndpoint(IAudioEndpointVolume endpoint)
    {
        Marshal.ReleaseComObject(endpoint);
    }

    private IReadOnlyList<AudioDeviceModel> EnumerateAudioDevices(AudioDeviceKind kind)
    {
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDeviceCollection? collection = null;
        var devices = new List<AudioDeviceModel>();
        var flow = kind == AudioDeviceKind.Input ? EDataFlow.Capture : EDataFlow.Render;
        var defaultId = TryGetDefaultAudioDeviceId(flow);

        try
        {
            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(AudioEndpointInterop.MmDeviceEnumeratorClsid)!)!;
            var endpointResult = deviceEnumerator.EnumAudioEndpoints(flow, AudioEndpointInterop.DeviceStateActive, out collection);
            if (endpointResult == 0)
            {
                _ = collection.GetCount(out var activeCount);
                if (activeCount == 0)
                {
                    Marshal.ReleaseComObject(collection);
                    collection = null;
                    endpointResult = deviceEnumerator.EnumAudioEndpoints(flow, AudioEndpointInterop.DeviceStateAll, out collection);
                }
            }

            if (endpointResult != 0 || collection is null)
            {
                return devices;
            }

            _ = collection.GetCount(out var count);
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    if (collection.Item(index, out device) != 0)
                    {
                        continue;
                    }

                    _ = device.GetId(out var id);
                    devices.Add(new AudioDeviceModel(
                        id,
                        TryReadDeviceName(device) ?? (kind == AudioDeviceKind.Input ? "Input device" : "Output device"),
                        string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase),
                        kind));
                }
                finally
                {
                    if (device is not null)
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to enumerate audio devices.", new Dictionary<string, object?>
            {
                ["kind"] = kind,
                ["error"] = exception.Message
            });
        }
        finally
        {
            if (collection is not null)
            {
                Marshal.ReleaseComObject(collection);
            }

            if (deviceEnumerator is not null)
            {
                Marshal.ReleaseComObject(deviceEnumerator);
            }
        }

        return devices
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<AppVolumeSessionModel> EnumerateAppVolumeSessions()
    {
        var sessions = new List<AppVolumeSessionModel>();
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;
        object? managerObject = null;

        try
        {
            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(AudioEndpointInterop.MmDeviceEnumeratorClsid)!)!;
            _ = deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);
            var iid = AudioEndpointInterop.AudioSessionManager2Iid;
            _ = device.Activate(ref iid, AudioEndpointInterop.ClsctxAll, nint.Zero, out managerObject);
            sessionManager = (IAudioSessionManager2)managerObject;
            managerObject = null;
            _ = sessionManager.GetSessionEnumerator(out sessionEnumerator);
            _ = sessionEnumerator.GetCount(out var count);

            for (var index = 0; index < count; index++)
            {
                IAudioSessionControl? session = null;
                try
                {
                    _ = sessionEnumerator.GetSession(index, out session);
                    if (session is not IAudioSessionControl2 sessionControl2 || session is not ISimpleAudioVolume simpleVolume)
                    {
                        continue;
                    }

                    _ = sessionControl2.GetState(out var state);
                    if (state == AudioSessionState.Expired)
                    {
                        continue;
                    }

                    _ = sessionControl2.GetSessionInstanceIdentifier(out var id);
                    _ = simpleVolume.GetMasterVolume(out var volume);
                    _ = simpleVolume.GetMute(out var isMuted);
                    _ = sessionControl2.GetProcessId(out var processId);
                    var processName = TryGetProcessName(processId);
                    var executablePath = TryGetProcessExecutablePath(processId);
                    var displayName = TryReadSessionDisplayName(sessionControl2, processName);
                    var iconPath = TryImportAppIcon(executablePath, processName, displayName);

                    sessions.Add(new AppVolumeSessionModel(id, displayName, Math.Round(volume * 100d, 0), isMuted, processName, iconPath));
                }
                finally
                {
                    if (session is not null)
                    {
                        Marshal.ReleaseComObject(session);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to enumerate app volume sessions.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
        finally
        {
            if (managerObject is not null)
            {
                Marshal.ReleaseComObject(managerObject);
            }

            if (sessionEnumerator is not null)
            {
                Marshal.ReleaseComObject(sessionEnumerator);
            }

            if (sessionManager is not null)
            {
                Marshal.ReleaseComObject(sessionManager);
            }

            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (deviceEnumerator is not null)
            {
                Marshal.ReleaseComObject(deviceEnumerator);
            }
        }

        return sessions
            .GroupBy(session => session.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(session => session.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private SystemInformationSnapshot ReadSystemInformation()
    {
        var cpuName = TryReadCpuName();
        var memorySummary = TryReadMemorySummary();
        var (gpuName, vramSummary) = TryReadGpuSummary();
        var storageSummary = TryReadStorageSummary();
        var windowsVersion = TryReadWindowsVersion();
        var architecture = $"{RuntimeInformation.OSArchitecture} OS / {RuntimeInformation.ProcessArchitecture} app";
        var deviceSummary = $"{Environment.MachineName}  {Environment.UserName}";

        return new SystemInformationSnapshot(
            string.IsNullOrWhiteSpace(cpuName) ? "CPU details unavailable" : cpuName,
            string.IsNullOrWhiteSpace(memorySummary) ? "Memory details unavailable" : memorySummary,
            string.IsNullOrWhiteSpace(gpuName) ? "GPU details unavailable" : gpuName,
            string.IsNullOrWhiteSpace(vramSummary) ? "VRAM details unavailable" : vramSummary,
            string.IsNullOrWhiteSpace(storageSummary) ? "Storage details unavailable" : storageSummary,
            string.IsNullOrWhiteSpace(windowsVersion) ? "Windows version unavailable" : windowsVersion,
            architecture,
            deviceSummary);
    }

    private static string? TryReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString") as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadMemorySummary()
    {
        try
        {
            var status = new MemoryStatusEx
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };

            if (!Kernel32.GlobalMemoryStatusEx(ref status))
            {
                return null;
            }

            var total = FormatBytes(status.TotalPhys);
            var available = FormatBytes(status.AvailPhys);
            return $"{total} installed  {available} available";
        }
        catch
        {
            return null;
        }
    }

    private static (string? gpuName, string? vramSummary) TryReadGpuSummary()
    {
        const string command = "$gpus = Get-CimInstance Win32_VideoController | Select-Object Name,@{Name='AdapterRam';Expression={[int64]($_.AdapterRAM)}}; $gpus | ConvertTo-Json -Compress";

        try
        {
            var output = ExecutePowerShell(command);
            if (string.IsNullOrWhiteSpace(output))
            {
                return (null, null);
            }

            var cards = ParseGpuEntries(output);
            var primary = cards
                .Where(card => !string.IsNullOrWhiteSpace(card.Name))
                .OrderByDescending(card => card.AdapterRam ?? 0)
                .FirstOrDefault();

            if (primary is null)
            {
                return (null, null);
            }

            var name = primary.Name?.Trim();
            var vram = primary.AdapterRam is > 0
                ? $"{FormatBytes((ulong)primary.AdapterRam.Value)} VRAM"
                : null;

            return (name, vram);
        }
        catch
        {
            return (null, null);
        }
    }

    private static IReadOnlyList<GpuInfoDto> ParseGpuEntries(string json)
    {
        try
        {
            var trimmed = json.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return JsonSerializer.Deserialize<List<GpuInfoDto>>(trimmed) ?? [];
            }

            var entry = JsonSerializer.Deserialize<GpuInfoDto>(trimmed);
            return entry is null ? [] : [entry];
        }
        catch
        {
            return [];
        }
    }

    private static string? TryReadStorageSummary()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
                .ToArray();

            if (drives.Length == 0)
            {
                return null;
            }

            var total = drives.Aggregate<DriveInfo, ulong>(0, (current, drive) => current + (ulong)drive.TotalSize);
            var free = drives.Aggregate<DriveInfo, ulong>(0, (current, drive) => current + (ulong)drive.AvailableFreeSpace);
            var used = total >= free ? total - free : 0;
            return $"{FormatBytes(used)} used of {FormatBytes(total)}";
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadWindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var productName = key?.GetValue("ProductName") as string;
            var displayVersion = key?.GetValue("DisplayVersion") as string;
            var build = key?.GetValue("CurrentBuildNumber") as string ?? key?.GetValue("CurrentBuild") as string;
            var ubr = key?.GetValue("UBR")?.ToString();

            var segments = new List<string>();
            if (!string.IsNullOrWhiteSpace(productName))
            {
                segments.Add(productName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(displayVersion))
            {
                segments.Add(displayVersion.Trim());
            }

            if (!string.IsNullOrWhiteSpace(build))
            {
                segments.Add(string.IsNullOrWhiteSpace(ubr)
                    ? $"Build {build.Trim()}"
                    : $"Build {build.Trim()}.{ubr.Trim()}");
            }

            return segments.Count == 0 ? null : string.Join("  ", segments);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;

        while (value >= 1024d && index < suffixes.Length - 1)
        {
            value /= 1024d;
            index++;
        }

        var format = value >= 100d || index == 0 ? "F0" : "F1";
        return $"{value.ToString(format, CultureInfo.InvariantCulture)} {suffixes[index]}";
    }

    private sealed class GpuInfoDto
    {
        public string? Name { get; set; }

        public long? AdapterRam { get; set; }
    }

    private static bool TrySetDefaultAudioDevice(string deviceId)
    {
        object? policyObject = null;
        try
        {
            policyObject = Activator.CreateInstance(Type.GetTypeFromCLSID(AudioEndpointInterop.PolicyConfigClientClsid)!);
            if (policyObject is not IPolicyConfig policyConfig)
            {
                return false;
            }

            var result = policyConfig.SetDefaultEndpoint(deviceId, ERole.Console);
            result |= policyConfig.SetDefaultEndpoint(deviceId, ERole.Multimedia);
            result |= policyConfig.SetDefaultEndpoint(deviceId, ERole.Communications);
            return result == 0;
        }
        finally
        {
            if (policyObject is not null)
            {
                Marshal.ReleaseComObject(policyObject);
            }
        }
    }

    private static void TrySetAppSessionVolume(string sessionId, double normalizedVolume)
    {
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;
        object? managerObject = null;

        try
        {
            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(AudioEndpointInterop.MmDeviceEnumeratorClsid)!)!;
            _ = deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);
            var iid = AudioEndpointInterop.AudioSessionManager2Iid;
            _ = device.Activate(ref iid, AudioEndpointInterop.ClsctxAll, nint.Zero, out managerObject);
            sessionManager = (IAudioSessionManager2)managerObject;
            managerObject = null;
            _ = sessionManager.GetSessionEnumerator(out sessionEnumerator);
            _ = sessionEnumerator.GetCount(out var count);

            for (var index = 0; index < count; index++)
            {
                IAudioSessionControl? session = null;
                try
                {
                    _ = sessionEnumerator.GetSession(index, out session);
                    if (session is not IAudioSessionControl2 sessionControl2 || session is not ISimpleAudioVolume simpleVolume)
                    {
                        continue;
                    }

                    _ = sessionControl2.GetSessionInstanceIdentifier(out var id);
                    if (string.Equals(id, sessionId, StringComparison.OrdinalIgnoreCase))
                    {
                        _ = simpleVolume.SetMasterVolume((float)normalizedVolume, Guid.Empty);
                        return;
                    }
                }
                finally
                {
                    if (session is not null)
                    {
                        Marshal.ReleaseComObject(session);
                    }
                }
            }
        }
        finally
        {
            if (managerObject is not null)
            {
                Marshal.ReleaseComObject(managerObject);
            }

            if (sessionEnumerator is not null)
            {
                Marshal.ReleaseComObject(sessionEnumerator);
            }

            if (sessionManager is not null)
            {
                Marshal.ReleaseComObject(sessionManager);
            }

            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (deviceEnumerator is not null)
            {
                Marshal.ReleaseComObject(deviceEnumerator);
            }
        }
    }

    private static string? TryGetDefaultAudioDeviceId(EDataFlow flow)
    {
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDevice? device = null;

        try
        {
            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(AudioEndpointInterop.MmDeviceEnumeratorClsid)!)!;
            if (deviceEnumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out device) != 0)
            {
                return null;
            }

            _ = device.GetId(out var id);
            return id;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (deviceEnumerator is not null)
            {
                Marshal.ReleaseComObject(deviceEnumerator);
            }
        }
    }

    private static string? TryReadDeviceName(IMMDevice device)
    {
        IPropertyStore? propertyStore = null;
        try
        {
            if (device.OpenPropertyStore(AudioEndpointInterop.StgmRead, out propertyStore) != 0)
            {
                return null;
            }

            var key = AudioEndpointInterop.DeviceFriendlyNameKey;
            if (propertyStore.GetValue(ref key, out var value) != 0)
            {
                return null;
            }

            try
            {
                return value.GetString();
            }
            finally
            {
                value.Clear();
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (propertyStore is not null)
            {
                Marshal.ReleaseComObject(propertyStore);
            }
        }
    }

    private static string TryReadSessionDisplayName(IAudioSessionControl2 sessionControl, string? processName)
    {
        try
        {
            _ = sessionControl.GetDisplayName(out var displayName);
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }
        }
        catch
        {
        }

        return string.IsNullOrWhiteSpace(processName) ? "Audio session" : processName;
    }

    private static string? TryGetProcessName(uint processId)
    {
        if (processId == 0)
        {
            return "System sounds";
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.IsNullOrWhiteSpace(process.MainWindowTitle)
                ? process.ProcessName
                : process.MainWindowTitle;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetProcessExecutablePath(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBluetoothRadioAvailable()
    {
        nint findHandle = nint.Zero;
        nint radioHandle = nint.Zero;

        try
        {
            var parameters = new BluetoothFindRadioParams
            {
                Size = Marshal.SizeOf<BluetoothFindRadioParams>()
            };
            findHandle = BluetoothInterop.BluetoothFindFirstRadio(ref parameters, out radioHandle);
            return findHandle != nint.Zero && radioHandle != nint.Zero;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (radioHandle != nint.Zero)
            {
                _ = BluetoothInterop.CloseHandle(radioHandle);
            }

            if (findHandle != nint.Zero)
            {
                _ = BluetoothInterop.BluetoothFindRadioClose(findHandle);
            }
        }
    }

    private static IReadOnlyList<string> GetWirelessInterfaceNames()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .Select(networkInterface => networkInterface.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool GetWifiEnabledState(IReadOnlyList<string> interfaceNames)
    {
        if (interfaceNames.Count == 0)
        {
            return false;
        }

        try
        {
            var output = ExecuteNetsh("interface show interface");
            foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var interfaceName in interfaceNames)
                {
                    if (!line.Contains(interfaceName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return line.TrimStart().StartsWith("Enabled", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
        }

        return true;
    }

    private static string? TryReadActiveWifiName()
    {
        try
        {
            var output = ExecuteNetsh("wlan show interfaces");
            var match = Regex.Match(output, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
            if (match.Success)
            {
                var value = match.Groups[1].Value.Trim();
                if (!value.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static IReadOnlyList<string> ParseProfiles(string output)
    {
        return output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Match(line, @"All User Profile\s*:\s*(.+)$"))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<WifiNetworkModel> ParseAvailableNetworks(string output, IReadOnlyList<string> savedProfiles, string activeNetwork)
    {
        var results = new List<WifiNetworkModel>();
        string? currentSsid = null;
        var currentSignal = 0;
        var currentIsSecure = false;
        var currentAuthentication = "Open";
        var currentEncryption = "None";

        void CommitCurrent()
        {
            if (string.IsNullOrWhiteSpace(currentSsid))
            {
                return;
            }

            if (results.Any(entry => entry.Ssid.Equals(currentSsid, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            results.Add(new WifiNetworkModel(
                currentSsid,
                currentSignal,
                currentIsSecure,
                currentSsid.Equals(activeNetwork, StringComparison.OrdinalIgnoreCase),
                savedProfiles.Contains(currentSsid, StringComparer.OrdinalIgnoreCase),
                currentAuthentication,
                currentEncryption));
        }

        foreach (var rawLine in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var ssidMatch = Regex.Match(line, @"^SSID\s+\d+\s*:\s*(.*)$", RegexOptions.IgnoreCase);
            if (ssidMatch.Success)
            {
                CommitCurrent();
                currentSsid = ssidMatch.Groups[1].Value.Trim();
                currentSignal = 0;
                currentIsSecure = false;
                currentAuthentication = "Open";
                currentEncryption = "None";
                continue;
            }

            if (line.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
            {
                var value = line[(line.IndexOf(':') + 1)..].Trim().TrimEnd('%');
                if (int.TryParse(value, out var signal))
                {
                    currentSignal = Math.Max(currentSignal, signal);
                }

                continue;
            }

            if (line.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase))
            {
                currentAuthentication = line[(line.IndexOf(':') + 1)..].Trim();
                currentIsSecure = !currentAuthentication.Contains("Open", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.StartsWith("Encryption", StringComparison.OrdinalIgnoreCase))
            {
                currentEncryption = line[(line.IndexOf(':') + 1)..].Trim();
            }
        }

        CommitCurrent();

        return results
            .OrderByDescending(entry => entry.IsConnected)
            .ThenByDescending(entry => entry.SignalQuality)
            .ThenBy(entry => entry.Ssid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<bool> AddOrReplaceWifiProfileAsync(WifiConnectionRequest request, CancellationToken cancellationToken)
    {
        var securityDefinition = TryBuildSecurityDefinition(request.Authentication, request.Encryption);
        if (securityDefinition is null)
        {
            logService.Warn("Nebula could not create a Wi-Fi profile for this network security type.", new Dictionary<string, object?>
            {
                ["ssid"] = request.Ssid,
                ["authentication"] = request.Authentication,
                ["encryption"] = request.Encryption
            });
            return false;
        }

        var profilePath = Path.Combine(Path.GetTempPath(), $"nebula-wifi-{Guid.NewGuid():N}.xml");

        try
        {
            await File.WriteAllTextAsync(profilePath, BuildWifiProfileXml(request, securityDefinition.Value.authentication, securityDefinition.Value.encryption), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var output = await ExecuteNetshAsync($"wlan add profile filename=\"{EscapeArgument(profilePath)}\" user=current", cancellationToken).ConfigureAwait(false);
            return output.Contains("added on interface", StringComparison.OrdinalIgnoreCase)
                   || output.Contains("is added to interface", StringComparison.OrdinalIgnoreCase)
                   || output.Contains("Profile", StringComparison.OrdinalIgnoreCase) && output.Contains("added", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to create a Wi-Fi profile for password-based connection.", new Dictionary<string, object?>
            {
                ["ssid"] = request.Ssid,
                ["error"] = exception.Message
            });
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(profilePath))
                {
                    File.Delete(profilePath);
                }
            }
            catch
            {
            }
        }
    }

    private static string BuildWifiProfileXml(WifiConnectionRequest request, string authentication, string encryption)
    {
        var escapedSsid = SecurityElement.Escape(request.Ssid) ?? request.Ssid;
        var hexSsid = Convert.ToHexString(Encoding.UTF8.GetBytes(request.Ssid));
        var escapedPassword = SecurityElement.Escape(request.Password ?? string.Empty) ?? string.Empty;

        return $$"""
                 <?xml version="1.0"?>
                 <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
                   <name>{{escapedSsid}}</name>
                   <SSIDConfig>
                     <SSID>
                       <hex>{{hexSsid}}</hex>
                       <name>{{escapedSsid}}</name>
                     </SSID>
                   </SSIDConfig>
                   <connectionType>ESS</connectionType>
                   <connectionMode>manual</connectionMode>
                   <MSM>
                     <security>
                       <authEncryption>
                         <authentication>{{authentication}}</authentication>
                         <encryption>{{encryption}}</encryption>
                         <useOneX>false</useOneX>
                       </authEncryption>
                       <sharedKey>
                         <keyType>passPhrase</keyType>
                         <protected>false</protected>
                         <keyMaterial>{{escapedPassword}}</keyMaterial>
                       </sharedKey>
                     </security>
                   </MSM>
                 </WLANProfile>
                 """;
    }

    private static (string authentication, string encryption)? TryBuildSecurityDefinition(string authentication, string encryption)
    {
        if (authentication.Contains("WPA3-Personal", StringComparison.OrdinalIgnoreCase))
        {
            return ("WPA3SAE", NormalizeEncryption(encryption));
        }

        if (authentication.Contains("WPA2-Personal", StringComparison.OrdinalIgnoreCase))
        {
            return ("WPA2PSK", NormalizeEncryption(encryption));
        }

        if (authentication.Contains("WPA-Personal", StringComparison.OrdinalIgnoreCase))
        {
            return ("WPAPSK", NormalizeEncryption(encryption));
        }

        if (authentication.Contains("Open", StringComparison.OrdinalIgnoreCase))
        {
            return ("open", "none");
        }

        return null;
    }

    private static string NormalizeEncryption(string encryption)
    {
        if (encryption.Contains("TKIP", StringComparison.OrdinalIgnoreCase))
        {
            return "TKIP";
        }

        if (encryption.Contains("None", StringComparison.OrdinalIgnoreCase))
        {
            return "none";
        }

        return "AES";
    }

    private static string EscapeArgument(string value) => value.Replace("\"", "\"\"", StringComparison.Ordinal);

    private static string ExecuteNetsh(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return string.IsNullOrWhiteSpace(output) ? error : output;
    }

    private static int? TryReadDisplayBrightness()
    {
        var output = ExecutePowerShell(
            "(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop | Select-Object -First 1 -ExpandProperty CurrentBrightness)");
        return int.TryParse(output.Trim(), out var brightness)
            ? Math.Clamp(brightness, 0, 100)
            : null;
    }

    private static bool TrySetDisplayBrightness(int brightness)
    {
        var command = "$methods = Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods -ErrorAction Stop; " +
                      $"Invoke-CimMethod -InputObject $methods -MethodName WmiSetBrightness -Arguments @{{ Timeout = 1; Brightness = {brightness} }} -ErrorAction Stop | Out-Null";
        _ = ExecutePowerShell(command);
        return true;
    }

    private static string ExecutePowerShell(string command)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(4000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return string.Empty;
        }

        Task.WaitAll([outputTask, errorTask], 1000);

        var output = outputTask.IsCompletedSuccessfully ? outputTask.Result : string.Empty;
        var error = errorTask.IsCompletedSuccessfully ? errorTask.Result : string.Empty;
        return string.IsNullOrWhiteSpace(output) ? error : output;
    }

    private static async Task<string> ExecuteNetshAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(output) ? error : output;
    }

    private string? TryImportAppIcon(string? executablePath, string? processName, string displayName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return null;
            }

            if (_audioSessionIconCache.TryGetValue(executablePath, out var cachedPath) && File.Exists(cachedPath))
            {
                return cachedPath;
            }

            var iconDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NebulaShell",
                "audio-session-icons");
            Directory.CreateDirectory(iconDirectory);

            var seedName = string.IsNullOrWhiteSpace(processName) ? displayName : processName;
            var safeName = CreateSafeFileName(seedName);
            var iconHandle = TryExtractIconHandle(executablePath);
            if (iconHandle == nint.Zero)
            {
                return null;
            }

            string iconPath;
            try
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(48, 48));
                if (bitmapSource.CanFreeze)
                {
                    bitmapSource.Freeze();
                }

                iconPath = ShellAssetCache.SaveBitmapSourceAsPng(iconDirectory, safeName, bitmapSource);
            }
            finally
            {
                _ = DestroyIcon(iconHandle);
            }

            _audioSessionIconCache[executablePath] = iconPath;
            return iconPath;
        }
        catch
        {
            return null;
        }
    }

    private static string CreateSafeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "audio-session";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(InvalidFileNameChars.Contains(character) ? '_' : character);
        }

        return builder.Length == 0 ? "audio-session" : builder.ToString();
    }

    private static nint TryExtractIconHandle(string executablePath)
    {
        var result = SHGetFileInfo(
            executablePath,
            0,
            out var fileInfo,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            ShgfiIcon | ShgfiLargeIcon);
        return result == nint.Zero ? nint.Zero : fileInfo.hIcon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private static Task<T> RunStaOperationAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration cancellationRegistration = default;
        var thread = new Thread(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completionSource.TrySetCanceled(cancellationToken);
                    return;
                }

                completionSource.TrySetResult(operation());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completionSource.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completionSource.TrySetException(exception);
            }
            finally
            {
                cancellationRegistration.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "NebulaAudioStaWorker"
        };

        thread.SetApartmentState(ApartmentState.STA);
        cancellationRegistration = cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken));
        thread.Start();
        return completionSource.Task;
    }
}
