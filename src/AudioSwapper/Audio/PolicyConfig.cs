using System;
using System.Runtime.InteropServices;

namespace AudioSwapper.Audio;

// ---------------------------------------------------------------------------
// IPolicyConfig -- the undocumented interface behind "Set as Default Device".
//
// Windows exposes no public API for changing the default audio endpoint. Every
// tool that does it (SoundSwitch, EarTrumpet, AudioSwitcher, nircmd) calls this
// interface on the PolicyConfigClient coclass. It is not in any SDK header, so
// the vtable layout below is transcribed from the reverse-engineered ABI and
// THE DECLARATION ORDER IS LOAD-BEARING: .NET assigns vtable slots by order of
// declaration, so every preceding method must be declared even though we never
// call it. Deleting one silently shifts SetDefaultEndpoint onto the wrong slot.
//
// The placeholder methods use IntPtr for every argument on purpose -- we only
// need the slot to exist and to be the right width, not to be callable.
// ---------------------------------------------------------------------------

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClientComObject
{
}

/// <summary>Windows 7 and later. Note SetDefaultEndpoint is vtable slot 11.</summary>
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
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
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility(IntPtr a, int b);
}

/// <summary>
/// Vista's variant. Same coclass, different IID, and one fewer method before
/// SetDefaultEndpoint (no ResetDeviceFormat) which puts it on slot 10. Kept as
/// a fallback in case a future Windows build drops the Win7 IID.
/// </summary>
[ComImport, Guid("568B9108-44BF-40B4-9006-86AFE5B5A620"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigVista
{
    [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
    [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
    [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
    [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
    [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
    [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
    [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility(IntPtr a, int b);
}

internal static class PolicyConfigClient
{
    /// <summary>
    /// Promotes <paramref name="deviceId"/> to the default endpoint for the
    /// given roles. Throws if neither interface variant is available.
    /// </summary>
    public static void SetDefaultEndpoint(string deviceId, params ERole[] roles)
    {
        object? raw = null;
        try
        {
            raw = Activator.CreateInstance(typeof(PolicyConfigClientComObject))
                  ?? throw new InvalidOperationException("PolicyConfigClient could not be created.");

            if (raw is IPolicyConfig modern)
            {
                foreach (var role in roles)
                {
                    int hr = modern.SetDefaultEndpoint(deviceId, role);
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                }
                return;
            }

            if (raw is IPolicyConfigVista legacy)
            {
                foreach (var role in roles)
                {
                    int hr = legacy.SetDefaultEndpoint(deviceId, role);
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                }
                return;
            }

            throw new NotSupportedException(
                "This build of Windows does not expose IPolicyConfig; the default " +
                "output device cannot be changed programmatically.");
        }
        finally
        {
            if (raw is not null && Marshal.IsComObject(raw))
                Marshal.ReleaseComObject(raw);
        }
    }
}
