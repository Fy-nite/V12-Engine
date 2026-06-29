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


    public struct TRS
    {
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector3 Scale = Vector3.One;
        
        public TRS(Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null)
        {
            Position = position ?? Vector3.Zero;
            Rotation = rotation ?? Quaternion.Identity;
            Scale = scale ?? Vector3.One;
        }
    }
    public enum RenderType
    {
        PrimitiveModel,
        RawElement,
        Mesh,
        Sprite,
        Text,
        Svg,
        ParticleSystem,
        Light,
        Custom
    }

    public interface IRenderable : IComponent, ISpatial
    {
        public RenderType RenderType { get; }
    }
    public enum LightType
    {
        Point,
        Directional,
        Spot,
        AreaLight
    }
    public interface ICameraRenderable : ITransformRenderable
    {
        public float FieldOfView { get; }
        public float AspectRatio { get; }
        public float NearClip { get; }
        public float FarClip { get; }
        public bool IsCurrent { get; }
    }
    public interface ILightRenderable : ITransformRenderable
    {
        public Color Color { get; }
        public float Intensity { get; }
        public float Range { get; }
        public LightType Type { get; }
        public float Angle { get; }
        public float SpotSoftness { get; }
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

    public interface ISvgRenderable : ITransformRenderable
    {
        public string SvgContent { get; }
        public Vector2 Size { get; }
        public Color Tint { get; }
    }
}
