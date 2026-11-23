using System;
using System.Collections.Generic;
using V12.Core.Core.Interfaces;

namespace V12.Core.UI
{
    /// <summary>
    /// Arguments for a UI action invoked by name from WorldML.
    /// A button uses <see cref="Owner"/>; a toggle carries <see cref="On"/>, a
    /// slider <see cref="Value"/>, and tabs/table/dropdown <see cref="Index"/>.
    /// </summary>
    public readonly record struct UiActionArgs(
        IWorldElement? Owner = null,
        bool On = false,
        float Value = 0f,
        int Index = -1);

    /// <summary>
    /// Named UI actions, so WorldML can bind a widget without a C# closure:
    ///
    /// <code>
    /// &lt;Component type="V12.Components.UI.ButtonComponent" Label="Pulse" Action="pulse" /&gt;
    /// </code>
    ///
    /// A script registers the handler once:
    ///
    /// <code>
    /// UiActions.Register("pulse", args =&gt; ...);
    /// </code>
    ///
    /// A component's own <c>OnClick</c>/<c>OnChanged</c> delegate always wins;
    /// the named action is the fallback, so authored content works with no
    /// per-widget wiring.
    /// </summary>
    public static class UiActions
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, Action<UiActionArgs>> Handlers =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registers (or replaces) the handler for <paramref name="name"/>.</summary>
        public static void Register(string name, Action<UiActionArgs> handler)
        {
            if (string.IsNullOrWhiteSpace(name) || handler is null) return;
            lock (Gate) Handlers[name.Trim()] = handler;
        }

        /// <summary>Removes the handler for <paramref name="name"/>, if any.</summary>
        public static void Unregister(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            lock (Gate) Handlers.Remove(name.Trim());
        }

        /// <summary>Removes every handler. Call when a gamepak stops.</summary>
        public static void Clear()
        {
            lock (Gate) Handlers.Clear();
        }

        /// <summary>True when a handler is registered for <paramref name="name"/>.</summary>
        public static bool IsRegistered(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            lock (Gate) return Handlers.ContainsKey(name.Trim());
        }

        /// <summary>
        /// Invokes the handler named by a widget. Returns false when the name is
        /// empty or unknown (the widget then simply does nothing).
        /// </summary>
        public static bool Invoke(string? name, in UiActionArgs args)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            Action<UiActionArgs>? handler;
            lock (Gate) Handlers.TryGetValue(name.Trim(), out handler);

            if (handler is null)
            {
                GameRoot.Log?.Warning("[UiActions] No handler registered for action '{Name}'", name);
                return false;
            }

            try
            {
                handler(args);
                // The handler usually changes visible state; the next frame must
                // capture it, or a click appears to do nothing until something
                // else forces a render.
                GameRoot.Instance?.MarkRenderDirty();
                return true;
            }
            catch (Exception ex)
            {
                GameRoot.Log?.Warning(ex, "[UiActions] Action '{Name}' threw", name);
                return false;
            }
        }
    }
}
