using System;
using System.Collections.Generic;
using System.Text;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.StereoKit.DebugItems
{
    public class InspectorComponent : ComponentBase
    {
        public static GameRoot root;
        public static bool _enabled = true;
        public IWorldElement rootElement;
        public static UIBuilder b;
        public InspectorComponent(GameRoot r) => root = r;
        //public void BuildUI()
        //{
        //    b = new();
        //    b.Button(b.Root, "meow", () => Console.WriteLine("meow"));
        //    rootElement = b.Root;
        //}
        public override void OnAttach(IWorldElement worldElement)
        {
            Console.WriteLine("KASDKJASKDJASDKJASDK \a inspector made");
            base.OnAttach(worldElement);
            b = new(worldElement, 800,600);
            b.Button(b.Root, "meow", () => Console.WriteLine("meow"));
            worldElement = b.Root; // if this works i am gonna be sad
            //worldElement.AddComponent(new MeshComponent(MeshShape.Box, 0.2f, 0.2f, 0.2f));
        }
        public void Render()
        {
          
        }

        public override IWorldElement BuildUI()
        {
            return new Element("thing");
        }
    }
}
