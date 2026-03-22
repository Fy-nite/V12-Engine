namespace V12.Core.Core.Interfaces
{
    /// <summary>
    /// Represents a service managed by the game registry that participates in the game loop.
    /// </summary>
    public interface IGameService
    {
        void Initialize();
        void Update(float deltaTime);
    }
}
