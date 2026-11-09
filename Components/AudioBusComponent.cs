using System;
using V12.Core;
using V12.Core.Audio;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    /// <summary>
    /// Scene-authored audio bus. Channels route to it by name, and the bus can parent
    /// another bus. Gain, mute and solo are real; summed-bus effects are not available
    /// in the first OwnAudioSharp backend, so put effects on a mixer channel instead.
    /// </summary>
    [ComponentEntry("AUDIO", "Audio Bus", SingleInstance = false, SortOrder = 13, Icon = "git-branch")]
    public class AudioBusComponent : ComponentBase, IAudioBusSource
    {
        private string _busName = "Custom";
        private string _parentBusName = "Master";
        private float _gain = 1f;
        private bool _muted;
        private bool _solo;
        private string? _registeredName;

        public override string Name => "AudioBus";
        public override string Description => "Named audio bus with gain, mute, solo and an optional parent bus";

        public string BusName
        {
            get => _busName;
            set { if (_busName != value) { _busName = value ?? string.Empty; MarkDirty(); } }
        }

        public string ParentBusName
        {
            get => _parentBusName;
            set { if (_parentBusName != value) { _parentBusName = value ?? string.Empty; MarkDirty(); } }
        }

        public float Gain
        {
            get => _gain;
            set { if (Math.Abs(_gain - value) > 0.001f) { _gain = Math.Clamp(value, 0f, 1f); MarkDirty(); } }
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
            if (!string.Equals(_registeredName, BusName, StringComparison.OrdinalIgnoreCase))
                Register();

            Apply();
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
                system.UnregisterBus(_registeredName!);

            system.RegisterBus(this);
            _registeredName = BusName;
        }

        private void Unregister()
        {
            var system = GameRoot.Instance?.Registry.Get<AudioSystem>("AudioSystem");
            if (system is not null && !string.IsNullOrWhiteSpace(_registeredName))
                system.UnregisterBus(_registeredName!);

            _registeredName = null;
        }

        private void Apply()
        {
            var system = GameRoot.Instance?.Registry.Get<AudioSystem>("AudioSystem");
            if (system is null)
                return;

            var bus = system.GetBus(BusName);
            if (bus is null)
                return;

            var parent = system.GetBus(ParentBusName);
            if (!ReferenceEquals(bus.Parent, parent))
                bus.Parent = parent;

            bus.Gain = Gain;
            bus.Muted = Muted;
            bus.Solo = Solo;
        }

        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Audio Bus");
            inspector.HelpBox("Channels route here by bus name. Summed-bus effects are not available yet; put effects on a mixer channel.");
            inspector.String("Bus Name", () => BusName, v => BusName = v);
            inspector.String("Parent Bus", () => ParentBusName, v => ParentBusName = v);
            inspector.Float("Gain", () => Gain, v => Gain = v);
            inspector.Bool("Muted", () => Muted, v => Muted = v);
            inspector.Bool("Solo", () => Solo, v => Solo = v);
            inspector.Button("Apply", Apply);
        }

        public override IWorldElement BuildUI() => new Element();
    }
}
