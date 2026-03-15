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
        /// <summary>
        /// The currently selected or focused world. When set, only this world will be updated by Update().
        /// </summary>
        public World? SelectedWorld { get; private set; }
            World UserSpace = new World("UserSpace"); // always open and active no matter what, contains the UI for being able to control the game and select worlds, etc. This world is not meant to be used for actual game content, but rather for the user interface and control of the game.
            World HomeWorld = new World("HomeWorld");
        
        public GameRoot() { 
            Worlds.Add(UserSpace);
            Worlds.Add(HomeWorld);
            // Default to the first created world as the focused world
            SelectedWorld = HomeWorld;
        }
        
        public void Initialize()
        {
#if DEBUG
            foreach (var World in Worlds)
            {
                Console.WriteLine($"Initializing world: {World.WorldName}");

            }
#endif 
        }

        public void Update(float deltaTime)
        {
            if (SelectedWorld != null)
            {
                try
                {
                    SelectedWorld.Update(deltaTime);
                }
                catch { }
                return;
            }
            UserSpace.Update(deltaTime);
        }

        public void SelectWorld(World world)
        {
            if (world == null) return;
            if (!Worlds.Contains(world)) return;
            SelectedWorld = world;
        }

        public bool SelectWorldByName(string name)
        {
            var found = Worlds.Find(w => w.WorldName == name);
            if (found != null)
            {
                SelectedWorld = found;
                return true;
            }
            return false;
        }

        public void DeselectWorld() => SelectedWorld = null;

        

    }
}
