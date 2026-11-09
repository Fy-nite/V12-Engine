using System.Numerics;
using V12.Core.Audio;

namespace V12.Core.Core.Interfaces
{
    public interface IAudioSource : IComponent
    {
        bool IsPlaying { get; }
        bool IsPaused { get; }
        float Volume { get; }
        float Pitch { get; }
        bool Loop { get; }
        string AudioClipPath { get; }
        float MinDistance { get; }
        float MaxDistance { get; }
        float Pan { get; }
        float SpatialBlend { get; }
        AudioAttenuationModel Attenuation { get; }
        AudioSourceBus Bus { get; }

        /// <summary>
        /// Optional scene mixer channel name. When set, the channel strip's bus,
        /// gain, pan, mute and solo are applied on top of this source.
        /// </summary>
        string MixerChannel { get; }

        int Priority { get; }
        Vector3 Position { get; }

        void Play();
        void Stop();
        void Pause();
    }

    public interface IAudioListener : IComponent
    {
        Vector3 Position { get; }
        Vector3 Forward { get; }
        Vector3 Up { get; }
    }
}
