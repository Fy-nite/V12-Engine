using System.Numerics;

namespace V12.Core.Input
{
    public interface IVRInputProvider
    {
        Vector3 HeadPosition { get; set; }
        Quaternion HeadOrientation { get; set; }
        Vector3 LeftHandPosition { get; set; }
        Quaternion LeftHandOrientation { get; set; }
        Vector3 RightHandPosition { get; set; }
        Quaternion RightHandOrientation { get; set; }
    }

    public class VRInputProvider : IVRInputProvider
    {
        public Vector3 HeadPosition { get; set; }
        public Quaternion HeadOrientation { get; set; }
        public Vector3 LeftHandPosition { get; set; }
        public Quaternion LeftHandOrientation { get; set; }
        public Vector3 RightHandPosition { get; set; }
        public Quaternion RightHandOrientation { get; set; }
    }
}
