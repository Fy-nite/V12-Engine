using System.Collections.Generic;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Components;
using V12.Core.Input;
using System;

namespace V12.Core.Systems
{
    public class LocomotionSystem : IGameService, IInputHandler
    {
        private readonly GameRoot _gameRoot;
        private InputService? _input;

        // Input state tracked by handler
        private float _moveX, _moveY;
        private bool _jumpRequested;

        public LocomotionSystem(GameRoot gameRoot)
        {
            _gameRoot = gameRoot;
        }

        public void Initialize() 
        {
            _input = _gameRoot.Registry.Get<InputService>();
            _input?.RegisterHandler(this);
        }

        public void OnInputEvent(InputEvent evt)
        {
            if (evt.Type == InputEventType.Axis)
            {
                if (evt.Name == "MoveX") _moveX = (float)evt.Value;
                if (evt.Name == "MoveY") _moveY = -(float)evt.Value;
            }
            if (evt.Type == InputEventType.ButtonDown && evt.Name == "Jump")
            {
                _jumpRequested = true;
            }
        }

        public void Update(float deltaTime)
        {
            var world = _gameRoot.SelectedWorld;
            if (world == null) return;
            
            var vrInput = _gameRoot.Registry.Get<IVRInputProvider>();

            foreach (var element in world.Root)
            {
                var player = element.GetComponent<V12.Components.PlayerComponent>();
                if (player != null && element.GetComponent<LocomotionComponent>() == null)
                {
                    element.AddComponent(new LocomotionComponent());
                }

                var loco = element.GetComponent<LocomotionComponent>();
                var transform = element.GetComponent<TransformComponent>();
                if (loco == null || transform == null) continue;

                // Move relative to head orientation
                Vector3 forward = Vector3.UnitZ;
                Vector3 right = Vector3.UnitX;
                if (vrInput != null)
                {
                    forward = Vector3.Transform(Vector3.UnitZ, vrInput.HeadOrientation);
                    forward.Y = 0;
                    forward = Vector3.Normalize(forward);
                    right = Vector3.Transform(Vector3.UnitX, vrInput.HeadOrientation);
                    right.Y = 0;
                    right = Vector3.Normalize(right);
                }

                Vector3 moveDir = (forward * _moveY + right * _moveX) * loco.MoveSpeed;
                loco.Velocity = new Vector3(moveDir.X, loco.Velocity.Y, moveDir.Z);

                // Apply gravity
                if (!loco.IsGrounded)
                {
                    loco.Velocity -= new Vector3(0, loco.Gravity * deltaTime, 0);
                }
                else
                {
                    if (_jumpRequested)
                    {
                        loco.Velocity = new Vector3(loco.Velocity.X, loco.JumpStrength, loco.Velocity.Z);
                        loco.IsGrounded = false;
                    }
                    else
                    {
                        loco.Velocity = new Vector3(loco.Velocity.X, 0, loco.Velocity.Z);
                    }
                }
                
                _jumpRequested = false;

                // Integrate
                Vector3 pos = new Vector3(transform.X, transform.Y, transform.Z);
                pos += loco.Velocity * deltaTime;
                
                // Ground check
                if (pos.Y < 0)
                {
                    pos.Y = 0;
                    loco.IsGrounded = true;
                    loco.Velocity = new Vector3(loco.Velocity.X, 0, loco.Velocity.Z);
                }
                
                transform.X = pos.X;
                transform.Y = pos.Y;
                transform.Z = pos.Z;
            }
        }
    }
}
