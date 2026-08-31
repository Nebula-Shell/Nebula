using System.Runtime.InteropServices;

namespace CaelestiaWin.Platform.Windows.Interop;

internal static class AudioEndpointInterop
{
    public const int ClsctxAll = 23;
    public const int DeviceStateActive = 0x00000001;
    public const int DeviceStateAll = 0x0000000F;
    public const int StgmRead = 0;

    public static readonly Guid MmDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid PolicyConfigClientClsid = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    public static readonly Guid AudioEndpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    public static readonly Guid AudioSessionManager2Iid = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    public static readonly Guid AudioMeterInformationIid = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    public static readonly PropertyKey DeviceFriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
}

internal enum EDataFlow
{
    Render,
    Capture,
    All
}

internal enum ERole
{
    Console,
    Multimedia,
    Communications
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string endpointId, out IMMDevice endpoint);
    int RegisterEndpointNotificationCallback(nint client);
    int UnregisterEndpointNotificationCallback(nint client);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid iid, int dwClsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    int OpenPropertyStore(int access, out IPropertyStore properties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out int state);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    int GetCount(out uint count);
    int Item(uint deviceNumber, out IMMDevice device);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    int GetCount(out uint propertyCount);
    int GetAt(uint propertyIndex, out PropertyKey key);
    int GetValue(ref PropertyKey key, out PropVariant value);
    int SetValue(ref PropertyKey key, ref PropVariant value);
    int Commit();
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    int NotImpl1();
    int NotImpl2();
    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    int RegisterSessionNotification(nint sessionNotification);
    int UnregisterSessionNotification(nint sessionNotification);
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, nint duckNotification);
    int UnregisterDuckNotification(nint duckNotification);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    int GetCount(out int sessionCount);
    int GetSession(int sessionIndex, out IAudioSessionControl session);
}

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    int GetState(out AudioSessionState state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, Guid eventContext);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, Guid eventContext);
    int GetGroupingParam(out Guid groupingId);
    int SetGroupingParam(Guid groupingId, Guid eventContext);
    int RegisterAudioSessionNotification(nint client);
    int UnregisterAudioSessionNotification(nint client);
}

[ComImport]
[Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    int GetState(out AudioSessionState state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, Guid eventContext);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, Guid eventContext);
    int GetGroupingParam(out Guid groupingId);
    int SetGroupingParam(Guid groupingId, Guid eventContext);
    int RegisterAudioSessionNotification(nint client);
    int UnregisterAudioSessionNotification(nint client);
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionIdentifier);
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceIdentifier);
    int GetProcessId(out uint processId);
    int IsSystemSoundsSession();
    int SetDuckingPreference(bool optOut);
}

[ComImport]
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    int GetPeakValue(out float peak);
    int GetMeteringChannelCount(out int channelCount);
    int GetChannelsPeakValues(int channelCount, [Out] float[] peakValues);
    int QueryHardwareSupport(out int hardwareSupportMask);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    int SetMasterVolume(float level, Guid eventContext);
    int GetMasterVolume(out float level);
    int SetMute(bool isMuted, Guid eventContext);
    int GetMute(out bool isMuted);
}

[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    int GetMixFormat();
    int GetDeviceFormat();
    int ResetDeviceFormat();
    int SetDeviceFormat();
    int GetProcessingPeriod();
    int SetProcessingPeriod();
    int GetShareMode();
    int SetShareMode();
    int GetPropertyValue();
    int SetPropertyValue();
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    int SetEndpointVisibility();
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct PropertyKey(Guid formatId, int propertyId)
{
    public readonly Guid FormatId = formatId;
    public readonly int PropertyId = propertyId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort VariantType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public nint PointerValue;
    public int Value;

    public readonly string? GetString()
    {
        return VariantType == 31 && PointerValue != nint.Zero
            ? Marshal.PtrToStringUni(PointerValue)
            : null;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant propVariant);

    public void Clear() => _ = PropVariantClear(ref this);
}

internal enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    int RegisterControlChangeNotify(nint notify);
    int UnregisterControlChangeNotify(nint notify);
    int GetChannelCount(out uint channelCount);
    int SetMasterVolumeLevel(float levelDb, Guid eventContext);
    int SetMasterVolumeLevelScalar(float level, Guid eventContext);
    int GetMasterVolumeLevel(out float levelDb);
    int GetMasterVolumeLevelScalar(out float level);
    int SetChannelVolumeLevel(uint channelNumber, float levelDb, Guid eventContext);
    int SetChannelVolumeLevelScalar(uint channelNumber, float level, Guid eventContext);
    int GetChannelVolumeLevel(uint channelNumber, out float levelDb);
    int GetChannelVolumeLevelScalar(uint channelNumber, out float level);
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, Guid eventContext);
    int GetMute(out bool isMuted);
    int GetVolumeStepInfo(out uint step, out uint stepCount);
    int VolumeStepUp(Guid eventContext);
    int VolumeStepDown(Guid eventContext);
    int QueryHardwareSupport(out uint hardwareSupportMask);
    int GetVolumeRange(out float volumeMinDb, out float volumeMaxDb, out float volumeIncrementDb);
}
