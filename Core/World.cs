using V12.Interfaces;

namespace V12.Core
{
    public class World
    {
        public List<IWorldElement> Root { get; set; }
        public string WorldName { get; set; }
        public World() { 
            WorldName = "DefaultWorld";
        }

        public World(string Name)
        {
            WorldName = Name;
        }

        public void Update(float deltaTime)
        {
            foreach (var element in Root)
            {
                try
                {
                element.Components.ForEach(component => component.Update(deltaTime));
                }
                catch { }
            }
        }
    }
}
