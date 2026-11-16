using System.Numerics;

namespace V12.Core.Interfaces.Renderer;

/// <summary>
/// A mesh whose vertices are skinned by a shared skeleton. The bone order here
/// is the skeleton's bone order, and <see cref="BoneIndices"/> is four indices
/// per vertex into that order.
///
/// The pose array is updated in place by the owning model component, so a
/// snapshot can reference it without copying. <see cref="InverseBindPoses"/> and
/// <see cref="BoneParents"/> are static for the life of the skeleton.
/// </summary>
public interface ISkinnedMeshRenderable : IMeshRenderable
{
    /// <summary>Element id of the skeleton owner. Parts sharing it share a skin.</summary>
    long SkeletonId { get; }

    /// <summary>World transform of the skeleton root.</summary>
    Matrix4x4 SkeletonWorld { get; }

    /// <summary>Parent bone index per bone, or -1 for a root.</summary>
    int[] BoneParents { get; }

    /// <summary>Current parent-space (local) transform per bone.</summary>
    Matrix4x4[] BoneLocals { get; }

    /// <summary>Inverse bind pose per bone, in skeleton space.</summary>
    Matrix4x4[] InverseBindPoses { get; }

    /// <summary>Four skin weights per vertex.</summary>
    float[] BoneWeights { get; }

    /// <summary>Four bone indices per vertex.</summary>
    int[] BoneIndices { get; }
}
