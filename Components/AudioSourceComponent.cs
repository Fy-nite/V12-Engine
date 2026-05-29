using System;
using V12.Core.Core.Interfaces;
using V12.Core;
namespace V12.Components
{
    /// <summary>
    /// 3D positional audio source.
    /// ResourcePath is a Godot res:// path to an AudioStream asset.
    /// Volume is in decibels (0 = full volume, negative = quieter).
    /// MaxDistance is the world-unit distance at which the audio becomes inaudible.
    /// </summary>
    public class AudioSourceComponent : ComponentBase
    {
        private string _resourcePath = string.Empty;
        private float  _volume       = 0f;   // dB
        private float  _pitch        = 1f;
        private bool   _loop         = false;
        private bool   _autoplay     = false;
        private float  _maxDistance  = 40f;

        public override string Name        => "AudioSource";
        public override string Description => "3D positional audio source";

        /// <summary>Godot res:// path to the AudioStream resource.</summary>
        public string ResourcePath
        {
            get => _resourcePath;
            set { if (_resourcePath != value) { _resourcePath = value ?? string.Empty; MarkDirty(); } }
        }
        /// <summary>Volume in decibels. 0 dB = full, negative = quieter.</summary>
        public float Volume
        {
            get => _volume;
            set { if (Math.Abs(_volume - value) > 0.001f) { _volume = value; MarkDirty(); } }
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
            set { if (_autoplay != value) { _autoplay = value; MarkDirty(); } }
        }
        public float MaxDistance
        {
            get => _maxDistance;
            set { if (Math.Abs(_maxDistance - value) > 0.001f) { _maxDistance = MathF.Max(0f, value); MarkDirty(); } }
        }

        public AudioSourceComponent() { }
        public AudioSourceComponent(string resourcePath, float volume = 0f, bool loop = false, bool autoplay = false)
        {
            _resourcePath = resourcePath; _volume = volume; _loop = loop; _autoplay = autoplay;
        }

        public override string ToString() =>
            $"AudioSource(Path:{ResourcePath} Vol:{Volume:F1}dB Pitch:{Pitch:F2} Loop:{Loop} Auto:{Autoplay})";
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
