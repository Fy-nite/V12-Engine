using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

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
        public Matrix4x4 Transform;
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
        public uint[] MeshIndices;

        // Material properties (PBR)
        public float MatR;
        public float MatG;
        public float MatB;
        public float MatA;
        public float MatMetallic;
        public float MatRoughness;
        public string MatTexturePath;

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
    }

    public struct AudioSourceSnapshot
    {
        public long OwnerElementId;
        public Vector3 Position;
        public float Volume;
        public float Pitch;
        public float MaxDistance;
        public bool IsPlaying;
        public string ClipPath;
    }

    public struct AudioListenerSnapshot
    {
        public Vector3 Position;
        public Vector3 Forward;
        public Vector3 Up;
        public bool HasValue;
    }

    public class FrameSnapshot
    {
        public List<RenderableSnapshot> Renderables = new();
        public List<AudioSourceSnapshot> AudioSources = new();
        public AudioListenerSnapshot Listener;
    }
}
