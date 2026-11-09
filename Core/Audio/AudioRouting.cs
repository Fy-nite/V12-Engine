namespace V12.Core.Audio;

/// <summary>Where an <c>AudioEffectComponent</c> attaches its effect.</summary>
public enum AudioEffectTarget
{
    /// <summary>The first audio source on the same element.</summary>
    Source,

    /// <summary>Every source routed to the named mixer channel.</summary>
    MixerChannel,
}

/// <summary>
/// A scene-authored mixer channel strip. Sources route to it by name; the audio
/// adapter applies its gain, pan, mute, solo and bus to every source on the channel.
/// </summary>
public interface IAudioMixerChannel
{
    string MixerName { get; }
    string BusName { get; }
    float Gain { get; }
    float Pan { get; }
    bool Muted { get; }
    bool Solo { get; }
}

/// <summary>
/// A scene-authored bus. The audio adapter creates a named bus from it and routes
/// channels to it by name.
/// </summary>
public interface IAudioBusSource
{
    string BusName { get; }
    string ParentBusName { get; }
    float Gain { get; }
    bool Muted { get; }
    bool Solo { get; }
}
