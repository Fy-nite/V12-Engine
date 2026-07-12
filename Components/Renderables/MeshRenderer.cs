using Assimp;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components.Renderables
{
    public class MeshRenderer : ComponentBase, IMeshRenderable
    {
        public override string Name { get => base.Name; set => base.Name = value; }
        public MeshRenderer() { }

        private IMeshRenderable _mesh;

        /// <summary>
        /// Name of the target MeshComponent on the same element. Set automatically when
        /// <see cref="Mesh"/> is assigned (e.g. during XML parsing). Used after network
        /// deserialization to re-link the reference since <see cref="Mesh"/> is [BsonIgnore]d.
        /// </summary>
        public string? MeshTargetName { get; set; }

        /// <summary>
        /// Reference to a MeshComponent on the same element. Marked BsonIgnore because
        /// BSON cannot serialize interface-typed properties, and the MeshComponent is
        /// already serialized independently in the element's Components list.
        /// After deserialization, <see cref="OnAttach"/> restores this reference.
        /// </summary>
        [BsonIgnore]
        public IMeshRenderable Mesh
        {
            get => _mesh;
            set
            {
                _mesh = value;
                // Capture the target component name for network round-trip
                if (value is IComponent comp && !string.IsNullOrEmpty(comp.Name))
                    MeshTargetName = comp.Name;
            }
        }

        /// <summary>
        /// Restore the Mesh reference after network deserialization by finding the
        /// matching MeshComponent on the same element. Uses <see cref="MeshTargetName"/>
        /// (set automatically when Mesh was assigned on the server) to locate the
        /// correct component. Falls back to any non-MeshRenderer IMeshRenderable.
        /// </summary>
        public override void OnAttach(IWorldElement element)
        {
            base.OnAttach(element);
            if (Mesh != null || Owner == null) return;

            foreach (var comp in Owner.Components)
            {
                if (comp is IMeshRenderable mr && comp is not MeshRenderer)
                {
                    // Prefer exact name match from MeshTargetName
                    if (!string.IsNullOrEmpty(MeshTargetName)
                        && string.Equals(comp.Name, MeshTargetName, StringComparison.OrdinalIgnoreCase))
                    {
                        Mesh = mr;
                        break;
                    }
                    // Fallback: first available non-MeshRenderer IMeshRenderable
                    if (Mesh == null)
                        Mesh = mr;
                }
            }
        }

        public double[] MeshPoints => Mesh?.MeshPoints ?? Array.Empty<double>();

        public uint[] Indices => Mesh?.Indices ?? Array.Empty<uint>();

        public Material Material => Mesh?.Material;

        public Matrix4x4 Transform
        {
            get
            {
                if (Owner != null)
                {
                    var t = Owner.GetComponent<TransformComponent>();
                    var s = Owner.GetComponent<ScaleComponent>();
                    
                    Matrix4x4 scale = s != null ? Matrix4x4.CreateScale(s.ScaleX, s.ScaleY, s.ScaleZ) : Matrix4x4.Identity;
                    
                    if (t != null)
                    {
                        return scale
                             * Matrix4x4.CreateFromYawPitchRoll(t.RY, t.RX, t.RZ)
                             * Matrix4x4.CreateTranslation(t.X, t.Y, t.Z);
                    }
                    // Fallback: use Element's LocalTransform
                    var lt = Owner.LocalTransform;
                    return scale
                         * Matrix4x4.CreateFromQuaternion(lt.Rotation)
                         * Matrix4x4.CreateTranslation(lt.Position);
                }
                return Mesh?.Transform ?? Matrix4x4.Identity;
            }
        }

        public bool IsWorldLocked => Mesh?.IsWorldLocked ?? true;

        public RenderType RenderType => Mesh?.RenderType ?? RenderType.Mesh;

        [BsonIgnore]
        public TRS LocalTransform => throw new NotImplementedException();

        [BsonIgnore]
        public Matrix4x4 WorldTransform => throw new NotImplementedException();

        public override IWorldElement BuildUI()
        {
            throw new NotImplementedException();
        }
    }
}
