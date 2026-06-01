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
        public double[] MeshPoints => Mesh.MeshPoints;

        public uint[] Indices => Mesh.Indices;

        public Material Material => Mesh.Material;

        public Matrix4x4 Transform
        {
            get
            {
                if (Owner != null)
                {
                    var t = Owner.GetComponent<TransformComponent>();
                    if (t != null)
                    {
                        return Matrix4x4.CreateFromYawPitchRoll(t.RY, t.RX, t.RZ)
                             * Matrix4x4.CreateTranslation(t.X, t.Y, t.Z);
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
