<#
.SYNOPSIS
Prints the current default audio render endpoint for each role.

Written against the public MMDevice API only, independently of the app's own
interop, so it can be trusted as a check on whether a switch really happened
rather than just echoing the app's opinion of itself.
#>

Add-Type -Language CSharp @'
using System;
using System.Runtime.InteropServices;

namespace AudioProbe
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumeratorObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropKey key);
        [PreserveSig] int GetValue(ref PropKey key, out PropVar value);
        [PreserveSig] int SetValue(ref PropKey key, ref PropVar value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PropKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    // 24 bytes on x64 -- the union is as wide as a DECIMAL.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PropVar
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public IntPtr Pointer;
        [FieldOffset(8)] public uint UInt;
    }

    public static class Probe
    {
        public static string Describe(int role)
        {
            IMMDeviceEnumerator e = (IMMDeviceEnumerator)new MMDeviceEnumeratorObject();
            IMMDevice device;
            if (e.GetDefaultAudioEndpoint(0, role, out device) < 0 || device == null)
                return "(none)";

            IPropertyStore store;
            if (device.OpenPropertyStore(0, out store) < 0 || store == null)
                return "(no property store)";

            PropKey key = new PropKey();
            key.FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
            key.PropertyId = 14;

            PropVar value;
            if (store.GetValue(ref key, out value) < 0) return "(no name)";
            if (value.VarType != 31 || value.Pointer == IntPtr.Zero) return "(vt=" + value.VarType + ")";

            return Marshal.PtrToStringUni(value.Pointer);
        }
    }
}
'@

[pscustomobject]@{
    Console        = [AudioProbe.Probe]::Describe(0)
    Multimedia     = [AudioProbe.Probe]::Describe(1)
    Communications = [AudioProbe.Probe]::Describe(2)
}
