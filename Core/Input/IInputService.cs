using V12.Core.Core.Interfaces;

namespace V12.Core.Input
{
    public interface IInputService : IGameService
    {
        void RegisterHandler(IInputHandler handler);
        void UnregisterHandler(IInputHandler handler);
        void SendEvent(InputEvent evt);
    }
}

