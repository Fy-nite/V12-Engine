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
        private float _moveX, _moveY, _rotate;
        private bool _jumpRequested;
        private float _rotationSpeed = 2.0f; // Multiplier

        public LocomotionSystem(GameRoot gameRoot)
        {
            _gameRoot = gameRoot;
        }

        public void Initialize() 
        {
            _input = _gameRoot.Registry.Get<InputService>();
            _input?.RegisterHandler(this);
        }

        public void Initialize(GameRoot g)
        {
            _input = g.Registry.Get<InputService>();
            _input?.RegisterHandler(this);
        }

        public void OnInputEvent(InputEvent evt)
        {
            if (evt.Type == InputEventType.Axis)
            {
                if (evt.Name == "MoveX") { _moveX = (float)evt.Value; Console.WriteLine($"Input: MoveX={_moveX}"); }
                if (evt.Name == "MoveY") { _moveY = -(float)evt.Value; Console.WriteLine($"Input: MoveY={_moveY}"); }
                if (evt.Name == "Rotate") { _rotate = (float)evt.Value; Console.WriteLine($"Input: Rotate={_rotate}"); }
            }
            if (evt.Type == InputEventType.ButtonDown && evt.Name == "Jump")
            {
                _jumpRequested = true;
                Console.WriteLine("Input: JumpRequested");
            }
        }

        public void Update(float deltaTime)
        {
            var world = _gameRoot.SelectedWorld;
            if (world == null) return;
            
            var vrInput = _gameRoot.Registry.Get<IVRInputProvider>();

            foreach (var element in world.Root)
            {
                if (element == null) return;
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

                if (loco.Velocity.LengthSquared() > 0.001f)
                {
                    Console.WriteLine($"LocomotionSystem: {element.Name} Velocity set to: {loco.Velocity}");
                }
            }
        }
    }
}
