<#
.SYNOPSIS
Lists every audio render endpoint with the state Windows reports for it.

.DESCRIPTION
Diagnostic for "the app says my device is not connected but Windows lets me pick
it". The app decides whether a switch is allowed from DEVICE_STATE, so this
shows exactly what it is seeing.

States: ACTIVE (1), DISABLED (2), NOTPRESENT (4), UNPLUGGED (8).

An endpoint that jack detection considers unoccupied reports UNPLUGGED even
though it is a permanent onboard output that is present and usable. Windows'
own picker happily selects those.
#>

Add-Type -Language CSharp @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AudioList
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class EnumeratorObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr c);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr c);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropKey key);
        [PreserveSig] int GetValue(ref PropKey key, out PropVar value);
        [PreserveSig] int SetValue(ref PropKey key, ref PropVar value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PropKey { public Guid FormatId; public int PropertyId; }

    // 24 bytes on x64 -- the union is as wide as a DECIMAL.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PropVar
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public IntPtr Pointer;
        [FieldOffset(8)] public uint UInt;
    }

    public static class Lister
    {
        static string Str(IPropertyStore store, string guid, int pid)
        {
            PropKey k = new PropKey();
            k.FormatId = new Guid(guid);
            k.PropertyId = pid;
            PropVar v;
            if (store.GetValue(ref k, out v) < 0) return "";
            if (v.VarType != 31 || v.Pointer == IntPtr.Zero) return "";
            return Marshal.PtrToStringUni(v.Pointer);
        }

        static int UInt(IPropertyStore store, string guid, int pid)
        {
            PropKey k = new PropKey();
            k.FormatId = new Guid(guid);
            k.PropertyId = pid;
            PropVar v;
            if (store.GetValue(ref k, out v) < 0) return -1;
            if (v.VarType != 19) return -1;
            return (int)v.UInt;
        }

        public static string[] All()
        {
            IMMDeviceEnumerator e = (IMMDeviceEnumerator)new EnumeratorObject();

            string defId = "", defCommsId = "";
            IMMDevice d0;
            if (e.GetDefaultAudioEndpoint(0, 1, out d0) >= 0 && d0 != null) d0.GetId(out defId);
            if (e.GetDefaultAudioEndpoint(0, 2, out d0) >= 0 && d0 != null) d0.GetId(out defCommsId);

            IMMDeviceCollection collection;
            e.EnumAudioEndpoints(0, 15, out collection);   // render, ALL states

            int count;
            collection.GetCount(out count);

            var rows = new List<string>();
            for (int i = 0; i < count; i++)
            {
                IMMDevice device;
                if (collection.Item(i, out device) < 0) continue;

                string id;
                device.GetId(out id);

                int state;
                device.GetState(out state);

                IPropertyStore store;
                string name = "", ff = "";
                if (device.OpenPropertyStore(0, out store) >= 0 && store != null)
                {
                    name = Str(store, "a45c254e-df1c-4efd-8020-67d146a850e0", 14);
                    int f = UInt(store, "1da5d803-d492-4edd-8c23-e0c0ffee7f0e", 0);
                    ff = f.ToString();
                }

                string role = "";
                if (id == defId) role += "DEFAULT ";
                if (id == defCommsId) role += "COMMS";

                rows.Add(string.Join("", new string[] { state.ToString(), name, ff, role.Trim(), id }));
            }
            return rows.ToArray();
        }
    }
}
'@

$stateNames = @{ 1 = 'ACTIVE'; 2 = 'DISABLED'; 4 = 'NOTPRESENT'; 8 = 'UNPLUGGED' }
$formFactors = @{
    0 = 'RemoteNetwork'; 1 = 'Speakers'; 2 = 'LineLevel'; 3 = 'Headphones'; 4 = 'Microphone'
    5 = 'Headset'; 6 = 'Handset'; 7 = 'DigitalPassthrough'; 8 = 'SPDIF'; 9 = 'DigitalDisplay'
    10 = 'Unknown'
}

# [char]1, not "`u{0001}" -- the `u escape is PowerShell 6+ and silently fails to
# split on Windows PowerShell 5.1.
[AudioList.Lister]::All() | ForEach-Object {
    $p = $_ -split ([char]1)
    [pscustomobject]@{
        State      = if ($stateNames.ContainsKey([int]$p[0])) { $stateNames[[int]$p[0]] } else { $p[0] }
        Role       = $p[3]
        FormFactor = if ($formFactors.ContainsKey([int]$p[2])) { $formFactors[[int]$p[2]] } else { $p[2] }
        Name       = $p[1]
        Id         = $p[4]
    }
} | Sort-Object @{e={ @('ACTIVE','UNPLUGGED','DISABLED','NOTPRESENT').IndexOf($_.State) }}, Name
