using System;
using V12.Core.Core.Interfaces;

namespace V12.Core.UI
{
    /// <summary>
    /// Simple default dashboard implementation that builds a small tabbed
    /// UI using an `IUIBuilder`. Intended as a fallback if the host does not
    /// provide a front-end dashboard implementation.
    /// </summary>
    public class DefaultDashboard : IDashboard
    {
        private IUIBuilder _builder;
        private bool _isOpen;
        private string _activeTab = "World";

        public void Initialize(IUIBuilder builder)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
            BuildUI();
        }

        void BuildUI()
        {
            try
            {
                var root = _builder.Root;
                // Horizontal tab bar at the top
                var tabRow = _builder.HLayout(root, "Tabs", spacing: 8f);
                _builder.Button(tabRow, "World",  () => SwitchToTab("World"));
                _builder.Button(tabRow, "Player", () => SwitchToTab("Player"));
                _builder.Button(tabRow, "System", () => SwitchToTab("System"));

                // Initial content panel
                SwitchToTab(_activeTab);
            }
            catch { }
        }

        void SwitchToTab(string tab)
        {
            _activeTab = tab;
            // For the default implementation we keep it lightweight; hosts
            // may replace this dashboard with a richer implementation.
        }

        public void Open()
        {
            _isOpen = true;
        }

        public void Close()
        {
            _isOpen = false;
        }

        public void Update(double deltaSeconds)
        {
            // Default dashboard does not need per-frame updates yet.
        }

        public bool IsOpen => _isOpen;
    }
}
