using System;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    public class ScriptComponent : ComponentBase
    {
        public string Source { get; set; }
        public string ScriptText { get; set; }

        /// <summary>Live interpreter handle — never crosses the wire (receivers
        /// rebuild it from <see cref="ScriptText"/> in OnAttach).</summary>
        [BsonIgnore]
        public IScriptRuntime Runtime { get; private set; }
        public bool IsInitialized { get; private set; }

        public override string Name => "Script";

        public ScriptComponent()
        {
            Source = string.Empty;
            ScriptText = string.Empty;
        }

        public override void OnAttach(IWorldElement worldElement)
        {
            base.OnAttach(worldElement);
            Initialize();
        }

        public void Initialize()
        {
            if (IsInitialized) return;

            try
            {
                var resolver = GetAssetResolver();

                // Use ScriptRuntimeRegistry to select the correct runtime for the script extension
                var scriptRegistry = FindGameRoot()?.Registry.Get<ScriptRuntimeRegistry>();
                Runtime = scriptRegistry?.CreateForScript(Source) ?? new MoonSharpScriptRuntime();
                Runtime.OnPrint += msg => Console.WriteLine($"[Script:{Name}] {msg}");

                // Runtimes that need the owning element (e.g. Contract .ct scripts)
                // receive it up front so hooks like on_init/on_update can address it.
                if (Owner != null && Runtime is IScriptOwnerAwareRuntime ownerAware)
                    ownerAware.SetOwner(Owner);

                // Expose owner element to Lua
                if (Owner != null)
                {
                    Runtime.SetGlobal("get_owner", (Func<IWorldElement>)(() => Owner));
                    Runtime.SetGlobal("get_component", (Func<string, object>)(name =>
                    {
                        foreach (var c in Owner.Components)
                        {
                            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
                                || c.GetType().Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                                return c;
                        }
                        return null;
                    }));
                }

                string code;
                string scriptName = Source ?? "inline";
                if (!string.IsNullOrEmpty(ScriptText))
                    code = ScriptText;
                else if (!string.IsNullOrEmpty(Source))
                {
                    // Fall back to the literal Source path when there is no asset
                    // resolver (or it can't place the file) — a plain relative or
                    // absolute path still loads.
                    string? resolvedPath = resolver?.Resolve(Source);
                    if (string.IsNullOrEmpty(resolvedPath)) resolvedPath = Source;
                    if (System.IO.File.Exists(resolvedPath))
                    {
                        // Compiled modules (.orbt/.oil) are read by the script
                        // runtime itself; only text sources are read here.
                        string ext = System.IO.Path.GetExtension(resolvedPath);
                        bool compiled = ext.Equals(".orbt", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".oil", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".oir", StringComparison.OrdinalIgnoreCase);
                        code = compiled ? "" : System.IO.File.ReadAllText(resolvedPath);
                        if (compiled) scriptName = resolvedPath;
                    }
                    else
                    {
                        Console.WriteLine($"[ScriptComponent] Script not found: {Source} (resolved: {resolvedPath})");
                        return;
                    }
                }
                else return;

                Runtime.Load(code, scriptName);
                Runtime.Call("on_init");
                IsInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScriptComponent] Error initializing: {ex.Message}");
            }
        }

        public void Reload()
        {
            Runtime?.Dispose();
            Runtime = null;
            IsInitialized = false;
            Initialize();
        }

        /// <summary>Generate editable script fields for the inspector.</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Script");
            inspector.String("Source", () => Source, v => Source = v);
            inspector.String("Script Text", () => ScriptText, v => ScriptText = v);
            inspector.ReadOnly("Initialized", IsInitialized);
            inspector.Button("Reload", Reload);
        }

        public override void OnDetach(IWorldElement worldElement)
        {
            Runtime?.Dispose();
            Runtime = null;
            IsInitialized = false;
            base.OnDetach(worldElement);
        }

        public override void Update(float deltaTime)
        {
            if (!IsInitialized || Runtime == null) return;
            Runtime.Call("on_update", (double)deltaTime);
        }

        public void CallEvent(string eventName, params object[] args)
        {
            if (!IsInitialized || Runtime == null) return;
            Runtime.Call(eventName, args);
        }

        private IAssetResolver GetAssetResolver()
        {
            var root = FindGameRoot();
            return root?.Registry.Get<IAssetResolver>();
        }

        private GameRoot FindGameRoot()
        {
            return GameRoot.Instance;
        }

        public override IWorldElement BuildUI()
        {
            return V12.Core.Procedurals.GenBox("ScriptComponentUI", System.Numerics.Vector3.One);
        }
    }
}
