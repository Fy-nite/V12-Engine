using System;
using System.Numerics;
using V12.Core.Rendering;

namespace V12.Core.Input
{
    /// <summary>
    /// Routes pointer, focus and keyboard input to world-space Paper canvases.
    /// Picking and pixel mapping are engine-side (<see cref="WorldUiPicking"/>),
    /// so this works the same for every host and with no renderer at all.
    ///
    /// Screen-space UI keeps priority: its input handler runs first and marks
    /// pointer events handled while the screen layer owns them.
    /// </summary>
    public sealed class WorldUiInput : IInputHandler, IDisposable
    {
        private readonly InputService _input;
        private readonly Func<IUiSource?> _source;
        private FrameSnapshot? _snapshot;
        private int _screenWidth;
        private int _screenHeight;
        private Vector2 _pointer;
        private bool _hasPointer;
        private bool _leftDown;
        private long _hoveredId;
        private long _focusedId;
        private float _hoverPixelX;
        private float _hoverPixelY;
        private bool _disposed;

        public WorldUiInput(InputService input, Func<IUiSource?> source)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _input.RegisterHandler(this);
        }

        /// <summary>Canvas under the pointer this frame (0 when none).</summary>
        public long HoveredCanvasId => _hoveredId;

        /// <summary>Canvas that owns keyboard focus (0 when none).</summary>
        public long FocusedCanvasId => _focusedId;

        public void ClearFocus()
        {
            if (_focusedId == 0) return;
            _focusedId = 0;
            _input.UiCapturesKeyboard = false;
        }

        /// <summary>
        /// Called by the frame producer before the canvas surfaces step: stores
        /// the frame, recomputes hover for the current pointer and feeds every
        /// surface so hover and press state are in place for this step.
        /// </summary>
        public void BeginFrame(FrameSnapshot? snapshot, int screenWidth, int screenHeight)
        {
            if (_disposed) return;

            _snapshot = snapshot;
            _screenWidth = Math.Max(0, screenWidth);
            _screenHeight = Math.Max(0, screenHeight);

            RefreshHover();
            FeedSurfaces();

            _input.UiCapturesPointer = _hoveredId != 0;
            _input.UiCapturesKeyboard = _focusedId != 0;
        }

        public void OnInputEvent(InputEvent evt)
        {
            if (_disposed || evt == null) return;

            switch (evt.Type)
            {
                // Pointer state is fed to the surfaces once per frame in
                // BeginFrame, just before they step. Feeding here as well would
                // deliver duplicate press/release edges to Paper.
                case InputEventType.PointerMove:
                    SetPointer(evt.Pointer);
                    RefreshHover();
                    if (_hoveredId != 0) evt.Handled = true;
                    break;

                case InputEventType.ButtonDown:
                case InputEventType.ButtonUp:
                    if (evt.Key is { } key)
                    {
                        HandleKey(evt, key);
                        break;
                    }

                    if (evt.Button == PointerButton.Left)
                    {
                        SetPointer(evt.Pointer);
                        RefreshHover();
                        _leftDown = evt.Type == InputEventType.ButtonDown;

                        // Press focuses the canvas under the pointer; pressing
                        // empty space clears focus.
                        if (_leftDown) _focusedId = _hoveredId;

                        if (_hoveredId != 0 || _focusedId != 0)
                            evt.Handled = true;
                    }
                    break;

                case InputEventType.Char:
                case InputEventType.Text:
                    if (_focusedId != 0 &&
                        _source()?.FeedCanvasInput(_focusedId, evt) == true)
                        evt.Handled = true;
                    break;
            }

            _input.UiCapturesPointer = _hoveredId != 0;
            _input.UiCapturesKeyboard = _focusedId != 0;
        }

        private void HandleKey(InputEvent evt, InputKey key)
        {
            bool down = evt.Type == InputEventType.ButtonDown;

            if (down && key == InputKey.Escape && _focusedId != 0)
            {
                ClearFocus();
                evt.Handled = true;
                return;
            }

            if (_focusedId != 0 &&
                _source()?.FeedCanvasInput(_focusedId, evt) == true)
                evt.Handled = true;
        }

        private void SetPointer(Vector2 position)
        {
            _pointer = position;
            _hasPointer = true;
        }

        private void RefreshHover()
        {
            long hovered = 0;
            float pixelX = 0f;
            float pixelY = 0f;

            if (_hasPointer && _snapshot != null && _screenWidth > 0 && _screenHeight > 0 &&
                WorldUiPicking.TryPick(_snapshot, _pointer, _screenWidth, _screenHeight, out var hit))
            {
                hovered = hit.CanvasId;
                pixelX = hit.PixelX;
                pixelY = hit.PixelY;
            }

            _hoveredId = hovered;
            _hoverPixelX = pixelX;
            _hoverPixelY = pixelY;

            // A focused canvas that disappeared loses focus.
            if (_focusedId != 0 && _source()?.ContainsCanvas(_focusedId) != true)
                ClearFocus();
        }

        /// <summary>Feeds every canvas: hover coordinates for the one under the
        /// pointer, off-canvas for the rest, so stale hover never sticks.</summary>
        private void FeedSurfaces()
        {
            var source = _source();
            if (source == null) return;

            foreach (var canvasId in source.CanvasIds)
            {
                bool hovered = _hoveredId != 0 && canvasId == _hoveredId;
                source.FeedCanvasPointer(
                    canvasId,
                    hovered ? _hoverPixelX : -10000f,
                    hovered ? _hoverPixelY : -10000f,
                    hovered && _leftDown);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _input.UnregisterHandler(this);
            _input.UiCapturesPointer = false;
            _input.UiCapturesKeyboard = false;
        }
    }
}
