using System;
using System.Collections.Generic;
using System.Text;
using V12.Interfaces;

namespace V12.Core
{
    public class Element : IWorldElement
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public IWorldElement? Parent { get; set; }

        public Element(string? name = null, string? description = null, IWorldElement? parent = null)
        {
            Name = name;
            Description = description;
            Parent = parent;
        }
    }
}
