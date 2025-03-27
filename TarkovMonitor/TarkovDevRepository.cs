namespace TarkovMonitor
{
    /// <summary>
    /// Repository class that manages collections of Tarkov-related game data from the Tarkov.dev API.
    /// This class serves as a central data store for tasks, maps, and items retrieved from the game.
    /// </summary>
    internal class TarkovDevRepository
    {
        /// <summary>
        /// Collection of all Tarkov tasks/quests available in the game.
        /// Tasks represent missions or objectives that players can complete.
        /// </summary>
        public List<TarkovDev.Task> Tasks;

        /// <summary>
        /// Collection of all available maps in Escape from Tarkov.
        /// Maps represent different locations/levels where gameplay takes place.
        /// </summary>
        public List<TarkovDev.Map> Maps;

        /// <summary>
        /// Collection of all items available in the game.
        /// Items include weapons, armor, consumables, quest items, and other in-game objects.
        /// </summary>
        public List<TarkovDev.Item> Items;

        /// <summary>
        /// Initializes a new instance of the TarkovDevRepository class.
        /// Creates empty collections for Tasks, Maps, and Items which can be populated later
        /// with data from the Tarkov.dev API.
        /// </summary>
        public TarkovDevRepository()
        {
            // Initialize empty collections
            Tasks = new List<TarkovDev.Task>();
            Maps = new List<TarkovDev.Map>();
            Items = new List<TarkovDev.Item>();
        }
    }
}
