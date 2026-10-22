using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Convenience base class for components that are compiled from C# source at
    /// runtime (a project's <c>scripts/*.cs</c> folder or a <c>.v12pak</c> that
    /// ships C# sources) instead of living in a compiled game assembly.
    /// <para>
    /// It fills in the two pieces <see cref="ComponentBase"/> leaves abstract and
    /// default-constructs <see cref="ComponentBase.Name"/> from the concrete type
    /// name, so a user component can be as small as:
    /// </para>
    /// <code>
    /// public sealed class SpinComponent : ScriptableComponent
    /// {
    ///     public float Speed = 1f;
    ///     public override void Update(float deltaTime)
    ///         => Owner?.GetOrAddTransform().RY += Speed * deltaTime;
    /// }
    /// </code>
    /// <para>
    /// Instances are created by <c>V12.Bindings.CSharpComponentRegistry</c> and are
    /// updated by <see cref="World"/> automatically once attached to an element.
    /// </para>
    /// </summary>
    public abstract class ScriptableComponent : ComponentBase
    {
        protected ScriptableComponent()
        {
            Name = GetType().Name;
        }

        /// <summary>
        /// Compiled components have no authored UI representation of their own.
        /// Returns an empty element so the engine always gets a non-null result.
        /// </summary>
        public override IWorldElement BuildUI() => new Element();
    }
}
