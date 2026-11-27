using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using V12.Core.Core.Interfaces;

namespace V12.Core.Networking
{
    /// <summary>
    /// Wire serialization for message payloads: BSON, plus an optional Brotli
    /// layer for payloads big enough to pay for it.
    /// <para>
    /// The compressed form is self-describing, so it stays backward compatible
    /// with raw BSON: a payload that starts with the <c>V12C</c> magic carries a
    /// codec byte and the original length; anything else is treated as plain
    /// BSON. Small payloads are never wrapped, so a 60 Hz value batch pays no
    /// header.
    /// </para>
    /// <para>
    /// Brotli is asymmetric on purpose: quality 3 compresses fast enough for the
    /// fixed step, and the case that matters (a component batch or a world
    /// snapshot with a big JSON/base64 payload) shrinks a lot. Already-compressed
    /// payloads are stored raw, because compression would only add bytes.
    /// </para>
    /// <para>
    /// This also feeds the transport's delivery choice. The size that decides
    /// whether a payload has to escalate to reliable is the size <em>after</em>
    /// this codec, so a large element update that compresses under the MTU stays
    /// on the unreliable channel instead of being promoted to reliable (and
    /// head-of-line blocking everything behind it). Compression improves latency
    /// here, not only bandwidth, and a reliable payload needs fewer fragments.
    /// </para>
    /// </summary>
    public static class AncientCompressor
    {
        /// <summary>"V12C". A real BSON document starts with its int32 length, so
        /// this four-byte sequence cannot collide with a legacy payload.</summary>
        private static readonly byte[] Magic = { 0x56, 0x31, 0x32, 0x43 };

        private const byte CodecBrotli = 1;
        private const int HeaderSize = 9; // magic(4) + codec(1) + originalLength(4)
        private const int MaxDecompressedBytes = 512 * 1024 * 1024;

        /// <summary>Payloads smaller than this are sent as raw BSON.</summary>
        public static int MinBytesToCompress { get; set; } = 512;

        /// <summary>Brotli quality. 1-4 trades ratio for latency.</summary>
        public static int BrotliQuality { get; set; } = 3;

        /// <summary>Brotli window bits.</summary>
        public static int BrotliWindow { get; set; } = 22;

        public static byte[] Compress(object obj)
        {
            // World uses a flat snapshot DTO because its Root is List<IWorldElement>
            // (interface-typed), which BSON cannot serialize without discriminator class maps.
            if (obj is V12.Core.World w)
            {
                var snap = new WorldSyncDTO { WorldId = w.Id, WorldName = w.WorldName };
                foreach (var el in w.Root)
                {
                    var esDto = CompressElement(el);
                    if (esDto != null) snap.Elements.Add(esDto);
                }
                return MaybeCompress(snap.ToBson());
            }

            return MaybeCompress(obj.ToBson());
        }

        public static ElementSyncDTO CompressElement(IWorldElement el)
        {
            if (el == null) return null;
            // Editor-transient subtrees (gizmo handles) are never synced.
            if (V12.Components.NonPersistentComponent.IsTransient(el)) return null;
            var esDto = new ElementSyncDTO { Id = el.Id, Uid = el.Uid, Name = el.Name, Description = el.Description };
            esDto.Position = el.LocalTransform.Position;
            esDto.Rotation = el.LocalTransform.Rotation;
            esDto.Scale = el.LocalTransform.Scale;
            if (el.Components != null)
            {
                foreach (var comp in el.Components)
                {
                    try
                    {
                        var csDto = CompressComponent(comp);
                        if (csDto != null) esDto.Components.Add(csDto);
                    }
                    catch { }
                }
            }
            if (el.Children != null)
            {
                foreach (var child in el.Children)
                {
                    var childDto = CompressElement(child);
                    if (childDto != null) esDto.Children.Add(childDto);
                }
            }
            return esDto;
        }

        public static T Decompress<T>(byte[] data)
        {
            data = MaybeDecompress(data);

            if (typeof(T) == typeof(V12.Core.World))
            {
                var snap = BsonSerializer.Deserialize<WorldSyncDTO>(data);
                var world = new V12.Core.World(snap.WorldName ?? "World");
                if (snap.WorldId != Guid.Empty)
                    world.Id = snap.WorldId;
                foreach (var es in snap.Elements)
                {
                    var element = DecompressElement(es);
                    if (element != null) world.Root.Add(element);
                }
                return (T)(object)world;
            }

            return BsonSerializer.Deserialize<T>(data);
        }

        /// <summary>Brotli-compress when it is worth it, otherwise return the input.</summary>
        private static byte[] MaybeCompress(byte[] raw)
        {
            if (raw is null || raw.Length < MinBytesToCompress) return raw!;

            int maxCompressed = BrotliEncoder.GetMaxCompressedLength(raw.Length);
            byte[] scratch = ArrayPool<byte>.Shared.Rent(HeaderSize + maxCompressed);
            try
            {
                if (!BrotliEncoder.TryCompress(
                        raw,
                        scratch.AsSpan(HeaderSize, maxCompressed),
                        out int written,
                        Math.Clamp(BrotliQuality, 0, 11),
                        Math.Clamp(BrotliWindow, 10, 24)))
                {
                    return raw;
                }

                // Not worth a header (already dense, or too small to matter).
                if (HeaderSize + written >= raw.Length) return raw;

                Magic.CopyTo(scratch, 0);
                scratch[4] = CodecBrotli;
                BinaryPrimitives.WriteInt32LittleEndian(scratch.AsSpan(5, 4), raw.Length);

                var result = new byte[HeaderSize + written];
                Buffer.BlockCopy(scratch, 0, result, 0, result.Length);
                return result;
            }
            catch
            {
                return raw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        /// <summary>Undo <see cref="MaybeCompress"/>. Raw BSON passes through.</summary>
        private static byte[] MaybeDecompress(byte[] data)
        {
            if (data is null || data.Length < HeaderSize + 1) return data!;

            for (int i = 0; i < Magic.Length; i++)
                if (data[i] != Magic[i]) return data; // legacy raw BSON

            if (data[4] != CodecBrotli) return data;

            int originalLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(5, 4));
            if (originalLength <= 0 || originalLength > MaxDecompressedBytes)
                throw new InvalidDataException($"Invalid compressed payload length {originalLength}");

            var result = new byte[originalLength];
            if (!BrotliDecoder.TryDecompress(data.AsSpan(HeaderSize), result, out int written)
                || written != originalLength)
            {
                throw new InvalidDataException("Malformed Brotli payload");
            }

            return result;
        }

        public static IWorldElement DecompressElement(ElementSyncDTO es)
        {
            if (es == null) return null;
            var element = new V12.Core.Element(es.Name, es.Description);
            element.Id = es.Id;
            // An incoming pin wins only when it is set; never wipe a locally
            // authored uid on a peer that loaded the archive from disk.
            if (es.Uid != Guid.Empty)
                element.Uid = es.Uid;
            element.LocalTransform = new V12.Core.Interfaces.Renderer.TRS
            {
                Position = es.Position,
                Rotation = es.Rotation,
                Scale = es.Scale
            };
            if (es.Components != null)
            {
                foreach (var cs in es.Components)
                {
                    try
                    {
                        var comp = DecompressComponent(cs);
                        if (comp != null) element.Components.Add(comp);
                    }
                    catch { }
                }
            }
            if (es.Children != null)
            {
                foreach (var childDto in es.Children)
                {
                    var child = DecompressElement(childDto);
                    if (child != null) element.AddChild(child);
                }
            }

            // Call OnAttach for each component after children are added
            // (some components, e.g. PlayerComponent, find or create child elements in OnAttach)
            foreach (var comp in element.Components)
            {
                try { comp.OnAttach(element); } catch { }
            }

            return element;
        }

        // -- Component serialization helpers -----------------------------------

        /// <summary>
        /// Serialize a single component to a <see cref="ComponentSyncDTO"/> suitable for wire transport.
        /// Returns null if the component cannot be serialized.
        /// </summary>
        public static ComponentSyncDTO? CompressComponent(IComponent comp)
        {
            if (comp == null) return null;
            var type = comp.GetType();
            try
            {
                // Use a short name that still uniquely identifies the type across matching assemblies:
                // "FullTypeName, AssemblyShortName" (no version/culture so it round-trips cleanly).
                var typeName = $"{type.FullName}, {type.Assembly.GetName().Name}";
                var data = ((object)comp).ToBson(type);
                return new ComponentSyncDTO { TypeName = typeName, Data = data };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AncientCompressor] Failed to serialize {type.FullName}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reconstruct a component from a <see cref="ComponentSyncDTO"/> received over the wire.
        /// Returns null if the type cannot be resolved or the data cannot be deserialized.
        /// </summary>
        public static IComponent? DecompressComponent(ComponentSyncDTO dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.TypeName) || dto.Data == null)
            {
                Console.WriteLine("[AncientCompressor] DecompressComponent: null or invalid DTO");
                return null;
            }

            // Resolve type: try exact name then short name fallback.
            var type = Type.GetType(dto.TypeName)
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                           .Select(a => { try { return a.GetType(dto.TypeName.Split(',')[0].Trim()); } catch { return null; } })
                           .FirstOrDefault(t => t != null);

            if (type == null)
            {
                Console.WriteLine($"[AncientCompressor] DecompressComponent: could not resolve type '{dto.TypeName}'");
                return null;
            }

            try
            {
                // Deserialize via BsonDocument so we don't need a generic type parameter at compile time.
                var doc = BsonSerializer.Deserialize<BsonDocument>(dto.Data);
                var serializer = BsonSerializer.LookupSerializer(type);
                using var reader = new BsonDocumentReader(doc);
                var ctx  = BsonDeserializationContext.CreateRoot(reader);
                var args = new BsonDeserializationArgs { NominalType = type };
                return serializer.Deserialize(ctx, args) as IComponent;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AncientCompressor] Failed to deserialize {type.FullName}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }
}
