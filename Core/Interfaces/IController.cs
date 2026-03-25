using V12.Core.Core.Interfaces;

namespace V12.Core.Interfaces
{
    /// <summary>
    /// Represents any controller that can be attached to a world element to drive its behaviour
    /// from a specific input source (keyboard+mouse, gamepad, VR controllers, etc.).
    /// </summary>
    public interface IController
    {
        /// <summary>The input method(s) this controller handles.</summary>
        InputMethods SupportedInputMethods { get; }

        /// <summary>True once <see cref="AttachTo"/> has been called and before <see cref="Detach"/>.</summary>
        bool IsAttached { get; }

        /// <summary>Bind this controller to a world element so it can read its components and drive behaviour.</summary>
        void AttachTo(IWorldElement element);

        /// <summary>Release the controller from its current element.</summary>
        void Detach();
    }
}

