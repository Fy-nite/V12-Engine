using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using V12.Components;
using V12.Components.Renderables;
using V12.Core.Core.Interfaces;
// my bad dragon dildo gets here tuesday! :D
namespace V12.Core
{
    public static class Procedurals
    {
        public static IWorldElement GenBox(string boxname, Vector3 _Scale)
        {
            var b = new Element() { Name = boxname };
            b.AddComponent(new ColliderComponent() { Name = boxname });
            var mesh = new MeshComponent() { Shape= MeshShape.Box };
            b.AddComponent(mesh);
            b.AddComponent(new MeshRenderer { Mesh = mesh});
            return b;
        }
    }
}
