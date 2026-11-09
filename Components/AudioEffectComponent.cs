using System;
using System.Collections.Generic;
using V12.Audio;
using V12.Core;
using V12.Core.Audio;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    /// <summary>
    /// Attaches one native effect to a source or to every source routed through a named
    /// mixer channel. Add a second component for a second effect; the chain order follows
    /// the component order in the inspector.
    /// </summary>
    [ComponentEntry("AUDIO", "Audio Effect", SingleInstance = false, SortOrder = 11, Icon = "waveform")]
    public class AudioEffectComponent : ComponentBase
    {
        private readonly Dictionary<IAudioChannel, IAudioEffect> _attached = new();
        private readonly List<IAudioChannel> _channels = new();

        private AudioEffectTarget _target = AudioEffectTarget.Source;
        private string _targetName = string.Empty;
        private AudioEffectKind _kind = AudioEffectKind.Reverb;
        private bool _enabled = true;
        private float _mix = 0.25f;
        private string _parameterName = string.Empty;
        private float _parameterValue;
        private bool _dirty = true;

        public override string Name => "AudioEffect";
        public override string Description => "Adds a native effect to a source or a mixer channel";

        public AudioEffectTarget Target
        {
            get => _target;
            set { if (_target != value) { _target = value; _dirty = true; MarkDirty(); } }
        }

        /// <summary>Mixer channel name when <see cref="Target"/> is MixerChannel.</summary>
        public string TargetName
        {
            get => _targetName;
            set { if (_targetName != value) { _targetName = value ?? string.Empty; _dirty = true; MarkDirty(); } }
        }

        public AudioEffectKind Kind
        {
            get => _kind;
            set { if (_kind != value) { _kind = value; _dirty = true; MarkDirty(); } }
        }

        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled != value) { _enabled = value; _dirty = true; MarkDirty(); } }
        }

        public float Mix
        {
            get => _mix;
            set { if (Math.Abs(_mix - value) > 0.001f) { _mix = Math.Clamp(value, 0f, 1f); _dirty = true; MarkDirty(); } }
        }

        /// <summary>Optional effect parameter name, e.g. "RoomSize" on a reverb.</summary>
        public string ParameterName
        {
            get => _parameterName;
            set { if (_parameterName != value) { _parameterName = value ?? string.Empty; _dirty = true; MarkDirty(); } }
        }

        public float ParameterValue
        {
            get => _parameterValue;
            set { if (Math.Abs(_parameterValue - value) > 0.001f) { _parameterValue = value; _dirty = true; MarkDirty(); } }
        }

        public override void Update(float deltaTime)
        {
            var system = GameRoot.Instance?.Registry.Get<AudioSystem>("AudioSystem");
            if (system is null)
            {
                DetachAll();
                return;
            }

            _channels.Clear();

            if (_target == AudioEffectTarget.Source)
            {
                var source = Owner?.GetComponent<IAudioSource>();
                if (source is not null && system.TryGetChannel(source.Id, out var channel))
                    _channels.Add(channel);
            }
            else
            {
                system.GetChannelsForMixerChannel(_targetName, _channels);
            }

            RemoveStaleChannels();

            foreach (var channel in _channels)
                EnsureEffect(channel);
        }

        private void RemoveStaleChannels()
        {
            if (_attached.Count == 0)
                return;

            List<IAudioChannel>? stale = null;

            foreach (var pair in _attached)
            {
                if (_channels.Contains(pair.Key))
                    continue;

                (stale ??= new List<IAudioChannel>()).Add(pair.Key);
            }

            if (stale is null)
                return;

            foreach (var channel in stale)
                Detach(channel);
        }

        private void EnsureEffect(IAudioChannel channel)
        {
            if (!_attached.TryGetValue(channel, out var effect) ||
                effect.Kind != _kind ||
                !ContainsEffect(channel, effect))
            {
                Detach(channel);
                effect = channel.Effects.Add(_kind);
                _attached[channel] = effect;
                _dirty = true;
                GameRoot.Log?.Debug("[AudioEffect] Attached {Kind} to a channel", _kind);
            }

            if (!_dirty)
                return;

            effect.Enabled = _enabled;
            effect.Mix = _mix;

            if (!string.IsNullOrWhiteSpace(_parameterName))
            {
                try { effect.SetParameter(_parameterName, _parameterValue); }
                catch (AudioEngineException) { /* parameter does not exist on this effect */ }
            }

            _dirty = false;
        }

        private static bool ContainsEffect(IAudioChannel channel, IAudioEffect effect)
        {
            var effects = channel.Effects.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                if (ReferenceEquals(effects[i], effect))
                    return true;
            }

            return false;
        }

        private void Detach(IAudioChannel channel)
        {
            if (!_attached.TryGetValue(channel, out var effect))
                return;

            try { channel.Effects.Remove(effect); }
            catch { /* the channel may already be gone */ }

            _attached.Remove(channel);
        }

        private void DetachAll()
        {
            if (_attached.Count == 0)
                return;

            foreach (var pair in _attached)
            {
                try { pair.Key.Effects.Remove(pair.Value); }
                catch { /* the channel may already be gone */ }
            }

            _attached.Clear();
        }

        public override void OnDetach(IWorldElement worldElement)
        {
            DetachAll();
            base.OnDetach(worldElement);
        }

        public override void OnDestroy()
        {
            DetachAll();
            base.OnDestroy();
        }

        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Audio Effect");
            inspector.Enum<AudioEffectTarget>("Target", () => Target, v => Target = v);

            if (Target == AudioEffectTarget.MixerChannel)
                inspector.String("Mixer Channel", () => TargetName, v => TargetName = v);
            else if (Owner?.GetComponent<IAudioSource>() is null)
                inspector.Warning("Add an AudioSourceComponent to this element first.");

            inspector.Enum<AudioEffectKind>("Effect", () => Kind, v => Kind = v);
            inspector.Bool("Enabled", () => Enabled, v => Enabled = v);
            inspector.Float("Mix", () => Mix, v => Mix = v);
            inspector.String("Parameter", () => ParameterName, v => ParameterName = v);
            inspector.Float("Value", () => ParameterValue, v => ParameterValue = v);
            inspector.Button("Apply", () => { _dirty = true; });
        }

        public override IWorldElement BuildUI() => new Element();
    }
}
