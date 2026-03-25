using V12.Core;

namespace V12.Core.Core.Interfaces
{
    /// <summary>
    /// Marker interface for components that signal an element needs a player controller.
    /// When <see cref="IsLocalControlled"/> is true and the element is spawned in a Godot scene,
    /// a matching <see cref="V12.Core.Interfaces.IController"/> will be created and attached
    /// automatically based on <see cref="RequiredInputMethod"/>.
    /// </summary>
    public interface IPlayerControlComponent : IComponent
    {
        /// <summary>The input method this component expects its controller to handle.</summary>
        InputMethods RequiredInputMethod { get; }

        /// <summary>Whether this element should accept local player input on the current machine.</summary>
        bool IsLocalControlled { get; }
    }
}
