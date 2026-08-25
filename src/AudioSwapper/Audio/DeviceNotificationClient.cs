using System;

namespace AudioSwapper.Audio;

/// <summary>
/// Bridges IMMNotificationClient callbacks to a single .NET event.
///
/// Windows raises these on an arbitrary MTA thread, and fires several in quick
/// succession for one physical event (plugging in a headset can produce an add,
/// two state changes and three default-changed callbacks). Coalescing and
/// thread marshalling are the caller's job -- see AudioDeviceService.
/// </summary>
internal sealed class DeviceNotificationClient : IMMNotificationClient
{
    private readonly Action _onChanged;

    public DeviceNotificationClient(Action onChanged) => _onChanged = onChanged;

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _onChanged();
    public void OnDeviceAdded(string deviceId) => _onChanged();
    public void OnDeviceRemoved(string deviceId) => _onChanged();
    public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId) => _onChanged();
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) => _onChanged();
}
