using System;
using System.Collections.Concurrent;
using MoonSharp.Interpreter;
using V12.Core.Interfaces;

namespace V12.Core
{
    public class MoonSharpScriptRuntime : IScriptRuntime
    {
        private Script _script;
        private readonly ConcurrentQueue<string> _printQueue = new();
        public event Action<string> OnPrint;
        public bool SupportsHotReload => true;
        public string[] SupportedExtensions => new[] { ".lua" };

        public MoonSharpScriptRuntime()
        {
            _script = new Script(CoreModules.Preset_Complete);
            _script.Globals["print"] = (Action<string>)(msg =>
            {
                _printQueue.Enqueue(msg);
                OnPrint?.Invoke(msg);
            });
            _script.Options.DebugPrint = msg =>
            {
                _printQueue.Enqueue(msg);
                OnPrint?.Invoke(msg);
            };
        }

        public void Load(string source, string scriptName)
        {
            try
            {
                _script.DoString(source, null, scriptName);
            }
            catch (SyntaxErrorException ex)
            {
                Console.WriteLine($"[MoonSharp] Syntax error in {scriptName}: {ex.Message}");
            }
        }

        public void Call(string functionName, params object[] args)
        {
            try
            {
                var fn = _script.Globals.Get(functionName);
                if (fn.Type == DataType.Function)
                    _script.Call(fn, args);
            }
            catch (ScriptRuntimeException ex)
            {
                Console.WriteLine($"[MoonSharp] Runtime error in {functionName}: {ex.Message}");
            }
        }

        public void SetGlobal(string name, object value)
        {
            _script.Globals[name] = DynValue.FromObject(_script, value);
        }

        public object GetGlobal(string name)
        {
            var val = _script.Globals.Get(name);
            return val.ToObject();
        }

        public void Dispose()
        {
            _script = null;
        }
    }
}
