using System;
using System.Numerics;
using V12.Core.Input;
using V12.Core.Interfaces.Renderer;

namespace V12.Core.Introspection
{
    /// <summary>Operations the introspection command queue can apply to the live tree.</summary>
    public enum SceneOp
    {
        Spawn,
        Despawn,
        Reparent,
        SetTransform,
        SetActive,
        SetProperty,
        AttachComponent,
        DetachComponent,
        SendInput,
        Pause,
        SetTimeScale,
    }

    /// <summary>Synthetic input carried by a <see cref="SceneOp.SendInput"/> command.</summary>
    public sealed class InputCommand
    {
        public InputEventType Type { get; set; } = InputEventType.ButtonDown;
        public string? Name { get; set; }
        public InputKey? Key { get; set; }
        public GamepadButton? PadButton { get; set; }
        public GamepadAxis? PadAxis { get; set; }
        public int Device { get; set; }
        public float Value { get; set; }
        public Vector2 Pointer { get; set; }
        public PointerButton Button { get; set; }
        public char Character { get; set; }
        public bool IsPressed { get; set; } = true;

        public InputEvent ToEvent(string requestId) => new()
        {
            Type = Type,
            Name = Name ?? string.Empty,
            Key = Key,
            PadButton = PadButton,
            PadAxis = PadAxis,
            Device = Device,
            Value = Value,
            Pointer = Pointer,
            Button = Button,
            Character = Character,
            IsPressed = IsPressed,
            // Tagged so handlers can ignore synthetic input from tooling without
            // needing a new InputEvent field.
            Meta = new()
            {
                ["source"] = "introspection",
                ["command"] = requestId,
            },
        };
    }

    /// <summary>
    /// One mutation request. Queued from any thread and applied by
    /// <see cref="SceneIntrospection.DrainCommands"/> on the tick thread, because
    /// the world tree is only safe to mutate from the loop that owns it.
    /// </summary>
    public sealed class SceneCommand
    {
        /// <summary>Caller-chosen id. Retries with the same id return the original
        /// receipt instead of applying twice.</summary>
        public string RequestId { get; set; } = "";
        public SceneOp Op { get; set; }
        /// <summary>Target world. Empty searches every loaded world for <see cref="TargetId"/>.</summary>
        public Guid WorldId { get; set; }
        public long TargetId { get; set; }
        /// <summary>Component instance id, when the op targets one. Prefer over
        /// <see cref="ComponentType"/> when an element has several of a type.</summary>
        public long ComponentId { get; set; }
        /// <summary>Simple or qualified component type name.</summary>
        public string? ComponentType { get; set; }
        /// <summary>Property name for <see cref="SceneOp.SetProperty"/>.</summary>
        public string? Property { get; set; }
        public SnapshotValue Value { get; set; }
        /// <summary>Required by <see cref="SceneOp.SetTransform"/> and optional on
        /// <see cref="SceneOp.Spawn"/>. Nullable on purpose: a
        /// <c>default(TRS)</c> has a zero scale and would collapse a spawned element.</summary>
        public TRS? Transform { get; set; }
        /// <summary>Destination parent for <see cref="SceneOp.Spawn"/> or
        /// <see cref="SceneOp.Reparent"/>. 0 means the world root.</summary>
        public long ParentId { get; set; }
        public string? Name { get; set; }
        public InputCommand? Input { get; set; }
        /// <summary>Apply no earlier than this tick. 0 applies on the next drain.</summary>
        public long ApplyAtTick { get; set; }
        /// <summary>Free-form attribution for the journal (agent, user, tool).</summary>
        public string? Actor { get; set; }
    }

    /// <summary>Outcome of one <see cref="SceneCommand"/>.</summary>
    public sealed class CommandReceipt
    {
        public string RequestId { get; set; } = "";
        public SceneOp Op { get; set; }
        public bool Accepted { get; set; }
        public string? Error { get; set; }
        public long AppliedTick { get; set; }
        /// <summary>Set by <see cref="SceneOp.Spawn"/> (element) and
        /// <see cref="SceneOp.AttachComponent"/> (component).</summary>
        public long? CreatedId { get; set; }
        /// <summary>Value before a <see cref="SceneOp.SetProperty"/> /
        /// <see cref="SceneOp.SetActive"/> / <see cref="SceneOp.SetTransform"/>.
        /// Lets a caller undo, and reveals that it overwrote someone else.</summary>
        public SnapshotValue? PreviousValue { get; set; }
        /// <summary>Transform before a <see cref="SceneOp.SetTransform"/>, so a
        /// caller can restore it exactly.</summary>
        public TRS? PreviousTransform { get; set; }
        public string? Actor { get; set; }
        public string? WorldName { get; set; }
    }
}
