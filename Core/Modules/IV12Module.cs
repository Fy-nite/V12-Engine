using System;
using System.Collections.Generic;

namespace V12.Core.Modules
{
    /// <summary>
    /// A module loaded into a running game from an assembly the engine and the
    /// renderer do <b>not</b> reference.
    ///
    /// <para>
    /// Modules are the trusted-code half of V12's mod story: overlays, dev tools,
    /// integrations, and high-trust gameplay extensions that need to attach to the
    /// live <see cref="GameRoot"/>. Content and components belong in game paks;
    /// modules are for code that runs in the same process.
    /// </para>
    ///
    /// <para>
    /// Nothing loads by default. A host opts in with
    /// <see cref="ModuleHost.EnableFromArgs"/> (the <c>--modules</c> and
    /// <c>--module</c> switches) or by calling <see cref="ModuleHost.Load"/> /
    /// <see cref="ModuleHost.LoadDirectory"/> directly, so a build that never opts
    /// in carries no module code.
    /// </para>
    ///
    /// <para>
    /// A module assembly must reference <c>V12</c> and contain a public,
    /// non-abstract implementation with a parameterless constructor. Modules are
    /// loaded through <c>GamepackLoader.AssemblyLoader</c> so they share the
    /// engine's load context; a module in a different context would implement a
    /// different <see cref="IV12Module"/> identity and never bind.
    /// </para>
    /// </summary>
    public interface IV12Module : IDisposable
    {
        /// <summary>
        /// Unique module id. Used for diagnostics, dependency resolution and the
        /// per-module data directory, so keep it stable across versions.
        /// </summary>
        string Name { get; }

        /// <summary>Optional human-readable version, shown in logs and diagnostics.</summary>
        string? Version => null;

        /// <summary>
        /// Module ids that must be started before this one. A missing dependency
        /// prevents this module from starting; a cycle is reported and the cycle
        /// members are skipped rather than half-started.
        /// </summary>
        IReadOnlyList<string> Dependencies => Array.Empty<string>();

        /// <summary>
        /// Attach to the running game. Return false to decline (for example when
        /// the module's own switch is absent); a declined module is disposed and
        /// not kept.
        /// </summary>
        bool Start(ModuleContext context);
    }
}
