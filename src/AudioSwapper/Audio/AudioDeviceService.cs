using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace AudioSwapper.Audio;

/// <summary>
/// The whole view of Windows audio this app needs: enumerate render endpoints,
/// report which one holds each default role, and move those roles elsewhere.
/// </summary>
internal sealed class AudioDeviceService : IDisposable
{
    private const int StgmRead = 0x0;

    private readonly Dispatcher _dispatcher;
    private readonly DeviceNotificationClient _notificationClient;
    private readonly DispatcherTimer _coalesceTimer;
    private IMMDeviceEnumerator? _enumerator;
    private bool _disposed;

    /// <summary>
    /// Raised on the UI thread after device topology or a default role changed.
    /// Already coalesced, so one physical plug event raises this exactly once.
    /// </summary>
    public event Action? DevicesChanged;

    public AudioDeviceService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        // Windows fires a burst of callbacks per physical event -- plugging in a
        // headset can produce an add, two state changes and three
        // default-changed callbacks. Restarting a short timer on each one
        // collapses the burst into a single refresh.
        _coalesceTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(220),
        };
        _coalesceTimer.Tick += (_, _) =>
        {
            _coalesceTimer.Stop();
            DevicesChanged?.Invoke();
        };

        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        _notificationClient = new DeviceNotificationClient(OnNativeNotification);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    private void OnNativeNotification()
    {
        if (_disposed) return;

        // The callback arrives on an arbitrary MTA thread; hop to the UI thread
        // before touching the timer or raising the event.
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_disposed) return;
            _coalesceTimer.Stop();
            _coalesceTimer.Start();
        });
    }

    /// <summary>
    /// Every render endpoint in any state. Unplugged and disabled devices are
    /// included on purpose, so a device configured last week still shows (greyed
    /// out) while it is disconnected instead of vanishing from the picker.
    /// </summary>
    public IReadOnlyList<AudioDevice> GetRenderDevices()
    {
        if (_enumerator is null) return Array.Empty<AudioDevice>();

        string? defaultId = TryGetDefaultId(ERole.Multimedia);
        string? defaultCommsId = TryGetDefaultId(ERole.Communications);

        int hr = _enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.All, out var collection);
        if (hr < 0 || collection is null) return Array.Empty<AudioDevice>();

        var results = new List<AudioDevice>();
        try
        {
            if (collection.GetCount(out int count) < 0) return results;

            for (int i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) < 0 || device is null) continue;
                try
                {
                    var parsed = ReadDevice(device, defaultId, defaultCommsId);
                    if (parsed is not null) results.Add(parsed);
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }

        // Active devices first, then alphabetical. Keeps the picker stable as
        // things come and go rather than reshuffling on every refresh.
        return results
            .OrderByDescending(d => d.IsActive)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private AudioDevice? ReadDevice(IMMDevice device, string? defaultId, string? defaultCommsId)
    {
        if (device.GetId(out string id) < 0 || string.IsNullOrEmpty(id)) return null;
        if (device.GetState(out var state) < 0) state = DeviceState.NotPresent;

        string name = "", shortName = "", adapter = "";
        var formFactor = EndpointFormFactor.UnknownFormFactor;

        if (device.OpenPropertyStore(StgmRead, out var store) >= 0 && store is not null)
        {
            try
            {
                name = ReadString(store, PropertyKeys.DeviceFriendlyName) ?? "";
                shortName = ReadString(store, PropertyKeys.DeviceDescription) ?? "";
                adapter = ReadString(store, PropertyKeys.InterfaceFriendlyName) ?? "";
                if (ReadUInt(store, PropertyKeys.FormFactor) is { } ff && ff <= 10)
                    formFactor = (EndpointFormFactor)ff;
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }

        if (string.IsNullOrWhiteSpace(name))
            name = string.IsNullOrWhiteSpace(shortName) ? "Unknown device" : shortName;

        return new AudioDevice
        {
            Id = id,
            Name = name,
            ShortName = string.IsNullOrWhiteSpace(shortName) ? name : shortName,
            AdapterName = adapter,
            FormFactor = formFactor,
            State = state,
            IsDefault = id == defaultId,
            IsDefaultCommunications = id == defaultCommsId,
        };
    }

    private static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        var k = key;
        if (store.GetValue(ref k, out var value) < 0) return null;
        try { return value.AsString(); }
        finally { Ole32.PropVariantClear(ref value); }
    }

    private static uint? ReadUInt(IPropertyStore store, PropertyKey key)
    {
        var k = key;
        if (store.GetValue(ref k, out var value) < 0) return null;
        try { return value.AsUInt(); }
        finally { Ole32.PropVariantClear(ref value); }
    }

    /// <summary>The endpoint id currently holding <paramref name="role"/>, if any.</summary>
    public string? TryGetDefaultId(ERole role)
    {
        if (_enumerator is null) return null;

        // Fails with E_NOTFOUND when every output is unplugged. Not an error.
        if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, role, out var endpoint) < 0 || endpoint is null)
            return null;

        try
        {
            return endpoint.GetId(out string id) >= 0 ? id : null;
        }
        finally
        {
            Marshal.ReleaseComObject(endpoint);
        }
    }

    public AudioDevice? GetCurrentDefault()
    {
        string? id = TryGetDefaultId(ERole.Multimedia);
        return id is null ? null : GetRenderDevices().FirstOrDefault(d => d.Id == id);
    }

    /// <summary>
    /// Moves the default roles onto <paramref name="deviceId"/>. Console and
    /// Multimedia always move together, because Windows presents them as one
    /// choice in its own UI. Communications is separate: Teams, Discord and Zoom
    /// follow it, and some people deliberately keep calls on a headset.
    /// </summary>
    public void SetDefault(string deviceId, bool includeCommunications)
    {
        var roles = includeCommunications
            ? new[] { ERole.Console, ERole.Multimedia, ERole.Communications }
            : new[] { ERole.Console, ERole.Multimedia };

        PolicyConfigClient.SetDefaultEndpoint(deviceId, roles);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _coalesceTimer.Stop();

        if (_enumerator is not null)
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            }
            catch (Exception)
            {
                // Shutting down; the callback dies with the process anyway.
            }

            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }
}
