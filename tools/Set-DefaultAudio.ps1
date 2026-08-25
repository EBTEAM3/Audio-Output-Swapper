<#
.SYNOPSIS
Sets the default audio render endpoint for one role.

Deliberately per-role, unlike the app itself, which moves Console and Multimedia
together. Used to put a machine back exactly as it was after testing, where the
Communications role was pointed at a different device on purpose.

.EXAMPLE
.\Set-DefaultAudio.ps1 -NameLike 'Razer' -Role Communications
#>
param(
    [Parameter(Mandatory = $true)][string]$NameLike,
    [ValidateSet('Console', 'Multimedia', 'Communications', 'All')]
    [string]$Role = 'All'
)

Add-Type -Language CSharp @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AudioSet
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class EnumeratorObject { }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    public class PolicyConfigObject { }

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

    // Declaration order is the vtable order; SetDefaultEndpoint must stay slot 11.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr a, int b);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PropKey { public Guid FormatId; public int PropertyId; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PropVar
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    public static class Setter
    {
        public static string[] FindActive(string nameLike)
        {
            IMMDeviceEnumerator e = (IMMDeviceEnumerator)new EnumeratorObject();
            IMMDeviceCollection collection;
            e.EnumAudioEndpoints(0, 1, out collection);   // render, active only

            int count;
            collection.GetCount(out count);

            var hits = new List<string>();
            for (int i = 0; i < count; i++)
            {
                IMMDevice device;
                if (collection.Item(i, out device) < 0) continue;

                string id;
                device.GetId(out id);

                IPropertyStore store;
                if (device.OpenPropertyStore(0, out store) < 0) continue;

                PropKey key = new PropKey();
                key.FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
                key.PropertyId = 14;

                PropVar value;
                if (store.GetValue(ref key, out value) < 0) continue;
                if (value.VarType != 31 || value.Pointer == IntPtr.Zero) continue;

                string name = Marshal.PtrToStringUni(value.Pointer);
                if (name != null && name.IndexOf(nameLike, StringComparison.OrdinalIgnoreCase) >= 0)
                    hits.Add(id + "|" + name);
            }
            return hits.ToArray();
        }

        public static int Set(string id, int role)
        {
            IPolicyConfig config = (IPolicyConfig)new PolicyConfigObject();
            return config.SetDefaultEndpoint(id, role);
        }
    }
}
'@

$matches = [AudioSet.Setter]::FindActive($NameLike)
if ($matches.Count -eq 0) { Write-Error "No active render device matching '$NameLike'"; exit 1 }
if ($matches.Count -gt 1) {
    Write-Warning "Multiple matches; using the first:"
    $matches | ForEach-Object { "  " + $_.Split('|')[1] }
}

$id, $name = $matches[0].Split('|')
$roles = if ($Role -eq 'All') { @(0, 1, 2) } else { @(@{Console = 0; Multimedia = 1; Communications = 2 }[$Role]) }

foreach ($r in $roles) {
    $hr = [AudioSet.Setter]::Set($id, $r)
    "role {0} -> {1}  (hr=0x{2:X8})" -f $r, $name, $hr
}
