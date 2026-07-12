namespace V12
{
    /// <summary>
    /// Core interface for a loadable game pak (plugin). Game paks are discovered
    /// at runtime by <see cref="GamePak.GamepackLoader"/>, instantiated, and
    /// driven through a two-phase lifecycle: Initialize then OnStart.
    /// </summary>
    public interface IV12Gamepack
    {
        /// <summary>Human-readable name for logging and identification.</summary>
        string Name { get; }

        /// <summary>
        /// First phase. Called after the backend (Godot) has registered platform
        /// services (IUIProvider, IRenderTargetFactory, etc.) but before the
        /// game loop begins. Register gamepak-specific services here.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Second phase. Called after all gamepaks have been initialized and
        /// <see cref="GameRoot.Initialize"/> has completed. Build UI, resolve
        /// services, and begin execution here.
        /// </summary>
        void OnStart();
    }
}
