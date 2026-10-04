using System;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using V12.Core.Core.Interfaces;
using V12.Core.NetworkCable;

namespace V12.Core.Networking
{
    /// <summary>
    /// Generic RPC channel. Call-side and receive-side live here so game code has a single
    /// entry point: mark a method <c>[Remote]</c>, trigger it by name, and every peer holding
    /// the matching element invokes it. Button presses ride the same channel via
    /// <see cref="ButtonComponent.Press"/>.
    ///
    /// Elements are addressed by id first (ids are synced to match the host after a
    /// WorldArchive), falling back to name for independently-spawned elements that every
    /// peer creates with the same name (e.g. the sample "SpawnBoxButton").
    ///
    /// Dispatch order per element: a <c>[Remote]</c> method with the given name, then a
    /// delegate-typed property with the given name (so plain closures like
    /// ButtonComponent.OnPressed or SliderComponent.OnChanged work without an attribute).
    /// </summary>
    public static class RpcDispatcher
    {
        /// <summary>Broadcast an RPC call against the element with <paramref name="elementId"/>.</summary>
        public static void Call(GameRoot root, long elementId, string method, params object[]? args)
            => Send(root, new RpcCallDTO { ElementId = elementId, Method = method }, args);

        /// <summary>Broadcast an RPC call against the element addressed by <paramref name="elementName"/>.</summary>
        public static void CallByName(GameRoot root, string elementName, string method, params object[]? args)
            => Send(root, new RpcCallDTO { ElementName = elementName, Method = method }, args);

        /// <summary>Broadcast an RPC call against the given element (id + name captured for lookup).</summary>
        public static void CallOn(GameRoot root, IWorldElement element, string method, params object[]? args)
        {
            if (element == null) return;
            Send(root, new RpcCallDTO { ElementId = element.Id, ElementName = element.Name, Method = method }, args);
        }

        /// <summary>Broadcast a button press so the target button's Press handler runs on peers.</summary>
        public static void CallButtonPressed(GameRoot root, IWorldElement button) => CallOn(root, button, "Press");

        private static void Send(GameRoot root, RpcCallDTO dto, object[]? args)
        {
            if (root?.Cables == null) return;

            // No connected peer (single-player / headless): nothing to broadcast to, and the
            // payload can't be serialized without a transport-configured BSON context. The
            // caller has already applied the effect locally.
            if (!HasConnectedPeer(root)) return;

            if (args != null && args.Length > 0)
            {
                try { dto.ArgsData = ((object)args).ToBson(); }
                catch (Exception ex) { Console.WriteLine($"[Rpc] Failed to serialize args for '{dto.Method}': {ex.Message}"); }
            }
            root.Cables.SendData(new MessageDTO(new Uri("networkcables://client"), AncientCompressor.Compress(dto))
            {
                MessageType = MessageType.RpcCall
            });
        }

        /// <summary>Receive-side: run the call locally on the matching element (no-op if absent).</summary>
        public static void InvokeLocal(GameRoot root, RpcCallDTO dto)
        {
            if (dto == null || root == null) return;

            object[]? args = null;
            if (dto.ArgsData != null && dto.ArgsData.Length > 0)
            {
                try { args = BsonSerializer.Deserialize<object[]>(dto.ArgsData); }
                catch (Exception ex) { Console.WriteLine($"[Rpc] Failed to deserialize args for '{dto.Method}': {ex.Message}"); }
            }

            var el = root.FindElement(e => e.Id == dto.ElementId);
            if (el == null && !string.IsNullOrEmpty(dto.ElementName))
                el = root.FindElement(e => e.Name == dto.ElementName);
            if (el == null) return;

            InvokeOnElement(el, dto.Method, args ?? Array.Empty<object>());
        }

        /// <summary>Invoke <paramref name="method"/> on the element, or on one of its components.</summary>
        public static void InvokeOnElement(IWorldElement el, string method, params object[]? args)
        {
            if (el == null || string.IsNullOrEmpty(method)) return;
            var argsArr = args ?? Array.Empty<object>();

            if (TryInvoke(el, method, argsArr)) return;
            if (el.Components != null)
            {
                foreach (var comp in el.Components)
                {
                    if (TryInvoke(comp, method, argsArr)) return;
                }
            }
        }

        private static bool TryInvoke(object target, string method, object[] args)
        {
            var type = target.GetType();

            // 1. [Remote] method (public or private, handles overloads by name).
            foreach (var mi in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (mi.Name != method || mi.GetCustomAttribute<RemoteAttribute>() == null) continue;
                try
                {
                    mi.Invoke(target, BindArgs(mi, args));
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Rpc] Failed to invoke {type.Name}.{method}: {ex.Message}");
                    return false;
                }
            }

            // 2. Delegate-typed property (e.g. ButtonComponent.OnPressed, SliderComponent.OnChanged).
            var prop = type.GetProperty(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && typeof(MulticastDelegate).IsAssignableFrom(prop.PropertyType))
            {
                if (prop.GetValue(target) is Delegate del)
                {
                    try { del.DynamicInvoke(BindArgs(del.Method, args)); }
                    catch (Exception ex) { Console.WriteLine($"[Rpc] Failed to invoke {type.Name}.{method} handler: {ex.Message}"); }
                    return true;
                }
            }
            return false;
        }

        private static object?[] BindArgs(MethodBase method, object[] args)
        {
            var ps = method.GetParameters();
            var bound = new object?[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (i < args.Length && args[i] != null)
                {
                    try { bound[i] = Convert.ChangeType(args[i], ps[i].ParameterType); }
                    catch { bound[i] = args[i]; }
                }
                else
                {
                    bound[i] = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
                }
            }
            return bound;
        }

        /// <summary>True when a network host has at least one client, or a client is connected.</summary>
        private static bool HasConnectedPeer(GameRoot root)
        {
            try
            {
                var host = root.Registry?.Get<NetworkHost>("NetworkHost");
                if (host != null) return host.ClientCount > 0;

                var client = root.Registry?.Get<NetworkClient>("NetworkClient");
                if (client != null) return client.IsConnected;
            }
            catch { /* registry not ready – treat as no peer */ }
            return false;
        }
    }

    /// <summary>Ergonomic trigger: <c>element.CallRemote("Press")</c>.</summary>
    public static class RpcElementExtensions
    {
        public static void CallRemote(this IWorldElement element, string method, params object[]? args)
        {
            var root = GameRoot.Instance;
            if (root != null) RpcDispatcher.CallOn(root, element, method, args);
        }
    }
}
