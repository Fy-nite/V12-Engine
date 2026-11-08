using V12.Core.Core.Interfaces;

namespace V12.Core.Input
{
    public interface IInputService : IGameService
    {
        void RegisterHandler(IInputHandler handler);
        void UnregisterHandler(IInputHandler handler);
        void SendEvent(InputEvent evt);

        /// <summary>UI owns the pointer this frame; gameplay pointer input should be suppressed.</summary>
        bool UiCapturesPointer { get; set; }

        /// <summary>UI owns keyboard focus; gameplay key input should be suppressed.</summary>
        bool UiCapturesKeyboard { get; set; }
    }
}

