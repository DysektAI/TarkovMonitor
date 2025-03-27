using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using Refit;
using System.Diagnostics;

namespace TarkovMonitor
{
    /// <summary>
    /// Main class for interacting with the Tarkov.dev API services.
    /// This class provides functionality to fetch game data, submit player statistics,
    /// and manage various game-related information for Escape from Tarkov.
    /// </summary>
    internal class TarkovDev
    {
        // GraphQL client for the main Tarkov.dev API
        private static readonly GraphQLHttpClient client = new("https://api.tarkov.dev/graphql", new SystemTextJsonSerializer());

        /// <summary>
        /// Interface for the Tarkov.dev data submission API endpoints.
        /// Handles queue times and goon sightings submissions.
        /// </summary>
        internal interface ITarkovDevAPI
        {
            [Post("/queue")]
            Task<DataSubmissionResponse> SubmitQueueTime([Body] QueueTimeBody body);
            [Post("/goons")]
            Task<DataSubmissionResponse> SubmitGoonsSighting([Body] GoonsBody body);
        }
        private static ITarkovDevAPI api = RestService.For<ITarkovDevAPI>("https://manager.tarkov.dev/api");

        /// <summary>
        /// Interface for the Tarkov.dev player-related API endpoints.
        /// Provides functionality to search players and retrieve player profiles.
        /// </summary>
        internal interface ITarkovDevPlayersAPI
        {
            [Get("/name/{name}")]
            Task<List<PlayerSearchResult>> SearchName(string name);
            [Get("/account/{accountId}")]
            Task<PlayerProfileResult> GetProfile(int accountId);
        }
        private static ITarkovDevPlayersAPI playersApi = RestService.For<ITarkovDevPlayersAPI>("https://player.tarkov.dev");

        // Timer for automatic data updates, runs every 20 minutes
        private static readonly System.Timers.Timer updateTimer = new()
        {
            AutoReset = true,
            Enabled = false,
            Interval = TimeSpan.FromMinutes(20).TotalMilliseconds
        };

        // Static collections to store game data
        public static List<Task> Tasks { get; private set; } = new();          // Game tasks/quests
        public static List<Map> Maps { get; private set; } = new();           // Game maps
        public static List<Item> Items { get; private set; } = new();         // In-game items
        public static List<Trader> Traders { get; private set; } = new();     // Game traders
        public static List<HideoutStation> Stations { get; private set; } = new(); // Hideout stations
        public static List<PlayerLevel> PlayerLevels { get; private set; } = new(); // Experience levels
        public static DateTime ScavAvailableTime { get; set; } = DateTime.Now; // Next available scav run time

        /// <summary>
        /// Initializes static data when class is loaded
        /// </summary>
        static TarkovDev()
        {
            // Load ScavAvailableTime from settings if available
            if (!string.IsNullOrEmpty(Properties.Settings.Default.scavAvailableTime))
            {
                try
                {
                    ScavAvailableTime = DateTime.Parse(Properties.Settings.Default.scavAvailableTime);
                }
                catch
                {
                    // If parsing fails, keep the default (now)
                }
            }
        }

        /// <summary>
        /// Initializes the TarkovDev API data and starts automatic updates.
        /// This ensures all game data is loaded at application startup.
        /// </summary>
        public static async System.Threading.Tasks.Task Initialize()
        {
            try
            {
                await UpdateApiData();
                await GetPlayerLevels();
                StartAutoUpdates();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing TarkovDev API data: {ex.Message}");
                // Still attempt to start automatic updates even if initial load fails
                StartAutoUpdates();
            }
        }

        /// <summary>
        /// Fetches all available tasks/quests from the Tarkov.dev API.
        /// Includes task details, requirements, and fail conditions.
        /// </summary>
        public async static Task<List<Task>> GetTasks()
        {
            var request = new GraphQL.GraphQLRequest()
            {
                Query = @"
                    query TarkovMonitorTasks {
                        tasks {
                            id
                            name
                            normalizedName
                            wikiLink
                            restartable
                            failConditions {
                              ...on TaskObjectiveTaskStatus {
                                task {
                                  id
                                }
                                status
                              }
                            }
                        }
                    }
                "
            };
            var response = await client.SendQueryAsync<TasksResponse>(request);
            Tasks = response.Data.Tasks;
            return Tasks;
        }

        /// <summary>
        /// Retrieves all available maps and their associated boss spawn information.
        /// Maps are sorted alphabetically by name.
        /// </summary>
        public async static Task<List<Map>> GetMaps()
        {
            var request = new GraphQL.GraphQLRequest()
            {
                Query = @"
                    query TarkovMonitorMaps {
                        maps {
                            id
                            name
                            nameId
                            normalizedName
                            bosses {
                                boss {
                                    normalizedName
                                }
                                escorts {
                                    boss {
                                        normalizedName
                                    }
                                }
                            }
                        }
                    }
                "
            };
            var response = await client.SendQueryAsync<MapsResponse>(request);
            Maps = response.Data.Maps;
            Maps.Sort((a, b) => a.Name.CompareTo(b.Name));
            return Maps;
        }

        /// <summary>
        /// Fetches all items from the game, including their properties, dimensions, and images.
        /// Special handling for weapon presets to use their correct dimensions and images.
        /// </summary>
        public async static Task<List<Item>> GetItems()
        {
            var request = new GraphQL.GraphQLRequest()
            {
                Query = @"
                    query TarkovMonitorItems {
                        items {
                            id
                            name
                            width
                            height
                            link
                            iconLink
                            gridImageLink
                            image512pxLink
                            types
                            properties {
                                ...on ItemPropertiesWeapon {
                                    defaultPreset { 
                                        iconLink 
                                        gridImageLink
                                        width
                                        height
                                    }
                                }
                            }
                        }
                    }
                "
            };
            var response = await client.SendQueryAsync<ItemsResponse>(request);
            Items = response.Data.Items;
            foreach (var item in Items)
            {
                if (item.Types.Contains("gun"))
                {
                    if (item.Properties?.DefaultPreset != null)
                    {
                        item.Width = item.Properties.DefaultPreset.Width;
                        item.Height = item.Properties.DefaultPreset.Height;
                        item.IconLink = item.Properties.DefaultPreset.IconLink;
                        item.GridImageLink = item.Properties.DefaultPreset.GridImageLink;
                    }
                }
            }
            return Items;
        }

        /// <summary>
        /// Retrieves all traders and their reputation levels.
        /// Includes special handling for Fence trader reputation affecting scav cooldowns.
        /// </summary>
        public async static Task<List<Trader>> GetTraders()
        {
            var request = new GraphQL.GraphQLRequest()
            {
                Query = @"
                    query TarkovMonitorTraders {
                        traders {
                            id
                            name
                            normalizedName 
                            reputationLevels {
                                ...on TraderReputationLevelFence {
                                    minimumReputation
                                    scavCooldownModifier
                                }
                            }
                        }
                    }
                "
            };
            var response = await client.SendQueryAsync<TradersResponse>(request);
            Traders = response.Data.Traders;
            return Traders;
        }

        /// <summary>
        /// Fetches all hideout stations and their upgrade levels.
        /// Includes station bonuses and requirements.
        /// </summary>
        public async static Task<List<HideoutStation>> GetHideout()
        {
            var request = new GraphQL.GraphQLRequest()
            {
                Query = @"
                    query TarkovMonitorHideoutStations {
                        hideoutStations {
                            id
                            name
                            normalizedName
                            levels {
                                id
                                level
                                bonuses {
                                    ...on HideoutStationBonus {
                                        type
                                        name
                                        value
                                    }
                                }
                            }
                        }
                    }
                "
            };
            var response = await client.SendQueryAsync<HideoutResponse>(request);
            Stations = response.Data.HideoutStations;
            return Stations;
        }

        /// <summary>
        /// Updates all API data concurrently by fetching tasks, maps, items, traders, and hideout information.
        /// </summary>
        public async static System.Threading.Tasks.Task UpdateApiData()
        {
            List<System.Threading.Tasks.Task> tasks = new() {
                GetTasks(),
                GetMaps(),
                GetItems(),
                GetTraders(),
                GetHideout(),
            };
            await System.Threading.Tasks.Task.WhenAll(tasks);
        }

        /// <summary>
        /// Retrieves the experience requirements for all player levels.
        /// </summary>
        public async static Task<List<PlayerLevel>> GetPlayerLevels()
        {
            var request = new GraphQL.GraphQLRequest()
            {
                Query = @"
                    query TarkovMonitorPlayerLevels {
                        playerLevels {
                            level
                            exp
                        }
                    }
                "
            };
            var response = await client.SendQueryAsync<PlayerLevelsResponse>(request);
            PlayerLevels = response.Data.PlayerLevels;
            return PlayerLevels;
        }

        /// <summary>
        /// Submits queue time data to the API for matchmaking statistics.
        /// </summary>
        /// <param name="mapNameId">The ID of the map</param>
        /// <param name="queueTime">Time spent in queue in seconds</param>
        /// <param name="type">Queue type</param>
        /// <param name="gameMode">Game mode (PMC/Scav)</param>
        /// <returns>API response indicating submission status</returns>
        public async static Task<DataSubmissionResponse> PostQueueTime(string mapNameId, int queueTime, string type, ProfileType gameMode)
        {
            try
            {
                return await api.SubmitQueueTime(new QueueTimeBody() { Map = mapNameId, Time = queueTime, Type = type, GameMode = gameMode.ToString().ToLower() });
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception($"Invalid Queue API response code ({ex.StatusCode}): {ex.Message}");
                }
                throw new Exception($"Queue API exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Queue API error: {ex.Message}");
            }
        }

        /// <summary>
        /// Submits a goon squad sighting to the API.
        /// </summary>
        /// <param name="mapNameId">The ID of the map where goons were spotted</param>
        /// <param name="date">Time of the sighting</param>
        /// <param name="accountId">Player's account ID</param>
        /// <param name="profileType">Player's profile type (PMC/Scav)</param>
        /// <returns>API response indicating submission status</returns>
        public async static Task<DataSubmissionResponse> PostGoonsSighting(string mapNameId, DateTime date, int accountId, ProfileType profileType)
        {
            try
            {
                return await api.SubmitGoonsSighting(new GoonsBody() { Map = mapNameId, GameMode = profileType.ToString().ToLower(), Timestamp = ((DateTimeOffset)date).ToUnixTimeMilliseconds(), AccountId = accountId });
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception($"Invalid Goons API response code ({ex.StatusCode}): {ex.Message}");
                }
                throw new Exception($"Goons API exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Goons API error: {ex.Message}");
            }
        }

        /// <summary>
        /// Retrieves a player's current experience points from the API.
        /// </summary>
        /// <param name="accountId">Player's account ID</param>
        /// <returns>Player's current experience points</returns>
        public async static Task<int> GetExperience(int accountId)
        {
            try
            {
                var profile = await playersApi.GetProfile(accountId);
                if (profile.Err != null)
                {
                    throw new Exception(profile.Errmsg);
                }
                if (profile?.Info == null)
                {
                    return 0;
                }
                return profile.Info.Experience;
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception($"Invalid Players API response code ({ex.StatusCode}): {ex.Message}");
                }
                throw new Exception($"Players API exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Players API error: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculates a player's level based on their total experience points.
        /// </summary>
        /// <param name="experience">Total experience points</param>
        /// <returns>Player's current level</returns>
        public static int GetLevel(int experience)
        {
            if (experience == 0)
            {
                return 0;
            }
            var totalExp = 0;
            for (var i = 0; i < PlayerLevels.Count; i++)
            {
                var levelData = PlayerLevels[i];
                totalExp += levelData.Exp;
                if (totalExp == experience)
                {
                    return levelData.Level;
                }
                if (totalExp > experience)
                {
                    return PlayerLevels[i - 1].Level;
                }
            }
            return PlayerLevels[PlayerLevels.Count - 1].Level;
        }

        /// <summary>
        /// Enables automatic data updates using the update timer.
        /// Updates will occur every 20 minutes.
        /// </summary>
        public static void StartAutoUpdates()
        {
            updateTimer.Enabled = true;
            updateTimer.Elapsed += UpdateTimer_Elapsed;
        }

        // Event handler for the update timer
        private static async void UpdateTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            await UpdateApiData();
        }

        // Response classes for API data

        /// <summary>
        /// Response class for tasks/quests data
        /// </summary>
        public class TasksResponse
        {
            public required List<Task> Tasks { get; set; }
        }

        /// <summary>
        /// Represents a game task/quest with its properties and requirements
        /// </summary>
        public class Task
        {
            public required string Id { get; set; }
            public required string Name { get; set; }
            public required string NormalizedName { get; set; }
            public string? WikiLink { get; set; }
            public bool Restartable { get; set; }
            public required List<TaskFailCondition> FailConditions { get; set; }
        }

        /// <summary>
        /// Represents a task reference used in fail conditions
        /// </summary>
        public class TaskFragment
        {
            public required string Id { get; set; }
        }

        /// <summary>
        /// Represents conditions that cause a task to fail
        /// </summary>
        public class TaskFailCondition
        {
            public required TaskFragment Task { get; set; }
            public required List<string> Status { get; set; }
        }

        /// <summary>
        /// Response class for map data
        /// </summary>
        public class MapsResponse
        {
            public required List<Map> Maps { get; set; }
        }

        /// <summary>
        /// Represents a game map and its properties
        /// </summary>
        public class Map
        {
            public required string Id { get; set; }
            public required string Name { get; set; }
            public required string NameId { get; set; }
            public required string NormalizedName { get; set; }
            public required List<BossSpawn> Bosses { get; set; }
            public bool HasGoons()
            {
                List<string> goons = new() { "death-knight", "big-pipe", "birdeye" };
                return Bosses.Any(b => goons.Contains(b.Boss.NormalizedName) || b.Escorts.Any(e => goons.Contains(e.Boss.NormalizedName)));
            }
        }
        public class BossEscort
        {
            public required Boss Boss { get; set; }
        }
        public class BossSpawn
        {
            public required Boss Boss { get; set; }
            public required List<BossEscort> Escorts { get; set; }
        }
        public class Boss
        {
            public required string NormalizedName { get; set; }
        }
        /// <summary>
        /// Response class for item data
        /// </summary>
        public class ItemsResponse
        {
            public required List<Item> Items { get; set; }
        }
        /// <summary>
        /// Represents an in-game item and its properties
        /// </summary>
        public class Item
        {
            public required string Id { get; set; }
            public required string Name { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public required string Link { get; set; }
            public required string IconLink { get; set; }
            public required string GridImageLink { get; set; }
            public required string Image512pxLink { get; set; }
            public required List<string> Types { get; set; }
            public ItemProperties? Properties { get; set; }
        }
        /// <summary>
        /// Represents special properties for items (like weapon presets)
        /// </summary>
        public class ItemProperties
        {
            public ItemPropertiesDefaultPreset? DefaultPreset { get; set; }
        }
        /// <summary>
        /// Represents default preset configuration for weapons
        /// </summary>
        public class ItemPropertiesDefaultPreset
        {
            public required string IconLink { get; set; }
            public required string GridImageLink { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        /// <summary>
        /// Response class for trader data
        /// </summary>
        public class TradersResponse
        {
            public required List<Trader> Traders { get; set; }
        }
        /// <summary>
        /// Represents a trader and their properties
        /// </summary>
        public class Trader
        {
            public required string Id { get; set; }
            public required string Name { get; set; }
            public required string NormalizedName { get; set; }
            public required List<TraderReputationLevel> ReputationLevels { get; set; }
        }
        /// <summary>
        /// Represents a trader's reputation level and its benefits
        /// </summary>
        public class TraderReputationLevel
        {
            public int minimumReputation { get; set; }
            public decimal ScavCooldownModifier { get; set; }
        }

        /// <summary>
        /// Response class for hideout data
        /// </summary>
        public class HideoutResponse
        {
            public required List<HideoutStation> HideoutStations { get; set; }
        }
        /// <summary>
        /// Represents a hideout station and its properties
        /// </summary>
        public class HideoutStation
        {
            public required string Id { get; set; }
            public required string Name { get; set; }
            public required string NormalizedName { get; set; }
            public required List<StationLevel> Levels { get; set; }
        }
        /// <summary>
        /// Represents a level of a hideout station
        /// </summary>
        public class StationLevel
        {
            public required string Id { get; set; }
            public int Level { get; set; }
            public required List<StationBonus> Bonuses { get; set; }
        }
        /// <summary>
        /// Represents a bonus provided by a hideout station
        /// </summary>
        public class StationBonus
        {
            public required string Type { get; set; }
            public required string Name { get; set; }
            public decimal Value { get; set; }
        }

        /// <summary>
        /// Response class for player level data
        /// </summary>
        public class PlayerLevelsResponse
        {
            public required List<PlayerLevel> PlayerLevels { get; set; }
        }
        /// <summary>
        /// Represents a player level and its experience requirement
        /// </summary>
        public class PlayerLevel
        {
            public int Level { get; set; }
            public int Exp { get; set; }
        }

        /// <summary>
        /// Request body for submitting queue times
        /// </summary>
        public class QueueTimeBody
        {
            public required string Map { get; set; }
            public int Time { get; set; }
            public required string Type { get; set; }
            public required string GameMode { get; set; }
        }

        /// <summary>
        /// Generic response for data submissions
        /// </summary>
        public class DataSubmissionResponse
        {
            public required string Status { get; set; }
        }

        /// <summary>
        /// Request body for submitting goon sightings
        /// </summary>
        public class GoonsBody
        {
            public required string Map { get; set; }
            public required string GameMode { get; set; }
            public long Timestamp { get; set; }
            public int AccountId { get; set; }
        }

        /// <summary>
        /// Base response class for player API requests
        /// </summary>
        public class PlayerApiResponse
        {
            public int? Err { get; set; }
            public string? Errmsg { get; set; }
        }

        /// <summary>
        /// Response class for player search results
        /// </summary>
        public class PlayerSearchResult
        {
            public int Aid { get; set; }
            public required string Name { get; set; }
        }

        /// <summary>
        /// Response class for player profile data
        /// </summary>
        public class PlayerProfileResult
        {
            public int? Err { get; set; }
            public string? Errmsg { get; set; }
            public PlayerProfileInfo? Info { get; set; }
        }
        /// <summary>
        /// Contains detailed player profile information
        /// </summary>
        public class PlayerProfileInfo
        {
            public int Experience { get; set; }
        }

        /// <summary>
        /// Calculates the scav cooldown time in seconds based on hideout bonuses and scav karma.
        /// Takes into account:
        /// - Base timer (1500 seconds)
        /// - Hideout module bonuses that reduce cooldown
        /// - Fence reputation (scav karma) modifiers
        /// </summary>
        /// <returns>The total cooldown time in seconds</returns>
        public static int ScavCooldownSeconds()
        {
            decimal baseTimer = 1500;
            decimal hideoutBonus = 0;
            foreach (var station in Stations)
            {
                foreach (var level in station.Levels)
                {
                    var cooldownBonus = level.Bonuses.Find(b => b.Type == "ScavCooldownTimer");
                    if (cooldownBonus == null)
                    {
                        continue;
                    }
                    if (TarkovTracker.Progress == null)
                    {
                        continue;
                    }
                    var built = TarkovTracker.Progress.Data.HideoutModulesProgress.Find(m => m.Id == level.Id && m.Complete);
                    if (built == null)
                    {
                        continue;
                    }
                    hideoutBonus += Math.Abs(cooldownBonus.Value);
                }
            }
            decimal karmaBonus = 1;
            foreach (var trader in Traders)
            {
                foreach (var repLevel in trader.ReputationLevels)
                {
                    if (Properties.Settings.Default.scavKarma >= repLevel.minimumReputation)
                    {
                        karmaBonus = repLevel.ScavCooldownModifier;
                    }
                }
            }
            decimal coolDown = baseTimer * karmaBonus;
            //System.Diagnostics.Debug.WriteLine($"{hideoutBonus} {karmaBonus} {coolDown}");
            return (int)Math.Round(coolDown - (coolDown * hideoutBonus));
        }

        /// <summary>
        /// Resets the scav cooldown timer to the current time plus the calculated cooldown period.
        /// </summary>
        /// <returns>The number of seconds until the scav cooldown is complete</returns>
        public static int ResetScavCoolDown()
        {
            var cooldownSeconds = ScavCooldownSeconds();
            ScavAvailableTime = DateTime.Now.AddSeconds(cooldownSeconds);

            // Save to settings
            Properties.Settings.Default.scavAvailableTime = ScavAvailableTime.ToString("o");
            Properties.Settings.Default.Save();

            return cooldownSeconds;
        }

        /// <summary>
        /// Gets the remaining time in seconds until the next Scav raid is available
        /// </summary>
        /// <returns>Seconds until next Scav raid is available, or 0 if available now</returns>
        public static int GetRemainingScavCooldownSeconds()
        {
            // Ensure ScavAvailableTime is not default
            if (ScavAvailableTime == DateTime.MinValue || ScavAvailableTime == default)
            {
                return 0;
            }

            var remainingTime = ScavAvailableTime - DateTime.Now;
            return remainingTime.TotalSeconds > 0 ? (int)remainingTime.TotalSeconds : 0;
        }
    }
}
