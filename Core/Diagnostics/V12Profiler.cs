
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
#if TRACY
using bottlenoselabs.C2CS.Runtime;
using Tracy;
#endif

namespace V12.Core.Diagnostics
{
    /// <summary>
    /// Compile-time-gated Tracy instrumentation for the V12 engine and its
    /// frontends. Every member is a zero-cost no-op unless the consuming build
    /// defines <c>TRACY</c> (Debug / ExportDebug by default).
    ///
    /// The wrapper deliberately exposes only the pieces the engine needs:
    /// named zones, frame marks, plots, messages and thread names. Callers use
    /// <see cref="BeginZone"/> with a <c>using</c> scope so a zone always ends,
    /// even when the wrapped code throws.
    /// </summary>
    public static class V12Profiler
    {
        /// <summary>
        /// Zone colours used by the engine. Tracy expects an RRGGBB value.
        /// Kept as named constants so a call site cannot accidentally pick a
        /// different shade for the same subsystem.
        /// </summary>
        public static class Colors
        {
            public const uint Engine = 0x4C78A8;
            public const uint World = 0x59A14F;
            public const uint Capture = 0xF28E2B;
            public const uint Runtime = 0x76B7B2;
            public const uint Renderer = 0xE15759;
        }

#if TRACY
        private static readonly Dictionary<string, CString> PlotNames = new();
        private static readonly object PlotNamesLock = new();
#endif

        /// <summary>True when this build was compiled with Tracy support.</summary>
        public static bool IsEnabled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if TRACY
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>True when the Tracy profiler is currently connected to this process.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsConnected()
        {
#if TRACY
            return PInvoke.TracyConnected() != 0;
#else
            return false;
#endif
        }

        /// <summary>
        /// Name the calling thread. Call once at the start of every thread that
        /// should appear in the profiler timeline (the V12 worker, the host's
        /// main thread, etc).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetThreadName(string name)
        {
#if TRACY
            using var cName = CString.FromString(name);
            PInvoke.TracySetThreadName(cName);
#endif
        }

        /// <summary>Emit a message shown in Tracy's Info tab.</summary>
        public static void AppInfo(string info)
        {
#if TRACY
            using var cInfo = CString.FromString(info);
            PInvoke.TracyEmitMessageAppinfo(cInfo, (ulong)info.Length);
#endif
        }

        /// <summary>
        /// Emit a transient text message at the current timeline position.
        /// </summary>
        public static void Message(string text, uint color = 0)
        {
#if TRACY
            using var cText = CString.FromString(text);
            PInvoke.TracyEmitMessageC(cText, (ulong)text.Length, color, 0);
#endif
        }

        /// <summary>
        /// Begin a named profiling zone. Dispose the returned value (a
        /// <c>using</c> scope is the normal form) to end it.
        /// </summary>
        /// <param name="name">Zone label shown in the timeline and statistics.</param>
        /// <param name="color">Optional RRGGBB zone colour, from <see cref="Colors"/>.</param>
        /// <param name="text">Optional extra text attached to this single zone.</param>
        /// <param name="filePath">Supplied by the compiler.</param>
        /// <param name="memberName">Supplied by the compiler.</param>
        /// <param name="lineNumber">Supplied by the compiler.</param>
        public static Zone BeginZone(
            string name,
            uint color = 0,
            string? text = null,
            [CallerFilePath] string filePath = "",
            [CallerMemberName] string memberName = "",
            [CallerLineNumber] int lineNumber = 0)
        {
#if TRACY
            using var cFile = CString.FromString(filePath);
            using var cMember = CString.FromString(memberName);
            using var cName = CString.FromString(name);

            var sourceLocation = PInvoke.TracyAllocSrclocName(
                (uint)lineNumber,
                cFile, (ulong)filePath.Length,
                cMember, (ulong)memberName.Length,
                cName, (ulong)name.Length,
                color);

            var context = PInvoke.TracyEmitZoneBeginAlloc(sourceLocation, 1);

            if (text != null)
            {
                using var cText = CString.FromString(text);
                PInvoke.TracyEmitZoneText(context, cText, (ulong)text.Length);
            }

            return new Zone(context);
#else
            return default;
#endif
        }

        /// <summary>Mark the end of a frame in the Tracy timeline.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FrameMark()
        {
#if TRACY
            PInvoke.TracyEmitFrameMark(default);
#endif
        }

        /// <summary>Add a value to a named plot. The plot name is cached for the process lifetime.</summary>
        public static void Plot(string name, double value)
        {
#if TRACY
            CString cName;
            lock (PlotNamesLock)
            {
                if (!PlotNames.TryGetValue(name, out cName))
                {
                    cName = CString.FromString(name);
                    PlotNames[name] = cName;
                }
            }

            PInvoke.TracyEmitPlot(cName, value);
#endif
        }

        /// <summary>
        /// Handle for an active Tracy zone. Disposing it ends the zone.
        /// </summary>
        public readonly struct Zone : IDisposable
        {
#if TRACY
            private readonly PInvoke.TracyCZoneCtx _context;

            internal Zone(PInvoke.TracyCZoneCtx context)
            {
                _context = context;
            }

            /// <summary>Attach text to this individual zone.</summary>
            public void SetText(string text)
            {
                using var cText = CString.FromString(text);
                PInvoke.TracyEmitZoneText(_context, cText, (ulong)text.Length);
            }

            /// <summary>Override this zone's name.</summary>
            public void SetName(string name)
            {
                using var cName = CString.FromString(name);
                PInvoke.TracyEmitZoneName(_context, cName, (ulong)name.Length);
            }

            /// <summary>Override this zone's colour.</summary>
            public void SetColor(uint color)
            {
                PInvoke.TracyEmitZoneColor(_context, color);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                PInvoke.TracyEmitZoneEnd(_context);
            }
#else
            /// <summary>No-op overload retained so call sites compile in every configuration.</summary>
            public void SetText(string text) { }

            /// <summary>No-op overload retained so call sites compile in every configuration.</summary>
            public void SetName(string name) { }

            /// <summary>No-op overload retained so call sites compile in every configuration.</summary>
            public void SetColor(uint color) { }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose() { }
#endif
        }
    }
}
