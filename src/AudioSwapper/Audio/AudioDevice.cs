namespace AudioSwapper.Audio;

/// <summary>
/// A snapshot of one audio render endpoint. Immutable -- the service hands out
/// fresh instances whenever Windows tells us something changed, rather than
/// mutating in place, so the UI can diff cheaply.
/// </summary>
internal sealed record AudioDevice
{
    /// <summary>
    /// The MMDevice endpoint id, e.g. "{0.0.0.00000000}.{9a...}". Stable across
    /// reboots and unplug cycles, so this is what we persist in config.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>"Speakers (Realtek(R) Audio)" -- what Windows shows the user.</summary>
    public required string Name { get; init; }

    /// <summary>"Speakers" -- the endpoint alone, used when the full name is too long.</summary>
    public string ShortName { get; init; } = "";

    /// <summary>"Realtek High Definition Audio" -- the adapter behind the endpoint.</summary>
    public string AdapterName { get; init; } = "";

    public EndpointFormFactor FormFactor { get; init; } = EndpointFormFactor.UnknownFormFactor;

    public DeviceState State { get; init; } = DeviceState.NotPresent;

    public bool IsActive => State == DeviceState.Active;

    /// <summary>True when this endpoint currently holds the eMultimedia role.</summary>
    public bool IsDefault { get; init; }

    /// <summary>True when this endpoint currently holds the eCommunications role.</summary>
    public bool IsDefaultCommunications { get; init; }
}
