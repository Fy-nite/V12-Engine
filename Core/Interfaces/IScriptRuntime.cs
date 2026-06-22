using System;

namespace V12.Core.Interfaces
{
    public interface IScriptRuntime : IDisposable
    {
        void Load(string source, string scriptName);
        void Call(string functionName, params object[] args);
        void SetGlobal(string name, object value);
        object GetGlobal(string name);
        event Action<string> OnPrint;
        bool SupportsHotReload { get; }
    }
}
