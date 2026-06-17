using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;
using V12.Core.Core.Interfaces;

namespace V12.Core.Interfaces.Renderer
{
    public class RenderPacket
    {
        public List<MeshDraw> Meshes = new();
        public List<SpriteDraw> Sprites = new();
        public List<TextDraw> Texts = new();

        public List<LightData> Lights = new();
        public List<CameraData> Cameras = new();
    }
    public struct MeshDraw
    {
        public Matrix4x4 Transform;
        public IMeshRenderable Mesh;
    }
    public struct SpriteDraw
    {
        public Matrix4x4 Transform;
        public ITexture Texture;
        public Vector2 Size;
        public Color Tint;
    }
    public struct TextDraw
    {
        public Matrix4x4 Transform;
        public string Text;
        public string FontName;
        public Color Color;
        public float Size;
    }
}
