using System;
using System.Numerics;
using V12.Core;
using V12.Core.Audio;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    [ComponentEntry("AUDIO", "Audio Source", SingleInstance = false, SortOrder = 1, Icon = "volume-high")]
    public class AudioSourceComponent : ComponentBase, IAudioSource
    {
        private string _audioClipPath = string.Empty;
        private float  _volume        = 1f;
        private float  _pitch         = 1f;
        private bool   _loop          = false;
        private bool   _autoplay      = false;
        private float  _minDistance   = 1f;
        private float  _maxDistance   = 40f;
        private float  _pan           = 0f;
        private float  _spatialBlend  = 1f;
        private bool   _isPlaying     = false;
        private bool   _isPaused      = false;
        private AudioAttenuationModel _attenuation = AudioAttenuationModel.InverseDistance;
        private AudioSourceBus _bus = AudioSourceBus.Sfx;
        private string _mixerChannel = string.Empty;
        private int _priority = 0;

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

        public float MinDistance
        {
            get => _minDistance;
            set { if (Math.Abs(_minDistance - value) > 0.001f) { _minDistance = MathF.Max(0f, value); MarkDirty(); } }
        }

        public float MaxDistance
        {
            get => _maxDistance;
            set { if (Math.Abs(_maxDistance - value) > 0.001f) { _maxDistance = MathF.Max(_minDistance, value); MarkDirty(); } }
        }

        public float Pan
        {
            get => _pan;
            set { if (Math.Abs(_pan - value) > 0.001f) { _pan = Math.Clamp(value, -1f, 1f); MarkDirty(); } }
        }

        public float SpatialBlend
        {
            get => _spatialBlend;
            set { if (Math.Abs(_spatialBlend - value) > 0.001f) { _spatialBlend = Math.Clamp(value, 0f, 1f); MarkDirty(); } }
        }

        public AudioAttenuationModel Attenuation
        {
            get => _attenuation;
            set { if (_attenuation != value) { _attenuation = value; MarkDirty(); } }
        }

        public AudioSourceBus Bus
        {
            get => _bus;
            set { if (_bus != value) { _bus = value; MarkDirty(); } }
        }

        /// <summary>Optional scene mixer channel name. Overrides Bus when set.</summary>
        public string MixerChannel
        {
            get => _mixerChannel;
            set { if (_mixerChannel != value) { _mixerChannel = value ?? string.Empty; MarkDirty(); } }
        }

        public int Priority
        {
            get => _priority;
            set { if (_priority != value) { _priority = value; MarkDirty(); } }
        }

        public bool IsPlaying => _isPlaying;
        public bool IsPaused => _isPaused;

        public Vector3 Position
        {
            get
            {
                if (Owner == null) return Vector3.Zero;
                var m = Owner.WorldTransform;
                return new Vector3(m.M41, m.M42, m.M43);
            }
        }

        public void Play()
        {
            if (!_isPlaying || _isPaused)
            {
                _isPlaying = true;
                _isPaused = false;
                MarkDirty();
            }
        }

        public void Stop()
        {
            if (_isPlaying || _isPaused)
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

        /// <summary>Generate editable audio source fields for the inspector.</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Audio Source");
            inspector.AssetPath("Clip Path", () => AudioClipPath, v => AudioClipPath = v, new[] { "wav", "mp3", "flac", "ogg", "aiff", "m4a", "aac" });
            inspector.Float("Volume", () => Volume, v => Volume = v);
            inspector.Float("Pitch", () => Pitch, v => Pitch = v);
            inspector.Bool("Loop", () => Loop, v => Loop = v);
            inspector.Bool("Autoplay", () => Autoplay, v => Autoplay = v);
            inspector.Float("Min Distance", () => MinDistance, v => MinDistance = v);
            inspector.Float("Max Distance", () => MaxDistance, v => MaxDistance = v);
            inspector.Float("Pan", () => Pan, v => Pan = v);
            inspector.Float("Spatial Blend", () => SpatialBlend, v => SpatialBlend = v);
            inspector.Enum<AudioAttenuationModel>("Attenuation", () => Attenuation, v => Attenuation = v);
            inspector.Enum<AudioSourceBus>("Bus", () => Bus, v => Bus = v);
            inspector.String("Mixer Channel", () => MixerChannel, v => MixerChannel = v);
            inspector.Int("Priority", () => Priority, v => Priority = v);
            inspector.Button("Play", Play);
            inspector.Button("Pause", Pause);
            inspector.Button("Stop", Stop);
        }

        public override string ToString() =>
            $"AudioSource(Clip:{AudioClipPath} Vol:{Volume:F2} Pitch:{Pitch:F2} Loop:{Loop} Auto:{Autoplay})";

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
