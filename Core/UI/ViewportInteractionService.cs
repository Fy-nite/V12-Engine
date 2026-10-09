using System;
using System.Collections.Generic;
using System.Numerics;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Components;
using V12.Components.Renderables;
using DrawingColor = System.Drawing.Color;

namespace V12.Core.UI
{
    /// <summary>
    /// Renderer-agnostic editor viewport interaction: orbit camera, object
    /// picking and the translation gizmo, plus the in-app file dialog behind
    /// <see cref="IViewportInteraction.ShowFileDialog"/>.
    ///
    /// Lives in the core V12 assembly beside <see cref="IViewportInteraction"/>;
    /// host renderers contribute primitives through <see cref="IViewportInteractionHost"/>
    /// and per-frame input through <see cref="IEditorInputSource"/> (the
    /// MonoGame host ships both as one adapter, nova drives its own
    /// WorldCanvasSystem instead).
    ///
    /// Right-drag orbits, wheel dollies, left-click picks (gizmo handle first),
    /// drag transforms along the grabbed handle: move (translate), sweep around
    /// the axis (rotate), stretch along it (scale). T / R / S switch mode; the
    /// gizmo is transient mesh entities (shafts+cones for translate, rings for
    /// rotate, shafts+cubes for scale) sized constant on screen.
    ///
    /// Runs as an <see cref="IGameService"/> so it ticks on the game thread
    /// inside <c>GameRoot.Update</c>.
    /// </summary>
    public sealed class ViewportInteractionService : IViewportInteraction, IGameService
    {
        private const float DegreesPerPixel = 0.25f;
        private const float MinPitchDeg = -89f;
        private const float MaxPitchDeg = 89f;
        private const float MinDist = 1f;
        private const float MaxDist = 200f;
        private const float DefaultDist = 7f;

        private readonly IViewportInteractionHost _host;
        private readonly IEditorInputSource _input;
        private readonly ViewportFileDialog _dialog;
        private readonly GameRoot _root;

        private Action<IWorldElement?>? _selectHandler;
        private IWorldElement? _editorCam;
        private IWorldElement? _gizmoTarget;
        private GizmoMode _mode = GizmoMode.Translate;

        // Orbit state (yaw / pitch / distance / focus), nova's model.
        private float _yawDeg;
        private float _pitchDeg;
        private float _dist = DefaultDist;
        private Vector3 _focus;
        private bool _orbiting;
        private Vector2 _prevMouse;
        private int _lastWheel;
        private bool _wheelInit;

        // Edge tracking against the previous input snapshot.
        private bool _prevLeftDown;
        private bool _prevT;
        private bool _prevR;
        private bool _prevS;
        private bool _prevEsc;

        // Active axis drag: pinned to the viewport the grab happened in so the
        // drag keeps tracking after the cursor leaves the rect (nova polls the
        // same way outside the container). Rotate/scale pin the target's full
        // world matrix at grab time so the whole gesture is a delta from the
        // press, never a re-read of whatever the element is mid-drag.
        private int _dragAxis = -1;
        private long _dragViewportId;
        private Vector3 _dragStartHit;
        private Vector3 _dragStartPos;
        private Matrix4x4 _dragStartWorld;
        private Vector3 _dragStartScale = Vector3.One;
        private float _dragRefLen = 1f;

        private readonly List<ViewportPickMesh> _pickBuffer = new();
        private readonly List<TransformGizmo.GizmoHandleSpec> _specScratch = new();

        // Transient gizmo entities: one "_EditorGizmo" root per target world
        // with a child element per handle mesh (see SyncGizmoEntities). They
        // render and pick through the normal pipeline; the transient marker
        // keeps them out of saves and network sync.
        private IWorldElement? _gizmoRoot;
        private World? _gizmoWorld;
        private readonly List<GizmoPart> _gizmoParts = new();
        private IWorldElement? _gizmoBuiltTarget;
        private GizmoMode _gizmoBuiltMode = GizmoMode.Translate;
        private Vector3 _gizmoLastOrigin;
        private float _gizmoLastSize;

        private sealed class GizmoPart
        {
            public IWorldElement Element;
            public ComponentBase Mesh;
            public TransformGizmo.GizmoMeshKind Kind;
        }

        public ViewportInteractionService(GameRoot root, IViewportInteractionHost host, IEditorInputSource input)
        {
            _root = root;
            _host = host;
            _input = input;
            _dialog = new ViewportFileDialog(root);
            // No overlay callback: the gizmo is transient mesh entities in the
            // target's world now, drawn by the normal render path in every host.
            _host.ViewportOverlay = null;
        }

        public void Initialize(GameRoot gameRoot) { }

        public void Update(float deltaTime) => Tick();

        public void Update(GameRoot gameRoot) { }

        public void SetSelectHandler(Action<IWorldElement?> handler) => _selectHandler = handler;

        /// <summary>Register the camera element to drive. Orbit state is seeded
        /// from its current pose (focus = a point ahead of it), so the opening
        /// framing is preserved exactly.</summary>
        public void SetEditorCamera(IWorldElement? cameraElement)
        {
            _editorCam = cameraElement;
            _orbiting = false;
            if (_editorCam == null) return;

            var w = _editorCam.WorldTransform;
            var pos = new Vector3(w.M41, w.M42, w.M43);
            var fwd = new Vector3(-w.M31, -w.M32, -w.M33);
            if (fwd.LengthSquared() < 1e-8f) fwd = -Vector3.UnitZ;
            fwd = Vector3.Normalize(fwd);

            _dist = DefaultDist;
            _focus = pos + fwd * _dist;
            // Spherical dir IS the camera's forward (ApplyToCamera computes
            // pos = focus - dir*dist) — seeding it backwards would mirror the
            // camera across the focus and negate pitch (nova parity).
            var dir = fwd;
            _yawDeg = MathF.Atan2(dir.Z, dir.X) * 180f / MathF.PI;
            _pitchDeg = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f)) * 180f / MathF.PI;
        }

        /// <summary>Show the gizmo around <paramref name="el"/> (null hides it).
        /// Also recenters the orbit focus on the element — editor standard,
        /// matches nova. A new target aborts any active drag.</summary>
        public void SetGizmoTarget(IWorldElement? el)
        {
            if (!ReferenceEquals(el, _gizmoTarget)) _dragAxis = -1;
            _gizmoTarget = el;
            if (el == null) DestroyGizmo();
            if (el == null || _orbiting) return;
            var w = el.WorldTransform;
            _focus = new Vector3(w.M41, w.M42, w.M43);
        }

        public void SetGizmoMode(GizmoMode mode)
        {
            _mode = mode;
            if (mode != GizmoMode.Translate) _dragAxis = -1;
        }

        public void ShowFileDialog(bool save, string title, string initialPath, Action<string?> onComplete)
            => _dialog.Open(save, title, initialPath, onComplete);

        // ── Per-frame input ───────────────────────────────────────────────

        private void Tick()
        {
            TickInput();

            // The orbit state is authoritative: rewrite the camera every frame
            // so a focus recenter (selection) or seed applies immediately.
            // Nova's UpdateCamera runs every frame too — applying only on input
            // leaves the camera stale against the state and jerks on the next
            // drag, which reads as the camera rotating "around the wrong spot".
            ApplyToCamera();

            // Keep the transient gizmo entities on the target (constant screen
            // size while dollying). Runs after the camera so sizing is exact.
            SyncGizmoEntities();
        }

        private void TickInput()
        {
            var s = _input.GetSnapshot();

            // While the file dialog is open the viewport is inert: no orbit,
            // no wheel, no picking through the dialog (its own controls come
            // from Gum on the same thread). Esc cancels the dialog.
            if (_dialog.IsOpen)
            {
                _orbiting = false;
                _dragAxis = -1;
                if (s.KeyEscape && !_prevEsc) _dialog.Cancel();
                _prevEsc = s.KeyEscape;
                _prevT = s.KeyT;
                _prevR = s.KeyR;
                _prevS = s.KeyS;
                _prevLeftDown = s.LeftDown;
                _wheelInit = true;
                _lastWheel = s.Wheel;
                return;
            }

            if (_editorCam == null || !s.WindowActive || s.MouseLocked)
            {
                if (s.LeftDown && !_prevLeftDown)
                    Console.WriteLine($"[Viewport] click dropped by guard: cam={_editorCam != null} active={s.WindowActive} lockMouse={s.MouseLocked}");
                return;
            }

            if (!_wheelInit) { _wheelInit = true; _lastWheel = s.Wheel; }

            bool inViewport = _host.TryGetViewportAt(s.MousePosition, out var vpId);

            // T / R / S switch gizmo mode (editor convention, nova parity).
            if (s.KeyT && !_prevT) SetGizmoMode(GizmoMode.Translate);
            else if (s.KeyR && !_prevR) SetGizmoMode(GizmoMode.Rotate);
            else if (s.KeyS && !_prevS) SetGizmoMode(GizmoMode.Scale);
            _prevT = s.KeyT;
            _prevR = s.KeyR;
            _prevS = s.KeyS;

            // Right-press starts an orbit only inside a viewport; release ends
            // it anywhere (polling, so mid-drag window leave still recovers).
            if (s.RightDown && !_orbiting)
            {
                if (inViewport)
                {
                    _orbiting = true;
                    _prevMouse = s.MousePosition;
                }
            }
            else if (!s.RightDown)
            {
                _orbiting = false;
            }

            if (_orbiting)
            {
                float dx = s.MousePosition.X - _prevMouse.X;
                float dy = s.MousePosition.Y - _prevMouse.Y;
                _prevMouse = s.MousePosition;
                if (dx != 0 || dy != 0)
                {
                    // Drag right → scene follows the cursor (sign flipped from
                    // the first cut after hands-on feedback: it was reversed).
                    _yawDeg += dx * DegreesPerPixel;
                    _pitchDeg = Math.Clamp(_pitchDeg + dy * DegreesPerPixel, MinPitchDeg, MaxPitchDeg);
                }
            }

            // Wheel dollies when over a viewport (UI scroll panels consume the
            // wheel over their own rects; the two never overlap by layout).
            int wheelDelta = s.Wheel - _lastWheel;
            _lastWheel = s.Wheel;
            if (wheelDelta != 0 && (inViewport || _orbiting))
            {
                // Same feel as nova (*0.9 / *1.1 per notch), smooth-wheel aware.
                _dist = Math.Clamp(_dist * MathF.Pow(1.1f, -(wheelDelta / 120f)), MinDist, MaxDist);
            }

            // Left button: press starts an axis grab or a pick; release ends
            // the drag (anywhere — the drag polls the pinned viewport).
            bool leftDown = s.LeftDown;
            bool leftPressed = leftDown && !_prevLeftDown;
            if (!leftDown && _dragAxis >= 0) _dragAxis = -1;
            _prevLeftDown = leftDown;

            if (leftPressed && inViewport && !_orbiting)
            {
                // Grab an axis arrow first — a gizmo drag consumes the press.
                if (_gizmoTarget != null && TryGrabAxis(vpId, s.MousePosition))
                {
                    Console.WriteLine($"[Viewport] grabbed handle {_dragAxis} of '{_gizmoTarget.Name}' vp={vpId}");
                }
                else
                {
                    var el = Pick(vpId, s.MousePosition);
                    Console.WriteLine($"[Viewport] press vp={vpId} at ({s.MousePosition.X},{s.MousePosition.Y}) target='{el?.Name ?? "(none)"}'");
                    _selectHandler?.Invoke(el);
                    SetGizmoTarget(el);
                }
            }
            else if (leftPressed)
            {
                Console.WriteLine($"[Viewport] press dropped: inViewport={inViewport} orbiting={_orbiting} pos=({s.MousePosition.X},{s.MousePosition.Y})");
            }

            if (leftDown && _dragAxis >= 0) UpdateDrag(s.MousePosition);
        }

        // ── Orbit camera ──────────────────────────────────────────────────

        /// <summary>Recompute the camera element's transform from orbit state.
        /// Same model as nova: pos = focus - dir*dist, looking at focus.</summary>
        private void ApplyToCamera()
        {
            if (_editorCam == null) return;

            float cp = _pitchDeg * MathF.PI / 180f;
            float cy = _yawDeg * MathF.PI / 180f;
            var dir = new Vector3(MathF.Cos(cp) * MathF.Cos(cy), MathF.Sin(cp), MathF.Cos(cp) * MathF.Sin(cy));
            var pos = _focus - dir * _dist;

            // Camera looks along +dir with up ≈ +Y: build the rotation from an
            // orthonormal basis (matches BuildViewProjection's -Z-forward read).
            var fwd = Vector3.Normalize(dir);
            var right = Vector3.Cross(fwd, Vector3.UnitY);
            if (right.LengthSquared() < 1e-8f) right = Vector3.UnitX;
            right = Vector3.Normalize(right);
            var up = Vector3.Cross(right, fwd);
            var m = new Matrix4x4(
                right.X, right.Y, right.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                -fwd.X, -fwd.Y, -fwd.Z, 0f,
                pos.X, pos.Y, pos.Z, 1f);
            var q = Quaternion.CreateFromRotationMatrix(m);

            // The camera is normally a scene root; tolerate nesting by pushing
            // the pose through the parent's inverse when there is one.
            var parent = _editorCam.Parent;
            if (parent == null)
            {
                _editorCam.LocalTransform = new TRS
                {
                    Position = pos,
                    Rotation = q,
                    Scale = Vector3.One
                };
            }
            else if (Matrix4x4.Invert(parent.WorldTransform, out var invParent))
            {
                var local = m * invParent;
                if (Matrix4x4.Decompose(local, out var s, out var lq, out var lp))
                    _editorCam.LocalTransform = new TRS { Position = lp, Rotation = lq, Scale = s };
            }
        }

        // ── Picking ───────────────────────────────────────────────────────

        /// <summary>Cast a ray through the mouse and return the nearest owning
        /// element, or null for empty space (nova HandleViewportPress parity:
        /// null → the editor deselects).</summary>
        private IWorldElement? Pick(long vpId, Vector2 mouse)
        {
            if (!_host.TryUnprojectRay(vpId, mouse, out var ro, out var rd))
            {
                Console.WriteLine($"[Viewport] pick vp={vpId}: ray failed");
                return null;
            }

            _host.CollectPickMeshes(vpId, _pickBuffer);
            float bestDist = float.MaxValue;
            IWorldElement? best = null;

            for (int i = 0; i < _pickBuffer.Count; i++)
            {
                var pm = _pickBuffer[i];
                if (pm.Owner == null) continue;
                // Gizmo handle meshes are grabbable, never selectable.
                if (IsGizmoElement(pm.Owner)) continue;

                // Work in local space (meshes are small), then convert the hit
                // back to world for a distance that is comparable across meshes.
                if (!Matrix4x4.Invert(pm.World, out var invW)) continue;
                var lo = Vector3.Transform(ro, invW);
                var ld = Vector3.TransformNormal(rd, invW);
                float ldLen = ld.Length();
                if (ldLen < 1e-8f) continue;
                ld /= ldLen;

                var pts = pm.Points;
                var idx = pm.Indices;
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    int i0 = (int)idx[t] * 3, i1 = (int)idx[t + 1] * 3, i2 = (int)idx[t + 2] * 3;
                    if (i0 < 0 || i1 < 0 || i2 < 0 || i0 + 2 >= pts.Length || i1 + 2 >= pts.Length || i2 + 2 >= pts.Length)
                        continue;

                    var v0 = new Vector3((float)pts[i0], (float)pts[i0 + 1], (float)pts[i0 + 2]);
                    var v1 = new Vector3((float)pts[i1], (float)pts[i1 + 1], (float)pts[i1 + 2]);
                    var v2 = new Vector3((float)pts[i2], (float)pts[i2 + 1], (float)pts[i2 + 2]);

                    if (!RayTriangle(lo, ld, v0, v1, v2, out float hitT)) continue;

                    var worldHit = Vector3.Transform(lo + ld * hitT, pm.World);
                    float d = Vector3.Distance(worldHit, ro);
                    if (d > 1e-4f && d < bestDist)
                    {
                        bestDist = d;
                        best = pm.Owner;
                    }
                }
            }
            Console.WriteLine($"[Viewport] pick vp={vpId} meshes={_pickBuffer.Count} -> {best?.Name ?? "(none)"} dist={(best != null ? bestDist.ToString("F2") : "-")}");
            return best;
        }

        /// <summary>Möller–Trumbore, two-sided (editor meshes have no winding
        /// guarantee once parents rotate).</summary>
        private static bool RayTriangle(Vector3 o, Vector3 d, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0f;
            var e1 = v1 - v0;
            var e2 = v2 - v0;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-8f) return false;
            float invDet = 1f / det;
            var tv = o - v0;
            float u = Vector3.Dot(tv, p) * invDet;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(d, q) * invDet;
            if (v < 0f || u + v > 1f) return false;
            float hit = Vector3.Dot(e2, q) * invDet;
            if (hit < 1e-5f) return false;
            t = hit;
            return true;
        }

        // ── Gizmo: grab + drag ────────────────────────────────────────────

        /// <summary>Analytic test of the gizmo handles — ray against the
        /// canonical fat pick shapes; on a hit, pins the drag state and
        /// returns true (caller skips world picking). The visual mesh is never
        /// raycast: what you see is what you can drag.</summary>
        private bool TryGrabAxis(long vpId, Vector2 mouse)
        {
            if (_gizmoTarget == null) return false;
            if (!_host.TryUnprojectRay(vpId, mouse, out var ro, out var rd)) return false;
            if (rd.LengthSquared() < 1e-12f) return false;
            rd = Vector3.Normalize(rd);

            if (!GizmoOriginAndSize(out var origin, out var size)) return false;
            TransformGizmo.BuildHandleSpecs(_mode, origin, size, _specScratch);
            if (!TransformGizmo.TryHitHandles(ro, rd, _specScratch, out int bestHandle, out _))
                return false;

            // Pin the drag to this viewport and this camera pose relationship.
            var w = _gizmoTarget.WorldTransform;
            _dragAxis = bestHandle;
            _dragViewportId = vpId;
            _dragStartPos = origin;
            _dragStartWorld = w;

            if (!_host.TryGetViewportCamera(vpId, out var view, out _)) return false;
            if (!Matrix4x4.Invert(view, out var invView)) return false;

            if (_mode == GizmoMode.Rotate)
            {
                // Rotation works in the plane through the pivot whose normal is
                // the grabbed axis — not the camera-facing drag plane.
                _dragStartHit = RayAxisPlaneHit(origin, TransformGizmo.AxisDirs[bestHandle], ro, rd);
            }
            else
            {
                var camFwd = CameraForward(invView);
                _dragStartHit = RayAxisHit(ro, rd, origin, camFwd);
                _dragRefLen = MathF.Max(1e-3f, size);
            }

            if (_mode == GizmoMode.Scale)
            {
                // Rendering scales a mesh via Width×ScaleComponent and ignores
                // Element.LocalTransform.Scale, so the gizmo must drive the
                // ScaleComponent (created on first scale drag).
                var sc = _gizmoTarget.GetComponent<ScaleComponent>();
                _dragStartScale = sc != null
                    ? new Vector3(sc.ScaleX, sc.ScaleY, sc.ScaleZ)
                    : Vector3.One;
            }
            return true;
        }

        /// <summary>Move / rotate / scale the drag target, always as a delta
        /// from the state pinned at grab time: translate projects onto the drag
        /// plane and keeps the axis component, rotate sweeps the angle around
        /// the axis in its own plane, scale multiplies along the axis.</summary>
        private void UpdateDrag(Vector2 mouse)
        {
            if (_dragAxis < 0 || _gizmoTarget == null) return;
            if (!_host.TryUnprojectRay(_dragViewportId, mouse, out var ro, out var rd)) return;
            if (!_host.TryGetViewportCamera(_dragViewportId, out var view, out _)) return;
            if (!Matrix4x4.Invert(view, out var invView)) return;

            var camFwd = CameraForward(invView);

            if (_mode == GizmoMode.Scale && _dragAxis == TransformGizmo.CenterHandle)
            {
                // Centre cube → uniform scale. Anchor-style ratio along the
                // look direction: factor = current/start signed distance from
                // the pivot, so it tracks the mouse naturally (nova parity).
                var hit = RayAxisHit(ro, rd, _dragStartPos, camFwd);
                float d0 = Vector3.Dot(_dragStartHit - _dragStartPos, camFwd);
                float d1 = Vector3.Dot(hit - _dragStartPos, camFwd);
                float factor = MathF.Abs(d0) < 1e-4f ? 1f : d1 / d0;
                factor = Math.Clamp(factor, 0.05f, 50f);

                var scCenter = _gizmoTarget.GetComponent<ScaleComponent>();
                if (scCenter == null)
                {
                    scCenter = new ScaleComponent();
                    _gizmoTarget.AddComponent(scCenter);
                }
                scCenter.ScaleX = _dragStartScale.X * factor;
                scCenter.ScaleY = _dragStartScale.Y * factor;
                scCenter.ScaleZ = _dragStartScale.Z * factor;
                return;
            }

            if (_dragAxis > 2) return;
            var axis = TransformGizmo.AxisDirs[_dragAxis];

            if (_mode == GizmoMode.Rotate)
            {
                var hit = RayAxisPlaneHit(_dragStartPos, axis, ro, rd);

                // Signed angle around the axis from the pinned start hit:
                // u = start direction (⊥ axis), v = axis × u.
                var u = _dragStartHit - _dragStartPos;
                u -= axis * Vector3.Dot(u, axis);
                if (u.LengthSquared() < 1e-10f) return;
                u = Vector3.Normalize(u);
                var v = Vector3.Cross(axis, u);
                var w = hit - _dragStartPos;
                float ang = MathF.Atan2(Vector3.Dot(w, v), Vector3.Dot(w, u));
                if (MathF.Abs(ang) < 1e-4f) return;

                var rot = Matrix4x4.CreateFromAxisAngle(axis, ang);
                var world = _dragStartWorld
                    * Matrix4x4.CreateTranslation(-_dragStartPos)
                    * rot
                    * Matrix4x4.CreateTranslation(_dragStartPos);
                SetElementWorldTransform(_gizmoTarget, world);
            }
            else if (_mode == GizmoMode.Scale)
            {
                var hit = RayAxisHit(ro, rd, _dragStartPos, camFwd);
                float delta = Vector3.Dot(hit - _dragStartHit, axis);
                float factor = MathF.Max(0.01f, 1f + delta / _dragRefLen);
                if (MathF.Abs(factor - 1f) < 1e-5f) return;

                var sc = _gizmoTarget.GetComponent<ScaleComponent>();
                if (sc == null)
                {
                    sc = new ScaleComponent();
                    _gizmoTarget.AddComponent(sc);
                }
                float sx = _dragStartScale.X, sy = _dragStartScale.Y, sz = _dragStartScale.Z;
                if (_dragAxis == 0) sx = _dragStartScale.X * factor;
                else if (_dragAxis == 1) sy = _dragStartScale.Y * factor;
                else sz = _dragStartScale.Z * factor;
                sc.ScaleX = sx;
                sc.ScaleY = sy;
                sc.ScaleZ = sz;
            }
            else
            {
                var hit = RayAxisHit(ro, rd, _dragStartPos, camFwd);
                float delta = Vector3.Dot(hit - _dragStartHit, axis);
                SetElementWorldPosition(_gizmoTarget, _dragStartPos + axis * delta);
            }
        }

        /// <summary>Write a world-space position onto the element, going through
        /// the parent's inverse when it is nested.</summary>
        private static void SetElementWorldPosition(IWorldElement el, Vector3 worldPos)
        {
            var parent = el.Parent;
            if (parent == null)
            {
                var t = el.LocalTransform;
                t.Position = worldPos;
                el.LocalTransform = t;
            }
            else if (Matrix4x4.Invert(parent.WorldTransform, out var invParent))
            {
                var t = el.LocalTransform;
                t.Position = Vector3.Transform(worldPos, invParent);
                el.LocalTransform = t;
            }
        }

        /// <summary>Write a full world matrix (rotate / scale results) onto the
        /// element: push through the parent's inverse and decompose back into
        /// position / rotation / scale.</summary>
        private static void SetElementWorldTransform(IWorldElement el, Matrix4x4 world)
        {
            var local = world;
            var parent = el.Parent;
            if (parent != null)
            {
                if (!Matrix4x4.Invert(parent.WorldTransform, out var invParent)) return;
                local = world * invParent;
            }
            if (!Matrix4x4.Decompose(local, out var s, out var q, out var p)) return;
            el.LocalTransform = new TRS { Position = p, Rotation = q, Scale = s };
        }

        /// <summary>Intersect the ray with the plane through
        /// <paramref name="point"/> perpendicular to the camera forward — the
        /// classic drag plane. The hit tracks the mouse; the axis dot in
        /// <see cref="UpdateDrag"/> discards any perpendicular motion. The plane
        /// must NOT contain the camera (t = 0 for every ray → dead drag).</summary>
        private static Vector3 RayAxisHit(Vector3 origin, Vector3 dir, Vector3 point, Vector3 fwd)
        {
            float denom = Vector3.Dot(dir, fwd);
            if (MathF.Abs(denom) < 1e-6f) return point;
            float t = Vector3.Dot(point - origin, fwd) / denom;
            return origin + dir * t;
        }

        /// <summary>Intersect the ray with the plane through
        /// <paramref name="point"/> whose normal is <paramref name="axis"/> —
        /// the rotation plane for a ring drag.</summary>
        private static Vector3 RayAxisPlaneHit(Vector3 point, Vector3 axis, Vector3 origin, Vector3 dir)
        {
            float denom = Vector3.Dot(dir, axis);
            if (MathF.Abs(denom) < 1e-6f) return point;
            float t = Vector3.Dot(point - origin, axis) / denom;
            return origin + dir * t;
        }

        private static Vector3 CameraForward(Matrix4x4 invView)
        {
            // invView is the camera's world matrix: row 3 is its +Z (backwards).
            var fwd = new Vector3(-invView.M31, -invView.M32, -invView.M33);
            return fwd.LengthSquared() < 1e-12f ? Vector3.UnitZ : Vector3.Normalize(fwd);
        }

        // ── Gizmo: transient mesh entities ──────────────────────────────────
        //
        // The gizmo is a "_EditorGizmo" root in the target's world with one
        // child element per handle mesh (cylinders/cones/cubes/torus from the
        // canonical specs). It renders and picks through the normal pipeline
        // in every host — no overlay draw path, no renderer special-casing.
        // The transient marker keeps it out of saves and network sync; grabs
        // use the analytic pick shapes (TryGrabAxis), never the visual mesh.

        /// <summary>True when <paramref name="el"/> is the gizmo root or one
        /// of its parts.</summary>
        private bool IsGizmoElement(IWorldElement? el)
        {
            if (el == null || _gizmoRoot == null) return false;
            for (var cur = el; cur != null; cur = cur.Parent)
                if (ReferenceEquals(cur, _gizmoRoot)) return true;
            return false;
        }

        /// <summary>Gizmo pivot (target world position) and constant-screen
        /// size from the editor camera. False when there is no target or no
        /// camera yet.</summary>
        private bool GizmoOriginAndSize(out Vector3 origin, out float size)
        {
            origin = Vector3.Zero;
            size = 0f;
            if (_gizmoTarget == null || _editorCam == null) return false;
            var w = _gizmoTarget.WorldTransform;
            origin = new Vector3(w.M41, w.M42, w.M43);
            var cw = _editorCam.WorldTransform;
            var camPos = new Vector3(cw.M41, cw.M42, cw.M43);
            float dist = Vector3.Distance(camPos, origin);
            float fov = 60f;
            var cam = _editorCam.GetComponent<ICameraRenderable>();
            if (cam != null && cam.FieldOfView > 1f && cam.FieldOfView < 179f)
                fov = cam.FieldOfView;
            size = TransformGizmo.ScreenSizeFromFov(dist, fov);
            return size > 0f;
        }

        /// <summary>Per-frame upkeep: ensure the gizmo tree exists for the
        /// target, rebuild it on target/mode/world change, and refresh part
        /// transforms so the gizmo holds constant screen size while dollying.
        /// Static frames skip untouched (element TRS assignment is
        /// epsilon-guarded, dims are compared before assignment).</summary>
        private void SyncGizmoEntities()
        {
            var target = _gizmoTarget;
            if (target == null) return; // SetGizmoTarget(null) destroys eagerly
            var world = _root.GetWorldForElement(target);
            if (world == null) { DestroyGizmo(); return; }

            bool needRebuild = _gizmoRoot == null
                || !ReferenceEquals(_gizmoWorld, world)
                || !world.Root.Contains(_gizmoRoot)
                || !ReferenceEquals(_gizmoBuiltTarget, target)
                || _gizmoBuiltMode != _mode;
            if (!GizmoOriginAndSize(out var origin, out var size)) return;
            if (needRebuild)
            {
                RebuildGizmo(world, target, origin, size);
                return;
            }
            if (_gizmoParts.Count == 0) return;
            if ((origin - _gizmoLastOrigin).LengthSquared() < 1e-10f
                && MathF.Abs(size - _gizmoLastSize) < 1e-6f)
                return; // static frame: nothing moved
            _gizmoLastOrigin = origin;
            _gizmoLastSize = size;

            TransformGizmo.BuildHandleSpecs(_mode, origin, size, _specScratch);
            int expected = 0;
            foreach (var s in _specScratch)
            {
                if (s.Mesh0.Mesh != TransformGizmo.GizmoMeshKind.None) expected++;
                if (s.Mesh1.Mesh != TransformGizmo.GizmoMeshKind.None) expected++;
            }
            if (_gizmoParts.Count != expected)
            {
                RebuildGizmo(world, target, origin, size);
                return;
            }
            int pi = 0;
            foreach (var s in _specScratch)
            {
                pi = SyncPartMesh(pi, s.Mesh0, s);
                if (pi < 0) { RebuildGizmo(world, target, origin, size); return; }
                pi = SyncPartMesh(pi, s.Mesh1, s);
                if (pi < 0) { RebuildGizmo(world, target, origin, size); return; }
            }
        }

        private void RebuildGizmo(World world, IWorldElement target, Vector3 origin, float size)
        {
            DestroyGizmo();

            var root = new Element("_EditorGizmo");
            root.AddComponent(new EditorTransientComponent());
            world.AddElement(root);
            _gizmoRoot = root;
            _gizmoWorld = world;

            TransformGizmo.BuildHandleSpecs(_mode, origin, size, _specScratch);
            foreach (var spec in _specScratch)
            {
                AddPartMesh(root, spec.Mesh0, spec);
                AddPartMesh(root, spec.Mesh1, spec);
            }
            _gizmoLastOrigin = origin;
            _gizmoLastSize = size;
            _gizmoBuiltTarget = target;
            _gizmoBuiltMode = _mode;
            Console.WriteLine($"[Viewport] gizmo spawned {_gizmoParts.Count} parts mode={_mode} target='{target.Name}' size={size:F2}");
        }

        private void DestroyGizmo()
        {
            if (_gizmoRoot == null)
            {
                _gizmoParts.Clear();
                return;
            }
            var world = _gizmoWorld ?? _root.GetWorldForElement(_gizmoRoot);
            if (world != null)
            {
                foreach (var part in _gizmoParts)
                    _gizmoRoot.RemoveChild(part.Element);
                world.RemoveElement(_gizmoRoot);
            }
            _gizmoRoot = null;
            _gizmoWorld = null;
            _gizmoParts.Clear();
            _gizmoBuiltTarget = null;
        }

        private void AddPartMesh(IWorldElement root, TransformGizmo.GizmoMeshPlacement placement,
            TransformGizmo.GizmoHandleSpec spec)
        {
            if (placement.Mesh == TransformGizmo.GizmoMeshKind.None) return;
            var el = new Element($"GizmoHandle_{spec.HandleId}");
            var mesh = CreatePartMesh(placement);
            el.AddComponent(mesh);
            var col = spec.Color;
            el.AddComponent(new MaterialComponent(col.R / 255f, col.G / 255f, col.B / 255f,
                col.A / 255f, 0f, 1f, unlit: true, noDepthTest: true));
            el.AddComponent(new GizmoHandleComponent(spec.HandleId));
            PlacePart(el, mesh, placement);
            root.AddChild(el);
            _gizmoParts.Add(new GizmoPart { Element = el, Mesh = mesh, Kind = placement.Mesh });
        }

        /// <summary>Refresh part <paramref name="pi"/> from a placement.
        /// Returns the next part index, or -1 when the part's mesh kind no
        /// longer matches (caller rebuilds).</summary>
        private int SyncPartMesh(int pi, TransformGizmo.GizmoMeshPlacement placement,
            TransformGizmo.GizmoHandleSpec spec)
        {
            if (placement.Mesh == TransformGizmo.GizmoMeshKind.None) return pi;
            if (pi < 0 || pi >= _gizmoParts.Count) return -1;
            var part = _gizmoParts[pi];
            if (part.Kind != placement.Mesh) return -1;
            PlacePart(part.Element, part.Mesh, placement);
            var col = spec.Color;
            var mat = part.Element.GetComponent<MaterialComponent>();
            if (mat != null)
            {
                float r = col.R / 255f, g = col.G / 255f, b = col.B / 255f, a = col.A / 255f;
                if (MathF.Abs(mat.R - r) > 0.002f) mat.R = r;
                if (MathF.Abs(mat.G - g) > 0.002f) mat.G = g;
                if (MathF.Abs(mat.B - b) > 0.002f) mat.B = b;
                if (MathF.Abs(mat.A - a) > 0.002f) mat.A = a;
            }
            return pi + 1;
        }

        /// <summary>Write a placement to both transform channels the hosts
        /// read: the element's LocalTransform (nova positions nodes by element
        /// world) and the component's own Position/Rotation (MonoGame draws by
        /// component Transform). The gizmo root sits at identity, so local is
        /// world here.</summary>
        private static void PlacePart(IWorldElement el, ComponentBase mesh,
            TransformGizmo.GizmoMeshPlacement p)
        {
            el.LocalTransform = new TRS { Position = p.Center, Rotation = p.Rotation, Scale = Vector3.One };
            switch (mesh)
            {
                case CylinderMesh c:
                    c.Position = p.Center; c.Rotation = p.Rotation;
                    break;
                case ConeMesh k:
                    k.Position = p.Center; k.Rotation = p.Rotation;
                    break;
                case BoxMesh b:
                    b.Position = p.Center; b.Rotation = p.Rotation;
                    break;
                case TorusSegmentMesh t:
                    t.Position = p.Center; t.Rotation = p.Rotation;
                    break;
            }
            SizePartMesh(mesh, p);
        }

        /// <summary>Write absolute dimensions (compared first: assigning
        /// regenerates the vertex cache, so static frames must not touch).</summary>
        private static void SizePartMesh(ComponentBase mesh, TransformGizmo.GizmoMeshPlacement p)
        {
            switch (mesh)
            {
                case CylinderMesh c:
                    float cr = p.Size.X * 0.5f;
                    if (MathF.Abs(c.Radius - cr) > 1e-6f) c.Radius = cr;
                    if (MathF.Abs(c.Height - p.Size.Y) > 1e-6f) c.Height = p.Size.Y;
                    break;
                case ConeMesh k:
                    float kr = p.Size.X * 0.5f;
                    if (MathF.Abs(k.Radius - kr) > 1e-6f) k.Radius = kr;
                    if (MathF.Abs(k.Height - p.Size.Y) > 1e-6f) k.Height = p.Size.Y;
                    break;
                case BoxMesh b:
                    if ((b.Size - p.Size).LengthSquared() > 1e-12f) b.Size = p.Size;
                    break;
                case TorusSegmentMesh t:
                    float rr = p.Size.X * 0.5f, tr = p.Size.Y * 0.5f;
                    if (MathF.Abs(t.RingRadius - rr) > 1e-6f) t.RingRadius = rr;
                    if (MathF.Abs(t.TubeRadius - tr) > 1e-6f) t.TubeRadius = tr;
                    break;
            }
        }

        private static ComponentBase CreatePartMesh(TransformGizmo.GizmoMeshPlacement p)
        {
            ComponentBase mesh = p.Mesh switch
            {
                TransformGizmo.GizmoMeshKind.Cylinder => new CylinderMesh(),
                TransformGizmo.GizmoMeshKind.Cone => new ConeMesh(),
                TransformGizmo.GizmoMeshKind.Cube => new BoxMesh(),
                TransformGizmo.GizmoMeshKind.Torus => new TorusSegmentMesh(),
                _ => throw new ArgumentOutOfRangeException(nameof(p), "No mesh for placement kind None"),
            };
            SizePartMesh(mesh, p);
            return mesh;
        }
    }
}
