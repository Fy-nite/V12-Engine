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
    }

    public class VRInputProvider : IVRInputProvider
    {
        private readonly object _lock = new();
        private Vector3 _headPosition;
        private Quaternion _headOrientation;
        private Vector3 _leftHandPosition;
        private Quaternion _leftHandOrientation;
        private Vector3 _rightHandPosition;
        private Quaternion _rightHandOrientation;

        public object SyncRoot => _lock;

        public Vector3 HeadPosition { get { lock (_lock) return _headPosition; } }
        public Quaternion HeadOrientation { get { lock (_lock) return _headOrientation; } }
        public Vector3 LeftHandPosition { get { lock (_lock) return _leftHandPosition; } }
        public Quaternion LeftHandOrientation { get { lock (_lock) return _leftHandOrientation; } }
        public Vector3 RightHandPosition { get { lock (_lock) return _rightHandPosition; } }
        public Quaternion RightHandOrientation { get { lock (_lock) return _rightHandOrientation; } }

        public void SetHeadPose(Vector3 pos, Quaternion rot) { lock (_lock) { _headPosition = pos; _headOrientation = rot; } }
        public void SetLeftHandPose(Vector3 pos, Quaternion rot) { lock (_lock) { _leftHandPosition = pos; _leftHandOrientation = rot; } }
        public void SetRightHandPose(Vector3 pos, Quaternion rot) { lock (_lock) { _rightHandPosition = pos; _rightHandOrientation = rot; } }
    }
}
