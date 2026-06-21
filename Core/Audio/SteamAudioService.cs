using System;
using System.Collections.Generic;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Core.Registry;
using IPL = SteamAudio.IPL;

namespace V12.Core.Audio
{
    public class SteamAudioService : IGameService
    {
        private IPL.Context _context;
        private IPL.Simulator _simulator;
        private bool _initialized;
        private GameRoot _gameRoot;
        private readonly Dictionary<IAudioSource, IPL.Source> _sources = new Dictionary<IAudioSource, IPL.Source>();
        private readonly Dictionary<IAudioSource, IPL.SimulationOutputs> _sourceOutputs = new Dictionary<IAudioSource, IPL.SimulationOutputs>();

        public bool IsInitialized => _initialized;

        public bool TryGetSourceGain(IAudioSource source, out float gain)
        {
            gain = 1f;
            if (!_initialized || !_sourceOutputs.TryGetValue(source, out var outputs))
                return false;
            var d = outputs.Direct;
            if ((d.Flags & IPL.DirectEffectFlags.ApplyDistanceAttenuation) != 0)
                gain *= Math.Max(0f, d.DistanceAttenuation);
            if ((d.Flags & IPL.DirectEffectFlags.ApplyOcclusion) != 0)
                gain *= Math.Max(0f, d.Occlusion);
            if ((d.Flags & IPL.DirectEffectFlags.ApplyDirectivity) != 0)
                gain *= Math.Max(0f, d.Directivity);
            gain = Math.Clamp(gain, 0f, 1f);
            return true;
        }

        public void Initialize(GameRoot g)
        {
            _gameRoot = g;
            try
            {
                var ctxSettings = new IPL.ContextSettings
                {
                    Version = 0x040601,
                    LogCallback = null,
                    AllocateCallback = null,
                    FreeCallback = null,
                    SimdLevel = IPL.SimdLevel.Avx2,
                    Flags = IPL.ContextFlags.Validation
                };

                var result = IPL.ContextCreate(ctxSettings, out _context);
                if (result != IPL.Error.Success)
                {
                    Console.WriteLine($"[SteamAudio] ContextCreate failed: {result}");
                    return;
                }

                var audioSettings = new IPL.AudioSettings
                {
                    SamplingRate = 48000,
                    FrameSize = 1024
                };

                var simSettings = new IPL.SimulationSettings
                {
                    Flags = IPL.SimulationFlags.Direct,
                    SceneType = IPL.SceneType.Default,
                    ReflectionType = IPL.ReflectionEffectType.Parametric,
                    MaxNumOcclusionSamples = 16,
                    MaxNumRays = 1024,
                    NumDiffuseSamples = 256,
                    MaxDuration = 1.0f,
                    MaxOrder = 1,
                    MaxNumSources = 64,
                    NumThreads = 8,
                    RayBatchSize = 16,
                    NumVisSamples = 8,
                    SamplingRate = audioSettings.SamplingRate,
                    FrameSize = audioSettings.FrameSize,
                    OpenCLDevice = default,
                    RadeonRaysDevice = default,
                    TanDevice = default
                };

                result = IPL.SimulatorCreate(_context, simSettings, out _simulator);
                if (result != IPL.Error.Success)
                {
                    Console.WriteLine($"[SteamAudio] SimulatorCreate failed: {result}");
                    IPL.ContextRelease(ref _context);
                    return;
                }

                _initialized = true;
                Console.WriteLine("[SteamAudio] Initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SteamAudio] Initialization failed: {ex.GetType().Name}: {ex.Message}");
                _initialized = false;
            }
        }

        public void Update(float deltaTime)
        {
            if (!_initialized) return;
            try
            {
                SyncComponents();
                if (_sources.Count == 0) return;

                var listener = FindListener();
                var sharedInputs = default(IPL.SimulationSharedInputs);
                sharedInputs.Listener = ToCoordinateSpace3(
                    listener?.Position ?? Vector3.Zero,
                    listener?.Forward ?? -Vector3.UnitZ,
                    listener?.Up ?? Vector3.UnitY);
                sharedInputs.NumRays = 0;
                sharedInputs.NumBounces = 0;
                sharedInputs.Duration = 0f;
                sharedInputs.Order = 0;
                sharedInputs.IrradianceMinDistance = 0f;
                sharedInputs.PathingVisCallback = null;
                sharedInputs.PathingUserData = IntPtr.Zero;
                IPL.SimulatorSetSharedInputs(_simulator, IPL.SimulationFlags.Direct, sharedInputs);

                foreach (var kvp in _sources)
                {
                    var source = kvp.Key;
                    var handle = kvp.Value;

                    var inputs = default(IPL.SimulationInputs);
                    inputs.Flags = IPL.SimulationFlags.Direct;
                    inputs.DirectFlags = IPL.DirectSimulationFlags.DistanceAttenuation | IPL.DirectSimulationFlags.Occlusion;
                    inputs.Source = ToCoordinateSpace3(source.Position, -Vector3.UnitZ, Vector3.UnitY);
                    inputs.DistanceAttenuationModel = new IPL.DistanceAttenuationModel
                    {
                        Type = IPL.DistanceAttenuationModelType.Default,
                        MinDistance = 1.0f
                    };
                    inputs.AirAbsorptionModel = default;
                    inputs.Directivity = default;
                    inputs.OcclusionType = IPL.OcclusionType.Raycast;
                    inputs.OcclusionRadius = 0.1f;
                    inputs.NumOcclusionSamples = 4;
                    inputs.HybridReverbTransitionTime = 1.0f;
                    inputs.HybridReverbOverlapPercent = 25;
                    inputs.Baked = false;
                    inputs.BakedDataIdentifier = default;
                    inputs.PathingProbes = default;
                    inputs.VisRadius = 0f;
                    inputs.VisThreshold = 0f;
                    inputs.VisRange = 0f;
                    inputs.PathingOrder = 0;
                    inputs.EnableValidation = false;
                    inputs.FindAlternatePaths = false;
                    inputs.NumTransmissionRays = 0;

                    IPL.SourceSetInputs(handle, IPL.SimulationFlags.Direct, inputs);
                }

                IPL.SimulatorRunDirect(_simulator);

                foreach (var kvp in _sources)
                {
                    IPL.SourceGetOutputs(kvp.Value, IPL.SimulationFlags.Direct, out var outputs);
                    _sourceOutputs[kvp.Key] = outputs;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SteamAudio] Update error: {ex.Message}");
            }
        }

        public void Update(GameRoot gameRoot) => Update(0);

        private void SyncComponents()
        {
            if (_gameRoot?.SelectedWorld == null) return;

            var worldSources = new HashSet<IAudioSource>();

            void Collect(IWorldElement element)
            {
                if (element?.Components == null) return;
                foreach (var c in element.Components.ToArray())
                {
                    if (c is IAudioSource src)
                        worldSources.Add(src);
                }
                if (element.Children != null)
                    foreach (var child in element.Children.ToArray())
                        Collect(child);
            }

            foreach (var root in _gameRoot.SelectedWorld.Root.ToArray())
                Collect(root);

            foreach (var src in worldSources)
            {
                if (!_sources.ContainsKey(src))
                    CreateSourceHandle(src);
            }

            var toRemove = new List<IAudioSource>();
            foreach (var src in _sources.Keys)
                if (!worldSources.Contains(src))
                    toRemove.Add(src);
            foreach (var src in toRemove)
                DestroySourceHandle(src);
        }

        private IAudioListener FindListener()
        {
            if (_gameRoot?.SelectedWorld == null) return null;
            foreach (var root in _gameRoot.SelectedWorld.Root.ToArray())
            {
                var found = FindListenerRecursive(root);
                if (found != null) return found;
            }
            return null;
        }

        private static IAudioListener FindListenerRecursive(IWorldElement element)
        {
            if (element?.Components == null) return null;
            foreach (var c in element.Components.ToArray())
                if (c is IAudioListener listener)
                    return listener;
            if (element.Children != null)
                foreach (var child in element.Children.ToArray())
                {
                    var found = FindListenerRecursive(child);
                    if (found != null) return found;
                }
            return null;
        }

        private void CreateSourceHandle(IAudioSource source)
        {
            try
            {
                var settings = new IPL.SourceSettings
                {
                    Flags = IPL.SimulationFlags.Direct
                };
                var result = IPL.SourceCreate(_simulator, settings, out IPL.Source handle);
                if (result == IPL.Error.Success)
                {
                    IPL.SourceAdd(handle, _simulator);
                    _sources[source] = handle;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SteamAudio] CreateSourceHandle error: {ex.Message}");
            }
        }

        private void DestroySourceHandle(IAudioSource source)
        {
            try
            {
                if (_sources.TryGetValue(source, out var handle))
                {
                    IPL.SourceRemove(handle, _simulator);
                    IPL.SourceRelease(ref handle);
                    _sources.Remove(source);
                    _sourceOutputs.Remove(source);
                }
            }
            catch { }
        }

        public void Shutdown()
        {
            if (!_initialized) return;
            try
            {
                foreach (var handle in _sources.Values)
                {
                    IPL.SourceRemove(handle, _simulator);
                    var h = handle;
                    IPL.SourceRelease(ref h);
                }
                _sources.Clear();

                IPL.SimulatorRelease(ref _simulator);
                IPL.ContextRelease(ref _context);
                _initialized = false;
                Console.WriteLine("[SteamAudio] Shutdown");
            }
            catch { }
        }

        private static IPL.CoordinateSpace3 ToCoordinateSpace3(Vector3 origin, Vector3 ahead, Vector3 up)
        {
            var forward = Vector3.Normalize(ahead);
            var upVec = Vector3.Normalize(up);
            var right = Vector3.Normalize(Vector3.Cross(forward, upVec));
            return new IPL.CoordinateSpace3
            {
                Origin = new IPL.Vector3 { X = origin.X, Y = origin.Y, Z = origin.Z },
                Ahead = new IPL.Vector3 { X = forward.X, Y = forward.Y, Z = forward.Z },
                Up = new IPL.Vector3 { X = upVec.X, Y = upVec.Y, Z = upVec.Z },
                Right = new IPL.Vector3 { X = right.X, Y = right.Y, Z = right.Z }
            };
        }
    }
}
