using System;
using System.Runtime.InteropServices;

namespace AudioSwapper.Audio;

// ---------------------------------------------------------------------------
// Core Audio (MMDevice API) interop.
//
// Everything in this file except IPolicyConfig is public, documented Windows
// API. IPolicyConfig is NOT documented by Microsoft -- it is the only way to
// change the default audio endpoint programmatically, and is what SoundSwitch,
// EarTrumpet and AudioSwitcher all use. It has been stable since Windows Vista
// and works on Windows 11. See PolicyConfig.cs for the caveats.
// ---------------------------------------------------------------------------

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[Flags]
internal enum DeviceState : uint
{
    Active = 0x1,
    Disabled = 0x2,
    NotPresent = 0x4,
    Unplugged = 0x8,
    All = 0xF,
}

/// <summary>
/// How the endpoint physically presents itself. Windows reports this per
/// device, and we use it to pick a sensible default icon so a freshly
/// discovered device is not just a generic blob.
/// </summary>
internal enum EndpointFormFactor
{
    RemoteNetworkDevice = 0,
    Speakers = 1,
    LineLevel = 2,
    Headphones = 3,
    Microphone = 4,
    Headset = 5,
    Handset = 6,
    UnknownDigitalPassthrough = 7,
    Spdif = 8,
    DigitalAudioDisplayDevice = 9, // HDMI / DisplayPort -- monitors, TVs, projectors
    UnknownFormFactor = 10,
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;

    public PropertyKey(string formatId, int propertyId)
    {
        FormatId = new Guid(formatId);
        PropertyId = propertyId;
    }
}

internal static class PropertyKeys
{
    // "Speakers (Realtek(R) Audio)" -- the name Windows shows in the volume flyout.
    public static PropertyKey DeviceFriendlyName = new("a45c254e-df1c-4efd-8020-67d146a850e0", 14);

    // "Speakers" -- the short endpoint description, without the adapter name.
    public static PropertyKey DeviceDescription = new("a45c254e-df1c-4efd-8020-67d146a850e0", 2);

    // "Razer USB Sound Card" -- the adapter behind the endpoint.
    // Property 6, not 2: property 2 in the same set is the device interface
    // path ("{1}.USB\VID_1532&PID_0529&MI_00\7&1AF954B&0&0000"), which reads as
    // a bug when it lands in the UI.
    public static PropertyKey InterfaceFriendlyName = new("b3f8fa53-0004-438e-9003-51a46e139bfc", 6);

    // EndpointFormFactor, as a VT_UI4.
    public static PropertyKey FormFactor = new("1da5d803-d492-4edd-8c23-e0c0ffee7f0e", 0);
}

/// <summary>
/// Just enough of PROPVARIANT to read the string and uint properties we ask
/// for. The union starts at offset 8 (vt plus three reserved WORDs).
///
/// The explicit Size matters and is easy to get wrong: the real PROPVARIANT is
/// 24 bytes on x64 because its union is as wide as a DECIMAL. Declaring only
/// the fields we read gives a 16-byte struct, and PropVariantInit inside the
/// property store then memsets 24 bytes over a 16-byte buffer -- corrupting
/// whatever follows it and making reads come back empty at random. The trailing
/// padding is never touched by us; it exists purely to size the buffer.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort VarType;
    [FieldOffset(8)] public IntPtr PointerValue;
    [FieldOffset(8)] public uint UIntValue;
    [FieldOffset(8)] public int IntValue;

    private const ushort VT_EMPTY = 0;
    private const ushort VT_UI4 = 19;
    private const ushort VT_LPWSTR = 31;

    public readonly string? AsString() =>
        VarType == VT_LPWSTR && PointerValue != IntPtr.Zero
            ? Marshal.PtrToStringUni(PointerValue)
            : null;

    public readonly uint? AsUInt() => VarType == VT_UI4 ? UIntValue : null;

    public readonly bool IsEmpty => VarType == VT_EMPTY;
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice? endpoint);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int Item(int index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                               [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
    [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore? properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out DeviceState state);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetAt(int index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
    [PreserveSig] int Commit();
}

[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, DeviceState newState);
    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

internal static class Ole32
{
    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant pvar);
}
