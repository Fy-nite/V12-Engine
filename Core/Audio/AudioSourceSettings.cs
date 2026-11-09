namespace V12.Core.Audio;

/// <summary>
/// Logical game-audio group. The engine maps these to real buses so a settings menu can
/// change music, effects, UI and voice independently.
/// </summary>
public enum AudioSourceBus
{
    Master,
    Music,
    Sfx,
    Ui,
    Voice,
}

/// <summary>Distance attenuation curve used by the game audio adapter.</summary>
public enum AudioAttenuationModel
{
    None,
    Linear,
    InverseDistance,
    Exponential,
}
