using System.Numerics;

namespace V12.Core.Core.Interfaces
{
    public interface IAudioSource : IComponent
    {
        bool IsPlaying { get; }
        float Volume { get; }
        float Pitch { get; }
        bool Loop { get; }
        string AudioClipPath { get; }
        float MaxDistance { get; }
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

    public interface IAudioPlayer : IGameService
    {
        void Play(IAudioSource source);
        void Stop(IAudioSource source);
        void SetGain(IAudioSource source, float gain);
        void RemoveSource(IAudioSource source);
    }
}
