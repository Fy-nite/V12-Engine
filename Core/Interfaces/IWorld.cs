using System;
using System.Collections.Generic;
using System.Text;
namespace V12.Core.Core.Interfaces
{
    public interface IWorld
    {
        string Name { get; }
        string Description { get; }
        List<IWorldElement> WorldElements { get; set; }

        /// <summary>
        /// Updates the state of the object to reflect changes in the current environment or context.
        /// </summary>
        /// <remarks>Call this method periodically to ensure that the object's state remains consistent
        /// with external factors. This may involve recalculating values or refreshing data from external sources as
        /// needed.</remarks>
        void Update();

    }
}
