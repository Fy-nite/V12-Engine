using System.Numerics;

namespace V12.Core.Input
{
    public interface IVRInputProvider
    {
        Vector3 HeadPosition { get; }
        Quaternion HeadOrientation { get; }
        Vector3 LeftHandPosition { get; }
        Quaternion LeftHandOrientation { get; }
        Vector3 RightHandPosition { get; }
        Quaternion RightHandOrientation { get; }
        Vector3 WorldPosition { get; }
        float BodyYaw { get; }
    }
}
