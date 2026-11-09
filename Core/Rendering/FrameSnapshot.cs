using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using V12.Core.Interfaces.Renderer;

namespace V12.Core.Rendering
{
    public enum SnapshotNodeType
    {
        RawElement,
        LightPoint,
        LightDirectional,
        LightSpot,
        MeshBox,
        MeshSphere,
        MeshCapsule,
        MeshCylinder,
        MeshPlane,
        MeshCustom,
        Sprite,
        Svg,
        Text,
        Camera
    }

    public struct RenderableSnapshot
    {
        public long ElementId;
        public long ParentId;
        public string Name;
        public SnapshotNodeType NodeType;

        /// <summary>
        /// Id of the element whose <c>ViewportComponent</c> owns this renderable's
        /// viewport. 0 = main screen. Elements under a viewport-bearing element
        /// render inside that element's SubViewport instead of the main scene.
        /// </summary>
        public long ViewportId;

        public Matrix4x4 Transform;
        public Matrix4x4 LocalTransform;
        public bool HasLocalTransform;
        public bool IsWorldLocked;

        // Light properties
        public Color LightColor;
        public float LightIntensity;
        public float LightRange;
        public float LightAngle;
        public float LightSpotSoftness;

        // Mesh properties
        public float MeshWidth;
        public float MeshHeight;
        public float MeshDepth;
        public double[] MeshPoints;
        public double[]? MeshUvs;
        public double[]? MeshNormals;
        public double[]? MeshTangents;
        public uint[] MeshIndices;

        // Material properties (PBR)
        public float MatR;
        public float MatG;
        public float MatB;
        public float MatA;
        public bool MatUnlit;
        public bool MatNoDepth;
        public float MatMetallic;
        public float MatRoughness;
        public float MatEmissionR;
        public float MatEmissionG;
        public float MatEmissionB;
        public float MatEmissionEnergy;
        public float MatNormalScale;
        public float MatAlphaCutoff;
        public bool MatDoubleSided;
        public bool MatTriplanar;
        public bool MatTriplanarWorld;
        public float MatTriplanarSharpness;
        public string MatTexturePath;
        public string MatNormalTexturePath;
        public string MatMetallicTexturePath;
        public string MatRoughnessTexturePath;
        public string MatEmissionTexturePath;
        public string MatOcclusionTexturePath;
        public float MatUvOffsetX;
        public float MatUvOffsetY;
        public float MatUvScaleX;
        public float MatUvScaleY;

        // Sprite / SVG
        public string TextureSource;
        public float SizeX;
        public float SizeY;
        public Color Tint;

        // SVG
        public string SvgContent;

        // Text
        public string TextContent;
        public Color TextColor;
        public float FontSize;

        // Camera
        public float Fov;
        public float NearClip;
        public float FarClip;
        public bool IsCurrentCamera;
        public CameraProjection Projection;
        public float OrthoSize;

        // Whether the original V12 element has a ColliderComponent.
        // Used by renderers to decide whether to add physics shapes.
        public bool HasCollider;
    }

    public class FrameSnapshot
    {
        public List<RenderableSnapshot> Renderables = new();

        /// <summary>Ids of every viewport element that claimed world content this
        /// frame (ViewportComponent with RenderWorld=true). Renderers use this to
        /// keep a SubViewport alive for each declared viewport even when it has
        /// no children of its own.</summary>
        public HashSet<long> ActiveViewportIds = new();
    }
}
