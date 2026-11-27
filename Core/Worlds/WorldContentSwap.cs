using System;
using System.Linq;
using V12.Core.Core.Interfaces;

namespace V12.Core
{
    /// <summary>
    /// Replaces a world's contents while giving old components a chance to
    /// detach. Used by content hot reload; the world object and its session stay
    /// alive, so focus, camera routing and UI continue to point at the same
    /// world.
    /// </summary>
    public static class WorldContentSwap
    {
        /// <summary>
        /// Detach every component in <paramref name="target"/>, then replace its
        /// whole tree with <paramref name="incoming"/>.
        /// </summary>
        public static void Replace(World target, World incoming, bool detachOld = true)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            if (detachOld)
                target.DetachAll();

            target.ReplaceFrom(incoming);
            GameRoot.Instance?.MarkRenderDirty();
        }
    }
}
