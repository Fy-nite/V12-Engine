using System;
using V12.Core;
using V12.Core.Audio;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    /// <summary>
    /// A scene-authored mixer strip. Sources route to it by setting their
    /// <c>MixerChannel</c> name. Gain, pan, mute and solo are applied to every source
    /// on the channel, and the channel routes those sources to its named bus.
    ///
    /// OwnAudioSharp exposes one native track per source, not a native submix, so a
    /// mixer channel is a logical strip. Effects that target the channel are applied to
    /// each source channel on it.
    /// </summary>
    [ComponentEntry("AUDIO", "Audio Mixer Channel", SingleInstance = false, SortOrder = 14, Icon = "sliders-horizontal")]
    public class AudioChannelComponent : ComponentBase, IAudioMixerChannel
    {
        private string _mixerName = "Channel";
        private string _busName = "Sfx";
        private float _gain = 1f;
        private float _pan;
        private bool _muted;
        private bool _solo;
        private string? _registeredName;

        public override string Name => "AudioMixerChannel";
        public override string Description => "Routes sources to a bus and applies gain, pan, mute and solo to them";

        public string MixerName
        {
            get => _mixerName;
            set { if (_mixerName != value) { _mixerName = value ?? string.Empty; MarkDirty(); } }
        }

        public string BusName
        {
            get => _busName;
            set { if (_busName != value) { _busName = value ?? string.Empty; MarkDirty(); } }
        }

        public float Gain
        {
            get => _gain;
            set { if (Math.Abs(_gain - value) > 0.001f) { _gain = Math.Clamp(value, 0f, 1f); MarkDirty(); } }
        }

        public float Pan
        {
            get => _pan;
            set { if (Math.Abs(_pan - value) > 0.001f) { _pan = Math.Clamp(value, -1f, 1f); MarkDirty(); } }
        }

        public bool Muted
        {
            get => _muted;
            set { if (_muted != value) { _muted = value; MarkDirty(); } }
        }

        public bool Solo
        {
            get => _solo;
            set { if (_solo != value) { _solo = value; MarkDirty(); } }
        }

        public override void OnAttach(IWorldElement worldElement)
        {
            base.OnAttach(worldElement);
            Register();
        }

        public override void Update(float deltaTime)
        {
            if (!string.Equals(_registeredName, MixerName, StringComparison.OrdinalIgnoreCase))
                Register();
        }

        public override void OnDetach(IWorldElement worldElement)
        {
            Unregister();
            base.OnDetach(worldElement);
        }

        public override void OnDestroy()
        {
            Unregister();
            base.OnDestroy();
        }

        private void Register()
        {
            var system = GameRoot.Instance?.Registry.Get<AudioSystem>("AudioSystem");
            if (system is null)
                return;

            if (!string.IsNullOrWhiteSpace(_registeredName))
                system.UnregisterMixerChannel(_registeredName!);

            system.RegisterMixerChannel(this);
            _registeredName = MixerName;
        }

        private void Unregister()
        {
            var system = GameRoot.Instance?.Registry.Get<AudioSystem>("AudioSystem");
            if (system is not null && !string.IsNullOrWhiteSpace(_registeredName))
                system.UnregisterMixerChannel(_registeredName!);

            _registeredName = null;
        }

        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Audio Mixer Channel");
            inspector.HelpBox("Route sources here with their Mixer Channel field. Add AudioEffectComponent with Target = MixerChannel to apply effects to this strip.");
            inspector.String("Mixer Name", () => MixerName, v => MixerName = v);
            inspector.String("Bus", () => BusName, v => BusName = v);
            inspector.Float("Gain", () => Gain, v => Gain = v);
            inspector.Float("Pan", () => Pan, v => Pan = v);
            inspector.Bool("Muted", () => Muted, v => Muted = v);
            inspector.Bool("Solo", () => Solo, v => Solo = v);
            inspector.Button("Apply", Register);
        }

        public override IWorldElement BuildUI() => new Element();
    }
}
