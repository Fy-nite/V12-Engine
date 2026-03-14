using System;
using V12.Interfaces;

namespace V12.Core.Networking
{
    /// <summary>
    /// Extension methods for easy dirty tracking on components and world elements.
    /// </summary>
    public static class DirtyTrackingExtensions
    {
        /// <summary>
        /// Raises the OnDirty event for a component if any subscribers are attached.
        /// </summary>
        /// <param name="component">The component to mark as dirty.</param>
        public static void RaiseDirty(this IComponent component)
        {
            // Access the event via reflection to invoke it
            var eventField = component.GetType().GetEvent("OnDirty");
            if (eventField != null)
            {
                var eventDelegate = (Action<IComponent>?)component.GetType()
                    .GetField("OnDirty", System.Reflection.BindingFlags.Instance | 
                                         System.Reflection.BindingFlags.NonPublic | 
                                         System.Reflection.BindingFlags.Public)?
                    .GetValue(component);
                
                eventDelegate?.Invoke(component);
            }
        }

        /// <summary>
        /// Raises the OnDirty event for a world element if any subscribers are attached.
        /// </summary>
        /// <param name="element">The world element to mark as dirty.</param>
        public static void RaiseDirty(this IWorldElement element)
        {
            // Access the event via reflection to invoke it
            var eventField = element.GetType().GetEvent("OnDirty");
            if (eventField != null)
            {
                var eventDelegate = (Action<IWorldElement>?)element.GetType()
                    .GetField("OnDirty", System.Reflection.BindingFlags.Instance | 
                                         System.Reflection.BindingFlags.NonPublic | 
                                         System.Reflection.BindingFlags.Public)?
                    .GetValue(element);
                
                eventDelegate?.Invoke(element);
            }
        }

        /// <summary>
        /// Automatically track all components in a world element.
        /// </summary>
        public static void TrackAllComponents(this DirtyTracker tracker, IWorldElement element)
        {
            if (element.Components == null) return;

            foreach (var component in element.Components)
            {
                tracker.TrackComponent(component);
            }
        }

        /// <summary>
        /// Check if a component has any dirty tracking subscribers.
        /// </summary>
        public static bool IsTracked(this IComponent component)
        {
            var eventField = component.GetType()
                .GetField("OnDirty", System.Reflection.BindingFlags.Instance | 
                                     System.Reflection.BindingFlags.NonPublic | 
                                     System.Reflection.BindingFlags.Public)?
                .GetValue(component) as Delegate;
            
            return eventField != null && eventField.GetInvocationList().Length > 0;
        }

        /// <summary>
        /// Check if a world element has any dirty tracking subscribers.
        /// </summary>
        public static bool IsTracked(this IWorldElement element)
        {
            var eventField = element.GetType()
                .GetField("OnDirty", System.Reflection.BindingFlags.Instance | 
                                     System.Reflection.BindingFlags.NonPublic | 
                                     System.Reflection.BindingFlags.Public)?
                .GetValue(element) as Delegate;
            
            return eventField != null && eventField.GetInvocationList().Length > 0;
        }
    }
}
