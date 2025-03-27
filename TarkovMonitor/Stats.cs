using System.Data.SQLite;
// do not upgrade to 1.0.118
// newer version throws an error after being compiled as single file assembly

namespace TarkovMonitor
{
    internal class Stats
    {
        public static string DatabasePath => Path.Join(Application.UserAppDataPath, "TarkovMonitor.db");
        public static string ConnectionString => $"Data Source={DatabasePath};Version=3;";
        private static readonly SQLiteConnection Connection;
        /// <summary>
        /// Initializes the <see cref="Stats"/> class.
        /// </summary>
        /// <remarks>
        /// Creates the database tables if they do not exist, and updates the database if necessary
        /// </remarks>
        static Stats()
        {
            Connection = new SQLiteConnection(ConnectionString);
            Connection.Open();

            List<string> createTableCommands = new()
            {
                "CREATE TABLE IF NOT EXISTS flea_sales (id INTEGER PRIMARY KEY, profile_id VARCHAR(24), item_id CHAR(24), buyer VARCHAR(14), count INT, currency CHAR(24), price INT, time TIMESTAMP DEFAULT CURRENT_TIMESTAMP);",
                "CREATE TABLE IF NOT EXISTS raids (id INTEGER PRIMARY KEY, profile_id VARCHAR(24), map VARCHAR(24), raid_type INT, queue_time DECIMAL(6,2), raid_id VARCHAR(24), time TIMESTAMP DEFAULT CURRENT_TIMESTAMP);",
            };
            foreach (var commandText in createTableCommands)
            {
                using var command = new SQLiteCommand(Connection);
                command.CommandText = commandText;
                command.ExecuteNonQuery();
            }

            UpdateDatabase();
        }

        /// <summary>
        /// Deletes all data from all tables in the database.
        /// <para>
        /// This method disables foreign key constraints, deletes all data from all tables,
        /// and then re-enables foreign key constraints.
        /// </para>
        /// </summary>
        public static void ClearData()
        {
            Query("PRAGMA foreign_keys=off;");
            var reader = Query("SELECT name FROM sqlite_master WHERE type='table';");
            while (reader.Read())
            {
                var tableName = reader.GetString(0);
                Query($"DELETE FROM {tableName};");
            }
            Query("PRAGMA foreign_keys=on;");
        }

        /// <summary>
        /// Executes a query on the database and returns the result.
        /// </summary>
        /// <param name="query">The SQL query to execute.</param>
        /// <param name="parameters">Parameters to replace in the query.</param>
        /// <returns>A <see cref="SQLiteDataReader"/> containing the result of the query.</returns>
        private static SQLiteDataReader Query(string query, Dictionary<string, object> parameters)
        {
            using var command = new SQLiteCommand(Connection);
            command.CommandText = query;
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue($"@{parameter.Key}", parameter.Value);
            }
            return command.ExecuteReader();
        }
        /// <summary>
        /// Executes a query on the database and returns the result.
        /// </summary>
        /// <param name="query">The SQL query to execute.</param>
        /// <returns>A <see cref="SQLiteDataReader"/> containing the result of the query.</returns>
        private static SQLiteDataReader Query(string query)
        {
            return Query(query, new());
        }

        /// <summary>
        /// Adds a flea market sale to the database.
        /// </summary>
        /// <param name="e">The message log content of the sale.</param>
        /// <param name="profile">The profile of the sale.</param>
        public static void AddFleaSale(FleaSoldMessageLogContent e, Profile profile)
        {
            var sql = "INSERT INTO flea_sales(profile_id, item_id, buyer, count, currency, price) VALUES(@profile_id, @item_id, @buyer, @count, @currency, @price);";
            var parameters = new Dictionary<string, object>
            {
                {
                    "profile_id", profile.Id
                },
                {
                    "item_id", e.SoldItemId
                },
                {
                    "buyer", e.Buyer
                },
                {
                    "count", e.SoldItemCount
                },
                {
                    "currency", e.ReceivedItems.ElementAt(0).Key
                },
                {
                    "price", e.ReceivedItems.ElementAt(0).Value
                },
            };
            Query(sql, parameters);
        }
        /// <summary>
        /// Returns the total amount of money (in the specified currency) that has been earned through flea market sales.
        /// </summary>
        /// <param name="currency">The currency to get the total for.</param>
        /// <returns>The total amount of money earned through flea market sales in the specified currency.</returns>
        public static int GetTotalSales(string currency)
        {
            var reader = Query("SELECT SUM(price) as total FROM flea_sales WHERE currency = @currency", new() { { "currency", currency } });
            while (reader.Read())
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }
                return reader.GetInt32(0);
            }
            return 0;
        }
        /// <summary>
        /// Adds a new raid to the database.
        /// </summary>
        /// <param name="e">The raid event to add.</param>
        public static void AddRaid(RaidInfoEventArgs e)
        {
            var sql = "INSERT INTO raids(profile_id, map, raid_type, queue_time, raid_id) VALUES (@profile_id, @map, @raid_type, @queue_time, @raid_id);";
            var parameters = new Dictionary<string, object> {
                {
                    "profile_id", e.Profile.Id
                },
                {
                    "map", e.RaidInfo.Map ?? string.Empty
                },
                {
                    "raid_type", e.RaidInfo.RaidType
                },
                {
                    "queue_time", e.RaidInfo.QueueTime
                },
                {
                    "raid_id", e.RaidInfo.RaidId
                },
            };
            Query(sql, parameters);
        }
        /// <summary>
        /// Returns the total number of raids that have been done on the given map.
        /// </summary>
        /// <param name="mapNameId">The nameId of the map to get the total for.</param>
        /// <returns>The total number of raids done on the given map.</returns>
        public static int GetTotalRaids(string mapNameId)
        {
            var reader = Query("SELECT COUNT(id) as total FROM raids WHERE map = @map", new() { { "map", mapNameId } });
            while (reader.Read())
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }
                return reader.GetInt32(0);
            }
            return 0;
        }
        /// <summary>
        /// Returns a dictionary where the keys are the names of the maps, and the values are the total number of raids of the given type that have been done on that map.
        /// </summary>
        /// <param name="raidType">The type of raid to get the totals for.</param>
        /// <returns>A dictionary of map names and totals.</returns>
        public static Dictionary<string, int> GetTotalRaidsPerMap(RaidType raidType)
        {
            Dictionary<string, int> mapTotals = new();
            var reader = Query("SELECT map, COUNT(id) as total FROM raids WHERE raid_type = @raid_type GROUP BY map", new() { { "raid_type", raidType } });
            while (reader.Read())
            {
                if (reader.IsDBNull(1))
                {
                    mapTotals[reader.GetString(0)] = 0;
                    continue;
                }
                mapTotals[reader.GetString(0)] = reader.GetInt32(1);
            }
            Dictionary<string, int> raidsPerMap = new();
            foreach (var map in TarkovDev.Maps)
            {
                raidsPerMap[map.Name] = 0;
                if (mapTotals.ContainsKey(map.NameId))
                {
                    raidsPerMap[map.Name] = mapTotals[map.NameId];
                }
            }
            return raidsPerMap;
        }

        /// <summary>
        /// Updates the database schema if it's not up to date.
        /// </summary>
        /// <remarks>
        /// Checks if the profile_id field exists in each of the raid and flea sales tables.
        /// If it doesn't exist, adds the field.
        /// </remarks>
        private static void UpdateDatabase()
        {
            List<string> db_tables = new() { "raids", "flea_sales" };
            foreach (var tableName in db_tables)
            {
                var profileIdFieldExists = false;
                var result = Query($"PRAGMA table_info({tableName});");
                while (result.Read())
                {
                    for (int i = 0; i < result.FieldCount; i++)
                    {
                        if (result.GetName(i) != "name")
                        {
                            continue;
                        }
                        if (result.GetString(i) == "profile_id")
                        {
                            profileIdFieldExists = true;
                            break;
                        }
                    }
                }
                if (!profileIdFieldExists)
                {
                    using var command = new SQLiteCommand(Connection);
                    command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN profile_id VARCHAR(24)";
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
