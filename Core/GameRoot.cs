using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace V12.Core
{
    /// <summary>
    /// The GameRoot class serves as the central entry point for the game, managing the overall game state and coordinating interactions between various components and systems. It is responsible for initializing the game world, handling game logic, and facilitating communication between different parts of the game architecture. The GameRoot class may also manage resources, handle user input, and oversee the main game loop to ensure smooth gameplay.
    /// </summary>
    public class GameRoot
    {
        public List<World> Worlds = new List<World>();
        
        public GameRoot() { 
            World UserSpace = new World("UserSpace");
            World HomeWorld = new World("HomeWorld");
            Worlds.Add(UserSpace);
            Worlds.Add(HomeWorld);
        }
        
        public void Initialize()
        {
            foreach (var World in Worlds)
            {
#if DEBUG
                Console.WriteLine($"Initializing world: {World.WorldName}");
#endif 
            }
        }

        public void Update(float deltaTime)
        {
            foreach (var World in Worlds)
            {
                World.Update(deltaTime);
            }
        }

        

    }
}
