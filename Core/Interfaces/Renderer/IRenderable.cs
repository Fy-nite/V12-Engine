using Assimp;
using Assimp.Unmanaged;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;
using V12.Core.Core.Interfaces;
using static System.Net.Mime.MediaTypeNames;

namespace V12.Core.Interfaces.Renderer
{
    public enum RenderType
    {
        PrimitiveModel,
        Mesh,
        Sprite,
        Text,
        ParticleSystem,
        Custom
    }

    public interface IRenderable : IComponent
    {
        public RenderType RenderType { get; }
    }

   

    public interface ITransformRenderable : IRenderable
    {
        public Matrix4x4 Transform { get; }
        public bool IsWorldLocked { get; }  // true = world space, false = body/hand-locked for XR
    }

    public interface IMeshRenderable : ITransformRenderable
    {
        public string Name { get; }
        public double[] MeshPoints { get; }
        public uint[] Indices { get; }
        public Material Material { get; }
    }

    public interface ISpriteRenderable : ITransformRenderable
    {
        public ITexture Texture { get; }
        public Vector2 Size { get; }
        public Color Tint { get; }
    }

    public interface ITextRenderable : ITransformRenderable
    {
        public string Text { get; }
        public Font Font { get; }
        public Color Color { get; }
        public float FontSize { get; }
    }
}
