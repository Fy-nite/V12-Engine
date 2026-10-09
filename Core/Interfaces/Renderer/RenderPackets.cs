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
        /// <summary>Hierarchy mirror data: every element in the rendered
        /// worlds (mesh owners and group ancestors alike). Renderers mirror
        /// the V12 tree from this and compose world matrices themselves —
        /// placement decisions live in V12, never in renderer code.</summary>
        public List<NodePlacement> Nodes = new();
        public List<SpriteDraw> Sprites = new();
        public List<TextDraw> Texts = new();

        //public List<LightData> Lights = new();
        //public List<CameraData> Cameras = new();
    }
    /// <summary>One element's place in the hierarchy: element-local matrix
    /// (no ancestors — see <see cref="V12.Core.ElementPlacement"/>).</summary>
    public struct NodePlacement
    {
        public long ElementId;
        public long ParentId;
        public Matrix4x4 Local;
    }
    public struct MeshDraw
    {
        public long ElementId;
        public IMeshRenderable Mesh;
        /// <summary>Viewport that owns this draw; 0 = main screen.</summary>
        public long ViewportId;
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
