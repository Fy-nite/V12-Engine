using System;
using System.Numerics;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    public class AudioSourceComponent : ComponentBase, IAudioSource
    {
        private string _audioClipPath = string.Empty;
        private float  _volume        = 1f;
        private float  _pitch         = 1f;
        private bool   _loop          = false;
        private bool   _autoplay      = false;
        private float  _maxDistance   = 40f;
        private bool   _isPlaying     = false;
        private bool   _isPaused      = false;

        public override string Name        => "AudioSource";
        public override string Description => "3D positional audio source";

        public string AudioClipPath
        {
            get => _audioClipPath;
            set { if (_audioClipPath != value) { _audioClipPath = value ?? string.Empty; MarkDirty(); } }
        }

        public float Volume
        {
            get => _volume;
            set { if (Math.Abs(_volume - value) > 0.001f) { _volume = Math.Clamp(value, 0f, 1f); MarkDirty(); } }
        }

        public float Pitch
        {
            get => _pitch;
            set { if (Math.Abs(_pitch - value) > 0.001f) { _pitch = MathF.Max(0.01f, value); MarkDirty(); } }
        }

        public bool Loop
        {
            get => _loop;
            set { if (_loop != value) { _loop = value; MarkDirty(); } }
        }

        public bool Autoplay
        {
            get => _autoplay;
            set { if (_autoplay != value) { _autoplay = value; if (_autoplay) _isPlaying = true; MarkDirty(); } }
        }

        public float MaxDistance
        {
            get => _maxDistance;
            set { if (Math.Abs(_maxDistance - value) > 0.001f) { _maxDistance = MathF.Max(0f, value); MarkDirty(); } }
        }

        public bool IsPlaying => _isPlaying;

        public Vector3 Position
        {
            get
            {
                var t = Owner?.GetComponent<TransformComponent>();
                return t != null ? new Vector3(t.X, t.Y, t.Z) : Vector3.Zero;
            }
        }

        public void Play()
        {
            if (!_isPlaying)
            {
                _isPlaying = true;
                _isPaused = false;
                MarkDirty();
            }
        }

        public void Stop()
        {
            if (_isPlaying)
            {
                _isPlaying = false;
                _isPaused = false;
                MarkDirty();
            }
        }

        public void Pause()
        {
            if (_isPlaying && !_isPaused)
            {
                _isPaused = true;
                MarkDirty();
            }
        }

        public AudioSourceComponent() { }

        public AudioSourceComponent(string audioClipPath, float volume = 1f, bool loop = false, bool autoplay = false)
        {
            _audioClipPath = audioClipPath ?? string.Empty;
            _volume = Math.Clamp(volume, 0f, 1f);
            _loop = loop;
            _autoplay = autoplay;
            _isPlaying = autoplay;
        }

        public override string ToString() =>
            $"AudioSource(Clip:{AudioClipPath} Vol:{Volume:F2} Pitch:{Pitch:F2} Loop:{Loop} Auto:{Autoplay})";

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
