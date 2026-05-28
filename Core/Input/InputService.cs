using System.Collections.Generic;
using System;
using V12.Core.Core.Interfaces;

namespace V12.Core.Input
{
    public class InputService : IInputService
    {
        readonly List<IInputHandler> _handlers = new();
        readonly object _lock = new object();

        public void Initialize() { }

        public void Update(float deltaTime) { }

        public void RegisterHandler(IInputHandler handler)
        {
            if (handler == null) return;
            lock (_lock) { if (!_handlers.Contains(handler)) _handlers.Add(handler); }
        }

        public void UnregisterHandler(IInputHandler handler)
        {
            if (handler == null) return;
            lock (_lock) { _handlers.Remove(handler); }
        }

        public void SendEvent(InputEvent evt)
        {
            if (evt == null) return;
            IInputHandler[] copy;
            lock (_lock) { copy = _handlers.ToArray(); }
            foreach (var h in copy)
            {
                try { h.OnInputEvent(evt); } catch { }
            }
        }

        public void Initialize(GameRoot g)
        {
            
        }
    }
}

