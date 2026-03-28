namespace V12.Core.Input
{
    /// <summary>
    /// Handler interface for receiving input events from the engine input service.
    /// </summary>
    public interface IInputHandler
    {
        void OnInputEvent(InputEvent evt);
    }
}

