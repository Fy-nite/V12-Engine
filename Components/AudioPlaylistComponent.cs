using System;
using System.Collections.Generic;
using System.Numerics;
using V12.Core;
using V12.Core.Audio;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    /// <summary>
    /// Plays a list of clips in order. Each clip plays once; the playlist advances when
    /// the channel ends. Optional shuffle and playlist looping. Useful for music,
    /// dialogue sequences, or ambient rotations.
    /// </summary>
    [ComponentEntry("AUDIO", "Audio Playlist", SingleInstance = false, SortOrder = 12, Icon = "list-music")]
    public class AudioPlaylistComponent : ComponentBase, IAudioSource, IAudioPlaybackListener
    {
        private List<string> _clips = new();
        private bool _playlistLoop = true;
        private bool _shuffle;
        private bool _autoplay;
        private bool _autoplayStarted;
        private int _currentIndex;
        private int _playedInPass;
        private bool _isPlaying;
        private bool _isPaused;
        private float _volume = 1f;
        private float _pitch = 1f;
        private float _minDistance = 1f;
        private float _maxDistance = 40f;
        private float _pan;
        private float _spatialBlend = 1f;
        private AudioAttenuationModel _attenuation = AudioAttenuationModel.InverseDistance;
        private AudioSourceBus _bus = AudioSourceBus.Music;
        private string _mixerChannel = string.Empty;
        private int _priority;
        private readonly Random _random = new();

        public override string Name => "AudioPlaylist";
        public override string Description => "Plays a list of clips in order, with optional shuffle and loop";

        public List<string> Clips
        {
            get => _clips;
            set
            {
                _clips = value ?? new List<string>();
                _currentIndex = Math.Clamp(_currentIndex, 0, Math.Max(0, _clips.Count - 1));
                _playedInPass = 0;
                MarkDirty();
            }
        }

        public bool PlaylistLoop
        {
            get => _playlistLoop;
            set { if (_playlistLoop != value) { _playlistLoop = value; MarkDirty(); } }
        }

        public bool Shuffle
        {
            get => _shuffle;
            set { if (_shuffle != value) { _shuffle = value; MarkDirty(); } }
        }

        public bool Autoplay
        {
            get => _autoplay;
            set { if (_autoplay != value) { _autoplay = value; _autoplayStarted = false; MarkDirty(); } }
        }

        public string AudioClipPath => _clips.Count == 0
            ? string.Empty
            : _clips[Math.Clamp(_currentIndex, 0, _clips.Count - 1)];

        public bool IsPlaying => _isPlaying;
        public bool IsPaused => _isPaused;

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

        public bool Loop => false;

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
            if (_clips.Count == 0)
                return;

            if (!_isPlaying && !_isPaused)
                _playedInPass = 0;

            if (_currentIndex < 0 || _currentIndex >= _clips.Count)
                _currentIndex = 0;

            _isPlaying = true;
            _isPaused = false;
            MarkDirty();
        }

        public void Pause()
        {
            if (_isPlaying && !_isPaused)
            {
                _isPaused = true;
                MarkDirty();
            }
        }

        public void Stop()
        {
            if (_isPlaying || _isPaused)
            {
                _isPlaying = false;
                _isPaused = false;
                _currentIndex = 0;
                _playedInPass = 0;
                MarkDirty();
            }
        }

        public void Next()
        {
            if (_clips.Count == 0)
                return;

            _currentIndex = NextIndex();
            _isPlaying = true;
            _isPaused = false;
            MarkDirty();
        }

        public void Previous()
        {
            if (_clips.Count == 0)
                return;

            _currentIndex = _shuffle
                ? _random.Next(_clips.Count)
                : (_currentIndex - 1 + _clips.Count) % _clips.Count;

            _isPlaying = true;
            _isPaused = false;
            MarkDirty();
        }

        public void OnAudioEnded()
        {
            if (_clips.Count == 0)
            {
                _isPlaying = false;
                return;
            }

            _playedInPass++;

            if (!_playlistLoop && _playedInPass >= _clips.Count)
            {
                _isPlaying = false;
                _isPaused = false;
                MarkDirty();
                return;
            }

            _currentIndex = NextIndex();
            _isPlaying = true;
            _isPaused = false;
            MarkDirty();
        }

        private int NextIndex()
        {
            if (_clips.Count <= 1)
                return 0;

            if (_shuffle)
            {
                int next;
                do { next = _random.Next(_clips.Count); }
                while (next == _currentIndex);
                return next;
            }

            return (_currentIndex + 1) % _clips.Count;
        }

        public override void Update(float deltaTime)
        {
            if (_autoplay && !_autoplayStarted)
            {
                _autoplayStarted = true;
                Play();
            }
        }

        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Audio Playlist");
            inspector.List<string>("Clips", () => Clips, v => Clips = v);
            inspector.Bool("Playlist Loop", () => PlaylistLoop, v => PlaylistLoop = v);
            inspector.Bool("Shuffle", () => Shuffle, v => Shuffle = v);
            inspector.Bool("Autoplay", () => Autoplay, v => Autoplay = v);
            inspector.ReadOnly("Current", AudioClipPath);
            inspector.Float("Volume", () => Volume, v => Volume = v);
            inspector.Float("Pitch", () => Pitch, v => Pitch = v);
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
            inspector.Button("Previous", Previous);
            inspector.Button("Next", Next);
        }

        public override IWorldElement BuildUI() => new Element();
    }
}
