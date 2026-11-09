using System;
using V12.Core;
using V12.Core.Audio;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    /// <summary>
    /// Controls the logical game-audio buses from a scene. Put one on a settings or
    /// bootstrap element and edit Music / SFX / UI / Voice levels in the inspector.
    /// </summary>
    [ComponentEntry("AUDIO", "Audio Mixer", SingleInstance = true, SortOrder = 10, Icon = "sliders")]
    public class AudioMixerComponent : ComponentBase
    {
        private float _master = 1f;
        private float _music = 1f;
        private float _sfx = 1f;
        private float _ui = 1f;
        private float _voice = 1f;
        private bool _muteMusic;
        private bool _muteSfx;
        private bool _muteUi;
        private bool _muteVoice;
        private bool _dirty = true;

        public override string Name => "AudioMixer";
        public override string Description => "Controls the logical game-audio bus volumes";

        public float MasterVolume
        {
            get => _master;
            set { if (Math.Abs(_master - value) > 0.001f) { _master = Math.Clamp(value, 0f, 1f); _dirty = true; MarkDirty(); } }
        }

        public float MusicVolume
        {
            get => _music;
            set { if (Math.Abs(_music - value) > 0.001f) { _music = Math.Clamp(value, 0f, 1f); _dirty = true; MarkDirty(); } }
        }

        public float SfxVolume
        {
            get => _sfx;
            set { if (Math.Abs(_sfx - value) > 0.001f) { _sfx = Math.Clamp(value, 0f, 1f); _dirty = true; MarkDirty(); } }
        }

        public float UiVolume
        {
            get => _ui;
            set { if (Math.Abs(_ui - value) > 0.001f) { _ui = Math.Clamp(value, 0f, 1f); _dirty = true; MarkDirty(); } }
        }

        public float VoiceVolume
        {
            get => _voice;
            set { if (Math.Abs(_voice - value) > 0.001f) { _voice = Math.Clamp(value, 0f, 1f); _dirty = true; MarkDirty(); } }
        }

        public bool MuteMusic
        {
            get => _muteMusic;
            set { if (_muteMusic != value) { _muteMusic = value; _dirty = true; MarkDirty(); } }
        }

        public bool MuteSfx
        {
            get => _muteSfx;
            set { if (_muteSfx != value) { _muteSfx = value; _dirty = true; MarkDirty(); } }
        }

        public bool MuteUi
        {
            get => _muteUi;
            set { if (_muteUi != value) { _muteUi = value; _dirty = true; MarkDirty(); } }
        }

        public bool MuteVoice
        {
            get => _muteVoice;
            set { if (_muteVoice != value) { _muteVoice = value; _dirty = true; MarkDirty(); } }
        }

        public override void Update(float deltaTime)
        {
            if (_dirty)
                Apply();
        }

        /// <summary>Pushes the current values to the live audio buses.</summary>
        public void Apply()
        {
            var system = GameRoot.Instance?.Registry.Get<AudioSystem>("AudioSystem");
            if (system is null)
                return;

            system.SetBusVolume(AudioSourceBus.Master, _master);
            system.SetBusVolume(AudioSourceBus.Music, _muteMusic ? 0f : _music);
            system.SetBusVolume(AudioSourceBus.Sfx, _muteSfx ? 0f : _sfx);
            system.SetBusVolume(AudioSourceBus.Ui, _muteUi ? 0f : _ui);
            system.SetBusVolume(AudioSourceBus.Voice, _muteVoice ? 0f : _voice);
            _dirty = false;
        }

        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Audio Mixer");
            inspector.Float("Master", () => MasterVolume, v => MasterVolume = v);
            inspector.Float("Music", () => MusicVolume, v => MusicVolume = v);
            inspector.Bool("Mute Music", () => MuteMusic, v => MuteMusic = v);
            inspector.Float("SFX", () => SfxVolume, v => SfxVolume = v);
            inspector.Bool("Mute SFX", () => MuteSfx, v => MuteSfx = v);
            inspector.Float("UI", () => UiVolume, v => UiVolume = v);
            inspector.Bool("Mute UI", () => MuteUi, v => MuteUi = v);
            inspector.Float("Voice", () => VoiceVolume, v => VoiceVolume = v);
            inspector.Bool("Mute Voice", () => MuteVoice, v => MuteVoice = v);
            inspector.Button("Apply", Apply);
        }

        public override IWorldElement BuildUI() => new Element();
    }
}
