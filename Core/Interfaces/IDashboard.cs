using V12.Core.UI;

namespace V12.Core.Core.Interfaces
{
    /// <summary>
    /// Frontend-agnostic dashboard interface. Implementations may use an
    /// `IUIBuilder` supplied by the engine to construct UI in a backend-agnostic
    /// manner (world-elements, immediate-mode, or platform-specific).
    /// </summary>
    public interface IDashboard
    {
        void Initialize(IUIBuilder builder);
        void Open();
        void Close();
        void Update(double deltaSeconds);
        bool IsOpen { get; }
    }
}
