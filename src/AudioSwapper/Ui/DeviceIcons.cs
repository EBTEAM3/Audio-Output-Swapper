using System;
using System.Collections.Generic;
using System.Linq;
using AudioSwapper.Audio;

namespace AudioSwapper.Ui;

/// <summary>
/// One icon the user can assign to an output device.
/// </summary>
/// <param name="Key">Stable identifier persisted in config.</param>
/// <param name="Label">What the picker calls it.</param>
/// <param name="Paths">
/// Stroke geometry on a 24x24 grid, in SVG path mini-language. Stroked rather
/// than filled because these have to stay legible at 16x16 in the system tray.
/// </param>
internal sealed record DeviceIcon(string Key, string Label, string[] Paths);

/// <summary>
/// The device icon set, and the rules for guessing a sensible one.
///
/// This is deliberately the single source of truth for icon geometry: the tray
/// renderer feeds these strings to WPF's Geometry.Parse, and the web UI gets the
/// same strings as JSON for its &lt;path d="..."&gt; elements. Both surfaces
/// therefore cannot drift apart.
/// </summary>
internal static class DeviceIcons
{
    public const string DefaultKey = "generic";

    public static readonly IReadOnlyList<DeviceIcon> All = new[]
    {
        new DeviceIcon("speakers", "Speakers", new[]
        {
            "M5.6 2.2h12.8a1.8 1.8 0 0 1 1.8 1.8v16a1.8 1.8 0 0 1-1.8 1.8H5.6a1.8 1.8 0 0 1-1.8-1.8V4a1.8 1.8 0 0 1 1.8-1.8z",
            "M12 10.4a3.7 3.7 0 1 0 0 7.4 3.7 3.7 0 0 0 0-7.4z",
            "M12 5.4a1.15 1.15 0 1 0 0 2.3 1.15 1.15 0 0 0 0-2.3z",
        }),

        new DeviceIcon("headphones", "Headphones", new[]
        {
            "M3 14h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a9 9 0 1 1 18 0v7a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3",
        }),

        // Bigger buds and shorter stems than a literal AirPod silhouette: at
        // 16px in the tray, a thin stem disappears and the icon collapses into
        // two anonymous dots.
        new DeviceIcon("earbuds", "Wireless earbuds", new[]
        {
            "M6.8 3.4a4.2 4.2 0 1 0 0 8.4 4.2 4.2 0 0 0 0-8.4z",
            "M6.8 11.8v4a2.6 2.6 0 0 0 2.6 2.6h.8",
            "M17.2 3.4a4.2 4.2 0 1 0 0 8.4 4.2 4.2 0 0 0 0-8.4z",
            "M17.2 11.8v4a2.6 2.6 0 0 1-2.6 2.6h-.8",
        }),

        new DeviceIcon("headset", "Headset", new[]
        {
            "M3 13h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a9 9 0 1 1 18 0v7a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3",
            "M8 19.4h3.2a2.2 2.2 0 0 0 2.2-2.2",
            "M13.4 15.6a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3z",
        }),

        new DeviceIcon("soundbar", "Sound bar", new[]
        {
            "M3 9.2h18a1.8 1.8 0 0 1 1.8 1.8v2a1.8 1.8 0 0 1-1.8 1.8H3A1.8 1.8 0 0 1 1.2 13v-2A1.8 1.8 0 0 1 3 9.2z",
            "M7.6 10.4a1.6 1.6 0 1 0 0 3.2 1.6 1.6 0 0 0 0-3.2z",
            "M16.4 10.4a1.6 1.6 0 1 0 0 3.2 1.6 1.6 0 0 0 0-3.2z",
        }),

        // Lens pushed off-centre with a single control knob beside it. A
        // centred lens flanked by two knobs reads as a washing machine once the
        // glyph drops below about 20px.
        new DeviceIcon("projector", "Projector", new[]
        {
            "M3.4 7.8h17.2a1.8 1.8 0 0 1 1.8 1.8v5a1.8 1.8 0 0 1-1.8 1.8H3.4a1.8 1.8 0 0 1-1.8-1.8v-5a1.8 1.8 0 0 1 1.8-1.8z",
            "M9.4 8.9a3.4 3.4 0 1 0 0 6.8 3.4 3.4 0 0 0 0-6.8z",
            "M17.6 10.9a1.1 1.1 0 1 0 0 2.2 1.1 1.1 0 0 0 0-2.2z",
            "M5.4 16.4v2.2",
            "M18.6 16.4v2.2",
        }),

        new DeviceIcon("tv", "TV", new[]
        {
            "M3 4.4h18a1.8 1.8 0 0 1 1.8 1.8v9.2a1.8 1.8 0 0 1-1.8 1.8H3a1.8 1.8 0 0 1-1.8-1.8V6.2A1.8 1.8 0 0 1 3 4.4z",
            "M8.4 20.4 12 17.2l3.6 3.2",
        }),

        new DeviceIcon("monitor", "Monitor", new[]
        {
            "M3.6 3.4h16.8a1.8 1.8 0 0 1 1.8 1.8v9a1.8 1.8 0 0 1-1.8 1.8H3.6a1.8 1.8 0 0 1-1.8-1.8v-9a1.8 1.8 0 0 1 1.8-1.8z",
            "M12 16v3.2",
            "M8 19.2h8",
        }),

        new DeviceIcon("hdmi", "HDMI / display", new[]
        {
            "M2 4.6h17.6a1.8 1.8 0 0 1 1.8 1.8v11.2a1.8 1.8 0 0 1-1.8 1.8h-4.4",
            "M2 16.2A5 5 0 0 1 5.9 20.1",
            "M2 12.2a9 9 0 0 1 7.9 7.9",
            "M2 8.2a13 13 0 0 1 11.9 11.9",
        }),

        new DeviceIcon("bluetooth", "Bluetooth", new[]
        {
            "M6.6 7.2 17 17.6 12 22.4V1.6l5 5L6.6 17",
        }),

        // A wide plug body rather than an anatomically correct thin one -- the
        // real proportions of a 3.5mm jack vanish at tray size.
        new DeviceIcon("jack", "Line out / 3.5mm", new[]
        {
            "M12 1.9v3.4",
            "M8.4 5.3h7.2v9a3.6 3.6 0 0 1-7.2 0v-9z",
            "M8.4 9h7.2",
            "M8.4 11.6h7.2",
        }),

        new DeviceIcon("generic", "Generic output", new[]
        {
            "M11 5 6 9H2v6h4l5 4V5z",
            "M15.5 8.5a5 5 0 0 1 0 7",
            "M19 5a10 10 0 0 1 0 14",
        }),
    };

    private static readonly Dictionary<string, DeviceIcon> ByKey =
        All.ToDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase);

    public static DeviceIcon Get(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var icon) ? icon : ByKey[DefaultKey];

    /// <summary>
    /// Picks a starting icon for a device we have not seen before.
    ///
    /// Windows reports a form factor per endpoint, which is right often enough
    /// to be worth using, but it is coarse -- almost every USB DAC and HDMI
    /// output claims "Speakers" or "DigitalAudioDisplayDevice". So the name is
    /// checked first for the words people actually use, and the form factor is
    /// the fallback. The user can always override the result.
    /// </summary>
    public static string GuessFor(AudioDevice device)
    {
        string haystack = $"{device.Name} {device.ShortName} {device.AdapterName}".ToLowerInvariant();

        // Ordered most-specific first: "wireless earbuds" must not match on the
        // bare word "wireless", and a "headset" must beat plain "headphones".
        (string Needle, string Key)[] nameHints =
        {
            ("earbud", "earbuds"),
            ("airpod", "earbuds"),
            ("buds", "earbuds"),
            ("galaxy bud", "earbuds"),
            ("headset", "headset"),
            ("headphone", "headphones"),
            ("projector", "projector"),
            ("beamer", "projector"),
            ("soundbar", "soundbar"),
            ("sound bar", "soundbar"),
            ("bluetooth", "bluetooth"),
            ("hands-free", "headset"),
            ("television", "tv"),
            ("tv", "tv"),
            ("monitor", "monitor"),
            ("display", "monitor"),
            ("hdmi", "hdmi"),
            ("displayport", "hdmi"),
            ("speaker", "speakers"),
            ("line out", "jack"),
            ("aux", "jack"),
            ("s/pdif", "jack"),
            ("spdif", "jack"),
            ("realtek", "speakers"),
        };

        foreach (var (needle, key) in nameHints)
        {
            if (haystack.Contains(needle, StringComparison.Ordinal)) return key;
        }

        return device.FormFactor switch
        {
            EndpointFormFactor.Speakers => "speakers",
            EndpointFormFactor.Headphones => "headphones",
            EndpointFormFactor.Headset => "headset",
            EndpointFormFactor.Handset => "headset",
            EndpointFormFactor.LineLevel => "jack",
            EndpointFormFactor.Spdif => "jack",
            EndpointFormFactor.DigitalAudioDisplayDevice => "hdmi",
            EndpointFormFactor.RemoteNetworkDevice => "bluetooth",
            _ => DefaultKey,
        };
    }
}
