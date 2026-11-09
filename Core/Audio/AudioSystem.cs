using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using V12.Audio;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Core.Audio;

/// <summary>
/// Bridges the game world to the backend-free audio engine. It walks the active worlds,
/// maps every <see cref="IAudioSource"/> to a mixer channel, computes distance/pan from
/// the listener, and keeps the channel state in step with the component.
/// </summary>
public sealed class AudioSystem : IGameService
{
    private sealed class Binding
    {
        public IAudioChannel Channel = null!;
        public string ResolvedPath = string.Empty;
        public string? FailedPath;
        public string MixerChannelName = string.Empty;
        public long LastSeenFrame;
        public bool WasPlaying;
    }

    private readonly Dictionary<long, Binding> _bindings = new();
    private readonly Dictionary<string, IAudioClip> _clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AudioSourceBus, IAudioBus> _buses = new();
    private readonly Dictionary<AudioSourceBus, float> _busVolumes = new();
    private readonly Dictionary<string, IAudioMixerChannel> _mixerChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IAudioBusSource> _busSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IAudioBus> _namedBuses = new(StringComparer.OrdinalIgnoreCase);

    private GameRoot? _root;
    private IAudioEngine? _engine;
    private long _frame;
    private bool _busesCreated;
    private bool _loggedFirstChannel;

    public IAudioEngine? Engine => _engine;

    public void Initialize(GameRoot g)
    {
        _root = g;
        _engine = g.Registry.Get<IAudioEngine>("IAudioEngine") ?? g.Registry.Get<IAudioEngine>();

        if (_engine is null)
        {
            _engine = new NullAudioEngine();
            g.Registry.Register("IAudioEngine", _engine);
        }

        _ = StartEngineAsync(_engine);
    }

    private static async Task StartEngineAsync(IAudioEngine engine)
    {
        try
        {
            await engine.StartAsync().ConfigureAwait(false);

            if (engine.IsRunning)
            {
                var info = engine.Info;
                GameRoot.Log?.Information(
                    "[AudioSystem] Audio engine running on {Backend} / {Device} ({Rate} Hz, {Channels} ch)",
                    info.BackendName ?? "unknown backend",
                    info.DeviceName ?? "default device",
                    info.SampleRate,
                    info.Channels);
            }
        }
        catch (Exception ex)
        {
            GameRoot.Log?.Error(ex, "[AudioSystem] Audio engine failed to start");
        }
    }

    public void Update(float deltaTime) => Update(_root);

    public void Update(GameRoot gameRoot)
    {
        if (gameRoot is null)
            return;

        var engine = _engine;
        if (engine is null || !engine.IsRunning)
            return;

        _frame++;

        if (!_busesCreated)
            CreateBuses(engine);

        var listener = FindListener(gameRoot);

        foreach (var world in gameRoot.ActiveWorlds)
        {
            foreach (var root in world.Root.ToArray())
                Visit(root, listener);
        }

        if (_frame % 120 == 0)
            CleanupStaleBindings();
    }

    private void CreateBuses(IAudioEngine engine)
    {
        foreach (AudioSourceBus bus in Enum.GetValues<AudioSourceBus>())
        {
            if (bus == AudioSourceBus.Master)
                continue;

            _buses[bus] = engine.CreateBus(new AudioBusDesc
            {
                Name = bus.ToString(),
                Gain = _busVolumes.TryGetValue(bus, out float gain) ? gain : 1f,
            });

            _namedBuses[bus.ToString()] = _buses[bus];
        }

        _busesCreated = true;
    }

    private void Visit(IWorldElement element, ListenerState listener)
    {
        if (element?.Components is null)
            return;

        foreach (var component in element.Components.ToArray())
        {
            if (component is IAudioSource source)
                UpdateSource(source, listener);
        }

        if (element.Children is null)
            return;

        foreach (var child in element.Children.ToArray())
            Visit(child, listener);
    }

    private void UpdateSource(IAudioSource source, ListenerState listener)
    {
        var engine = _engine;
        if (engine is null)
            return;

        // Do not allocate a native track for a source that has never played. Once a
        // binding exists it is kept for reuse, so stop/play cycles stay cheap.
        if (!source.IsPlaying && !source.IsPaused && !_bindings.ContainsKey(source.Id))
            return;

        string clipPath = source.AudioClipPath;
        if (string.IsNullOrWhiteSpace(clipPath))
        {
            if (_bindings.TryGetValue(source.Id, out var emptyBinding) && emptyBinding.Channel is not null)
            {
                emptyBinding.Channel.Stop();
                emptyBinding.LastSeenFrame = _frame;
            }
            return;
        }

        string resolvedPath = V12AssetResolver.ResolveGlobal(clipPath);

        var mixer = GetMixerChannel(source.MixerChannel);
        string busName = string.IsNullOrWhiteSpace(mixer?.BusName)
            ? source.Bus.ToString()
            : mixer!.BusName;
        var targetBus = GetBus(busName);

        if (!_bindings.TryGetValue(source.Id, out var binding))
        {
            binding = new Binding();
            _bindings[source.Id] = binding;
        }

        if (binding.Channel is null || !string.Equals(binding.ResolvedPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(binding.FailedPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
            {
                binding.LastSeenFrame = _frame;
                return;
            }

            try
            {
                if (!_clips.TryGetValue(resolvedPath, out var clip))
                {
                    clip = engine.CreateClip(new AudioClipDesc
                    {
                        Path = resolvedPath,
                        Kind = AudioClipKind.File,
                        Preload = false,
                    });
                    _clips[resolvedPath] = clip;
                }

                if (binding.Channel is null)
                {
                    binding.Channel = engine.CreateChannel(new AudioChannelDesc
                    {
                        Name = source.GetType().Name,
                        Kind = AudioChannelKind.File,
                        Clip = clip.Handle,
                        Bus = targetBus?.Handle ?? default,
                        Loop = source.Loop,
                        Gain = 1f,
                        Pan = source.Pan,
                        Priority = source.Priority,
                    });

                    if (!_loggedFirstChannel)
                    {
                        _loggedFirstChannel = true;
                        GameRoot.Log?.Information(
                            "[AudioSystem] First audio channel opened: {Path}",
                            resolvedPath);
                    }
                }
                else
                {
                    binding.Channel.Clip = clip;
                }

                binding.ResolvedPath = resolvedPath;
                binding.FailedPath = null;
            }
            catch (Exception ex)
            {
                binding.FailedPath = resolvedPath;
                GameRoot.Log?.Error(ex, $"[AudioSystem] Could not open audio clip '{resolvedPath}'");
                return;
            }
        }

        binding.LastSeenFrame = _frame;
        binding.MixerChannelName = mixer?.MixerName ?? string.Empty;

        if (!ReferenceEquals(binding.Channel.Bus, targetBus))
            binding.Channel.Bus = targetBus;

        binding.Channel.Loop = source.Loop;

        float gain = source.Volume;
        float pan = source.Pan;

        if (listener.HasValue && source.SpatialBlend > 0f)
        {
            var toSource = source.Position - listener.Position;
            float distance = toSource.Length();
            gain *= Attenuate(source.Attenuation, distance, source.MinDistance, source.MaxDistance);

            if (distance > 0.0001f)
            {
                var direction = toSource / distance;
                float spatialPan = Vector3.Dot(direction, listener.GetRight());
                pan = Math.Clamp(pan + spatialPan * source.SpatialBlend, -1f, 1f);
            }
        }

        if (mixer is not null)
        {
            gain *= mixer.Gain;
            pan = Math.Clamp(pan + mixer.Pan, -1f, 1f);
        }

        binding.Channel.Muted = mixer?.Muted ?? false;
        binding.Channel.Solo = mixer?.Solo ?? false;
        binding.Channel.Gain = Math.Clamp(gain, 0f, 1f);
        binding.Channel.Pan = Math.Clamp(pan, -1f, 1f);
        binding.Channel.Tempo = Math.Clamp(source.Pitch, 0.25f, 4f);
        binding.Channel.PitchSemitones = 0f;

        bool playing = source.IsPlaying && !source.IsPaused;

        if (playing)
        {
            if (!binding.WasPlaying)
            {
                // Rising edge: a fresh Play() after the source ended or was paused.
                // Rewind a finished channel so a one-shot can fire again.
                if (binding.Channel.IsFinished)
                    binding.Channel.Seek(0);

                if (binding.Channel.State != AudioPlaybackState.Playing)
                    binding.Channel.Play();
            }
            else if (binding.Channel.IsFinished)
            {
                // It ran to the end while the component still said "playing".
                // Stop and rewind the channel; a playlist listener advances itself.
                if (source is IAudioPlaybackListener playbackListener)
                {
                    binding.Channel.Stop();
                    binding.Channel.Seek(0);
                    playbackListener.OnAudioEnded();
                }
                else
                {
                    source.Stop();
                    binding.Channel.Stop();
                    binding.Channel.Seek(0);
                }
            }
            else if (binding.Channel.State != AudioPlaybackState.Playing)
            {
                binding.Channel.Play();
            }
        }
        else if (source.IsPlaying && source.IsPaused)
        {
            if (binding.Channel.State != AudioPlaybackState.Paused)
                binding.Channel.Pause();
        }
        else if (binding.Channel.State != AudioPlaybackState.Stopped)
        {
            binding.Channel.Stop();
        }

        binding.WasPlaying = source.IsPlaying && !source.IsPaused;
    }

    private IAudioBus? ResolveBus(AudioSourceBus bus)
        => _buses.TryGetValue(bus, out var value) ? value : null;

    /// <summary>Registers a scene-authored mixer channel strip.</summary>
    public void RegisterMixerChannel(IAudioMixerChannel channel)
    {
        if (string.IsNullOrWhiteSpace(channel.MixerName))
            return;
        _mixerChannels[channel.MixerName] = channel;
    }

    public void UnregisterMixerChannel(IAudioMixerChannel channel)
        => UnregisterMixerChannel(channel.MixerName);

    public void UnregisterMixerChannel(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            _mixerChannels.Remove(name);
    }

    public IAudioMixerChannel? GetMixerChannel(string? name)
        => !string.IsNullOrWhiteSpace(name) && _mixerChannels.TryGetValue(name, out var channel)
            ? channel
            : null;

    /// <summary>Registers a scene-authored bus and creates it on the engine.</summary>
    public void RegisterBus(IAudioBusSource bus)
    {
        if (string.IsNullOrWhiteSpace(bus.BusName))
            return;

        _busSources[bus.BusName] = bus;
        EnsureBus(bus.BusName, bus.ParentBusName);
    }

    public void UnregisterBus(IAudioBusSource bus)
        => UnregisterBus(bus.BusName);

    public void UnregisterBus(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            _busSources.Remove(name);
    }

    /// <summary>
    /// Resolves a bus by name, creating it on first use. "Master" and an empty name
    /// mean the master bus directly.
    /// </summary>
    public IAudioBus? GetBus(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "Master", StringComparison.OrdinalIgnoreCase))
            return null;

        if (_namedBuses.TryGetValue(name, out var existing))
            return existing;

        return EnsureBus(name, null);
    }

    private IAudioBus? EnsureBus(string name, string? parentName)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "Master", StringComparison.OrdinalIgnoreCase))
            return null;

        if (_namedBuses.TryGetValue(name, out var existing))
            return existing;

        if (Enum.TryParse<AudioSourceBus>(name, ignoreCase: true, out var builtInName) &&
            _buses.TryGetValue(builtInName, out var builtIn))
        {
            _namedBuses[name] = builtIn;
            return builtIn;
        }

        var engine = _engine;
        if (engine is null || !engine.IsRunning)
            return null;

        var parent = EnsureBus(
            string.IsNullOrWhiteSpace(parentName) ? "Master" : parentName!,
            null);

        float gain = _busSources.TryGetValue(name, out var source) ? source.Gain : 1f;
        var bus = engine.CreateBus(new AudioBusDesc
        {
            Name = name,
            Parent = parent?.Handle ?? default,
            Gain = gain,
        });

        _namedBuses[name] = bus;
        return bus;
    }

    /// <summary>Finds every live source channel routed to a mixer channel name.</summary>
    public void GetChannelsForMixerChannel(string name, List<IAudioChannel> results)
    {
        results.Clear();
        if (string.IsNullOrWhiteSpace(name))
            return;

        foreach (var binding in _bindings.Values)
        {
            if (binding.Channel is null)
                continue;
            if (!string.Equals(binding.MixerChannelName, name, StringComparison.OrdinalIgnoreCase))
                continue;
            results.Add(binding.Channel);
        }
    }

    /// <summary>
    /// Finds the live mixer channel for a source component. Components such as
    /// <c>AudioEffectComponent</c> use this to attach per-source effects.
    /// </summary>
    public bool TryGetChannel(long sourceId, out IAudioChannel channel)
    {
        if (_bindings.TryGetValue(sourceId, out var binding) && binding.Channel is not null)
        {
            channel = binding.Channel;
            return true;
        }

        channel = null!;
        return false;
    }

    public bool TryGetChannel(IAudioSource source, out IAudioChannel channel)
        => TryGetChannel(source.Id, out channel);

    private static float Attenuate(
        AudioAttenuationModel model,
        float distance,
        float minDistance,
        float maxDistance)
    {
        if (model == AudioAttenuationModel.None)
            return 1f;

        if (distance <= minDistance)
            return 1f;

        if (maxDistance <= minDistance || distance >= maxDistance)
            return 0f;

        return model switch
        {
            AudioAttenuationModel.Linear =>
                1f - ((distance - minDistance) / (maxDistance - minDistance)),
            AudioAttenuationModel.InverseDistance =>
                minDistance / distance,
            AudioAttenuationModel.Exponential =>
                MathF.Pow(minDistance / distance, 2f),
            _ => 1f,
        };
    }

    private void CleanupStaleBindings()
    {
        List<long>? stale = null;

        foreach (var pair in _bindings)
        {
            if (_frame - pair.Value.LastSeenFrame <= 120)
                continue;

            (stale ??= new List<long>()).Add(pair.Key);
        }

        if (stale is null)
            return;

        foreach (long id in stale)
        {
            var binding = _bindings[id];
            if (binding.Channel is not null)
            {
                binding.Channel.Stop();
                _engine?.DestroyChannel(binding.Channel);
            }
            _bindings.Remove(id);
        }
    }

    /// <summary>Sets a logical game-audio bus volume. Used by settings menus.</summary>
    public void SetBusVolume(AudioSourceBus bus, float volume)
    {
        float gain = Math.Clamp(volume, 0f, 1f);
        _busVolumes[bus] = gain;

        if (bus == AudioSourceBus.Master)
        {
            if (_engine is not null)
                _engine.Master.Gain = gain;
            return;
        }

        if (_buses.TryGetValue(bus, out var audioBus))
            audioBus.Gain = gain;
    }

    public float GetBusVolume(AudioSourceBus bus)
    {
        if (bus == AudioSourceBus.Master)
            return _engine?.Master.Gain ?? (_busVolumes.TryGetValue(bus, out float master) ? master : 1f);

        return _busVolumes.TryGetValue(bus, out float gain)
            ? gain
            : _buses.TryGetValue(bus, out var audioBus) ? audioBus.Gain : 1f;
    }

    private ListenerState FindListener(GameRoot? gameRoot)
    {
        if (gameRoot is null)
            return default;

        var listenerElement = gameRoot.FindElementWithComponent<IAudioListener>();
        if (listenerElement is not null)
        {
            var listener = listenerElement.GetComponent<IAudioListener>();
            if (listener is not null)
            {
                return new ListenerState
                {
                    Position = listener.Position,
                    Forward = listener.Forward,
                    Up = listener.Up,
                    HasValue = true,
                };
            }
        }

        var cameraElement = gameRoot.SelectedWorld?.FindElementWithComponentRecursive<ICameraRenderable>();
        if (cameraElement is null)
        {
            foreach (var world in gameRoot.ActiveWorlds)
            {
                cameraElement = world.FindElementWithComponentRecursive<ICameraRenderable>();
                if (cameraElement is not null)
                    break;
            }
        }

        if (cameraElement is null)
            return default;

        var matrix = cameraElement.WorldTransform;
        var position = new Vector3(matrix.M41, matrix.M42, matrix.M43);
        var forward = -new Vector3(matrix.M31, matrix.M32, matrix.M33);
        var up = new Vector3(matrix.M21, matrix.M22, matrix.M23);

        if (forward.LengthSquared() < 0.0001f)
            forward = -Vector3.UnitZ;
        else
            forward = Vector3.Normalize(forward);

        if (up.LengthSquared() < 0.0001f)
            up = Vector3.UnitY;
        else
            up = Vector3.Normalize(up);

        var right = Vector3.Cross(forward, up);
        if (right.LengthSquared() < 0.0001f)
            right = Vector3.UnitX;
        else
            right = Vector3.Normalize(right);

        return new ListenerState
        {
            Position = position,
            Forward = forward,
            Up = up,
            Right = right,
            HasValue = true,
        };
    }

    private struct ListenerState
    {
        public Vector3 Position;
        public Vector3 Forward;
        public Vector3 Up;
        public Vector3 Right;
        public bool HasValue;

        public readonly Vector3 GetRight()
        {
            if (Right.LengthSquared() > 0.0001f)
                return Right;

            var right = Vector3.Cross(Forward, Up);
            return right.LengthSquared() > 0.0001f ? Vector3.Normalize(right) : Vector3.UnitX;
        }
    }
}
