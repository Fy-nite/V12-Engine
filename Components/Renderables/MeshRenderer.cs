using Assimp;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components.Renderables
{
    public class MeshRenderer : ComponentBase, IMeshRenderable
    {
        public override string Name { get => base.Name; set => base.Name = value; }
        public MeshRenderer() { }
        public IMeshRenderable Mesh { get; set; }
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
                    else if (Mesh != null)
                    {
                        return scale * Mesh.Transform;
                    }
                }
                return Mesh?.Transform ?? Matrix4x4.Identity;
            }
        }

        public bool IsWorldLocked => Mesh?.IsWorldLocked ?? true;

        public RenderType RenderType => Mesh?.RenderType ?? RenderType.Mesh;

        public override IWorldElement BuildUI()
        {
            throw new NotImplementedException();
        }
    }
}
