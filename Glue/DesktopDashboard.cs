using System;
using System.Collections.Generic;

namespace V12.GlueCode
{
    /// <summary>
    /// Frontend-agnostic dashboard built into the core `V12` assembly. Hosts
    /// use `Update(double deltaSeconds)` to drive periodic refreshes. The
    /// dashboard renders via any `IV12UIBuilder` implementation provided by
    /// the host frontend.
    /// </summary>
    public class DesktopDashboard
    {
        private readonly IV12UIBuilder _ui;
        private readonly IUIElementHandle _rootPanel;
        private readonly Dictionary<string, IUIElementHandle> _fieldHandles = new();
        private readonly string[] _tabs = new[] { "World", "Player", "System" };
        private string _activeTab = "World";

        private double _refreshTimer;
        private const double RefreshInterval = 0.016; // 60 Hz refresh

        public DesktopDashboard(IV12UIBuilder ui)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _rootPanel = _ui.CreatePanel("V12 Dashboard");
            BuildRoot();
            SwitchToTab(_activeTab);
        }

        public bool IsOpen { get; private set; }

        public void Open()
        {
            IsOpen = true;
            RefreshValues();
        }

        public void Close()
        {
            IsOpen = false;
        }

        /// <summary>
        /// Host should call this from their update loop and provide delta seconds.
        /// </summary>
        public void Update(double deltaSeconds)
        {
            if (!IsOpen) return;
            _refreshTimer += deltaSeconds;
            if (_refreshTimer < RefreshInterval) return;
            _refreshTimer = 0;
            RefreshValues();
        }

        private void BuildRoot()
        {
            // Bottom toggles act as tab buttons.
            foreach (var t in _tabs)
            {
                var tabName = t;
                _ui.AddToggle(_rootPanel, tabName, tabName == _activeTab, isOn =>
                {
                    if (isOn) SwitchToTab(tabName);
                });
            }
        }

        private void SwitchToTab(string tab)
        {
            if (_ui == null) return;
            _activeTab = tab;

            _ui.Clear(_rootPanel);

            // Recreate bottom toggles so they persist after clear.
            foreach (var t in _tabs)
            {
                var tabName = t;
                _ui.AddToggle(_rootPanel, tabName, tabName == _activeTab, isOn =>
                {
                    if (isOn) SwitchToTab(tabName);
                });
            }

            _fieldHandles.Clear();

            switch (tab)
            {
                case "World":
                    _fieldHandles["world.name"] = _ui.AddField<string>(_rootPanel, "Name", "—", _ => { });
                    _fieldHandles["world.count"] = _ui.AddField<int>(_rootPanel, "Elements", 0, _ => { });
                    break;
                case "Player":
                    _fieldHandles["player.name"] = _ui.AddField<string>(_rootPanel, "Name", "—", _ => { });
                    _fieldHandles["player.pos"] = _ui.AddField<string>(_rootPanel, "Position", "—", _ => { });
                    _fieldHandles["player.health"] = _ui.AddField<string>(_rootPanel, "Health", "—", _ => { });
                    _fieldHandles["player.status"] = _ui.AddField<string>(_rootPanel, "Status", "—", _ => { });
                    break;
                case "System":
                    _fieldHandles["sys.net"] = _ui.AddField<string>(_rootPanel, "Network", "—", _ => { });
                    _fieldHandles["sys.os"] = _ui.AddField<string>(_rootPanel, "OS", Environment.OSVersion.ToString(), _ => { });
                    _fieldHandles["sys.ver"] = _ui.AddField<string>(_rootPanel, "Version", GetRuntimeVersion(), _ => { });
                    break;
            }

            RefreshValues();
        }

        private void RefreshValues()
        {
            if (_ui == null) return;

            var world = TryGetWorldInfo();
            if (world != null)
            {
                if (_fieldHandles.TryGetValue("world.name", out var h1)) _ui.UpdateFieldValue(h1, world.WorldName);
                if (_fieldHandles.TryGetValue("world.count", out var h2)) _ui.UpdateFieldValue(h2, world.ElementCount);
            }

            var player = TryGetPlayerInfo();
            if (player != null)
            {
                if (_fieldHandles.TryGetValue("player.name", out var p1)) _ui.UpdateFieldValue(p1, player.Name ?? "(unnamed)");
                if (_fieldHandles.TryGetValue("player.pos", out var p2)) _ui.UpdateFieldValue(p2, player.Position);
                if (_fieldHandles.TryGetValue("player.health", out var p3)) _ui.UpdateFieldValue(p3, player.Health);
                if (_fieldHandles.TryGetValue("player.status", out var p4)) _ui.UpdateFieldValue(p4, player.Status);
            }

            if (_fieldHandles.TryGetValue("sys.net", out var s1)) _ui.UpdateFieldValue(s1, TryGetNetworkStatus() ?? "—");
            if (_fieldHandles.TryGetValue("sys.os", out var s2)) _ui.UpdateFieldValue(s2, Environment.OSVersion.ToString());
            if (_fieldHandles.TryGetValue("sys.ver", out var s3)) _ui.UpdateFieldValue(s3, GetRuntimeVersion());
        }

        private static string GetRuntimeVersion()
        {
            try { return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription; }
            catch { return "unknown"; }
        }

        private class WorldInfo { public string WorldName = "(no world)"; public int ElementCount = 0; }
        private class PlayerInfo { public string? Name; public string Position = "—"; public string Health = "—"; public string Status = "—"; }

        private WorldInfo? TryGetWorldInfo()
        {
            try
            {
                var connectorType = Type.GetType("V12.V12Connector, V12");
                if (connectorType == null) return null;
                var prop = connectorType.GetProperty("Root");
                var root = prop?.GetValue(null);
                if (root == null) return null;
                var selectedWorldProp = root.GetType().GetProperty("SelectedWorld");
                var world = selectedWorldProp?.GetValue(root);
                if (world == null) return null;
                var nameProp = world.GetType().GetProperty("WorldName");
                var rootProp = world.GetType().GetProperty("Root");
                var name = nameProp?.GetValue(world)?.ToString() ?? "(no world)";
                var list = rootProp?.GetValue(world) as System.Collections.ICollection;
                var count = list?.Count ?? 0;
                return new WorldInfo { WorldName = name, ElementCount = count };
            }
            catch
            {
                return null;
            }
        }

        private PlayerInfo? TryGetPlayerInfo()
        {
            try
            {
                var connectorType = Type.GetType("V12.V12Connector, V12");
                if (connectorType == null) return null;
                var prop = connectorType.GetProperty("Root");
                var root = prop?.GetValue(null);
                if (root == null) return null;
                var selectedWorldProp = root.GetType().GetProperty("SelectedWorld");
                var world = selectedWorldProp?.GetValue(root);
                if (world == null) return null;

                var rootList = world.GetType().GetProperty("Root")?.GetValue(world) as System.Collections.IEnumerable;
                if (rootList == null) return null;

                foreach (var el in rootList)
                {
                    var compsProp = el.GetType().GetProperty("Components");
                    var comps = compsProp?.GetValue(el) as System.Collections.IEnumerable;
                    if (comps == null) continue;
                    foreach (var c in comps)
                    {
                        var typeName = c.GetType().FullName ?? "";
                        if (typeName.Contains("PlayerComponent") || typeName.Contains("IPlayerControlComponent"))
                        {
                            var isLocal = c.GetType().GetProperty("IsLocalControlled");
                            var local = isLocal?.GetValue(c) as bool? ?? false;
                            if (local)
                            {
                                var info = new PlayerInfo();
                                info.Name = el.GetType().GetProperty("Name")?.GetValue(el)?.ToString();
                                info.Position = "—";
                                info.Health = "—";
                                info.Status = "Active";
                                return info;
                            }
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private string? TryGetNetworkStatus()
        {
            try
            {
                var connectorType = Type.GetType("V12.V12Connector, V12");
                if (connectorType == null) return null;
                var prop = connectorType.GetProperty("NetworkStatus");
                var status = prop?.GetValue(null)?.ToString();
                return status;
            }
            catch
            {
                return null;
            }
        }
    }
}
