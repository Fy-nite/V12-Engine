using V12.Core.Core.Interfaces;

namespace V12.Core
{
    public class World
    {
        public List<IWorldElement> Root { get; set; }
        public string WorldName { get; set; }
        public World() { 
            WorldName = "DefaultWorld";
            Root = new List<IWorldElement>();
        }

        public World(string Name)
        {
            WorldName = Name;
            Root = new List<IWorldElement>();
        }
        public void AddElement(IWorldElement element)
        {
            Root.Add(element);
        }
        public void GenerateWorld()
        {
            // This method can be overridden in derived classes to create specific world content.
            // For example, you could create a HomeWorld class that inherits from World and overrides this method to populate the world with specific elements.
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
