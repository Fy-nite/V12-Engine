using V12.Core.Core.Interfaces;

namespace V12.Core.Interfaces
{
    /// <summary>
    /// Optional capability for a <see cref="IScriptRuntime"/> that needs to know
    /// which world element the script is attached to (e.g. a compiled Contract
    /// runtime exposing the owner to the script). ScriptComponent calls
    /// <see cref="SetOwner"/> after selecting the runtime so scripts can reach
    /// their own element.
    /// </summary>
    public interface IScriptOwnerAwareRuntime
    {
        void SetOwner(IWorldElement owner);
    }
}
