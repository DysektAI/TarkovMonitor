using System.Text.Json.Nodes;
using Websocket.Client;

namespace TarkovMonitor
{
    /// <summary>
    /// Static class that handles WebSocket communication with the Tarkov.dev server.
    /// This class manages real-time updates and commands for the Tarkov Monitor application.
    /// </summary>
    internal static class SocketClient
    {
        /// <summary>
        /// Event that is triggered when an exception occurs during WebSocket operations.
        /// Subscribers can handle these exceptions for logging or user notification purposes.
        /// </summary>
        public static event EventHandler<ExceptionEventArgs>? ExceptionThrown;

        /// <summary>
        /// The WebSocket server URL for the Tarkov.dev service.
        /// Production endpoint for live environment.
        /// </summary>
        private static readonly string wsUrl = "wss://socket.tarkov.dev";

        // Development endpoint for local testing (commented out)
        //private static readonly string wsUrl = "ws://localhost:8080";
        //private static WebsocketClient? socket;

        /// <summary>
        /// Sends a JSON message to the WebSocket server.
        /// Creates a new WebSocket connection for each message, sends the data, and then disposes of the connection.
        /// </summary>
        /// <param name="message">The JSON object containing the message to send</param>
        /// <returns>A Task representing the asynchronous operation</returns>
        /// <remarks>
        /// The method will return immediately if no remote ID is configured in the application settings.
        /// Each message is sent with a session ID that includes the remote ID and "-tm" suffix.
        /// </remarks>
        public static async Task Send(JsonObject message)
        {
            // Get the remote ID from application settings
            var remoteid = Properties.Settings.Default.remoteId;
            if (remoteid == null || remoteid == "")
            {
                return;
            }

            // Add session ID to the message
            message["sessionID"] = remoteid;

            // Create a new WebSocket client with the session ID in the connection URL
            WebsocketClient socket = new(new Uri(wsUrl + $"?sessionid={remoteid}-tm"));

            /* Commented out ping-pong handler implementation
            socket.MessageReceived.Subscribe(msg => {
                if (msg.Text == null)
                {
                    return;
                }
                var message = JsonNode.Parse(msg.Text);
                if (message == null)
                {
                    return;
                }
                if (message["type"]?.ToString() == "ping")
                {
                    socket.Send(new JsonObject
                    {
                        ["type"] = "pong"
                    }.ToJsonString());
                }
            });*/

            // Start the WebSocket connection
            await socket.Start();
            try
            {
                // Send the message immediately
                await socket.SendInstant(message.ToJsonString());
            }
            catch
            {
                // Rethrow any exceptions that occur during sending
                throw;
            }
            finally
            {
                // Always dispose of the socket connection after use
                socket.Dispose();
            }
        }

        /// <summary>
        /// Sends a player position update to the websocket server.
        /// This method is called when the player's position changes in the game.
        /// </summary>
        /// <param name="e">Event arguments containing the player's position and raid information</param>
        /// <remarks>
        /// The method performs the following steps:
        /// 1. Validates the map name against known maps
        /// 2. Creates a JSON payload with position coordinates
        /// 3. Sends the update to the server
        /// 4. Handles any exceptions that occur during the process
        /// 
        /// The method will return without sending if the map name is not recognized.
        /// </remarks>
        public static async Task UpdatePlayerPosition(PlayerPositionEventArgs e)
        {
            // Find the normalized map name from the raid information
            var map = TarkovDev.Maps.Find(m => m.NameId == e.RaidInfo.Map)?.NormalizedName;
            if (map == null && e.RaidInfo.Map != null)
            {
                return;
            }

            // Construct the position update payload
            var payload = new JsonObject
            {
                ["type"] = "command",
                ["data"] = new JsonObject
                {
                    ["type"] = "playerPosition",
                    ["map"] = map,
                    ["position"] = new JsonObject
                    {
                        ["x"] = e.Position.X,
                        ["y"] = e.Position.Y,
                        ["z"] = e.Position.Z
                    }
                }
            };

            try
            {
                await Send(payload);
            }
            catch (Exception ex)
            {
                // Trigger the exception event with context about the operation
                ExceptionThrown?.Invoke(payload, new(ex, "updating player position"));
            }
        }

        /// <summary>
        /// Sends a command to the websocket server to change the currently displayed map.
        /// This method is typically called when the user wants to view a different map in the interface.
        /// </summary>
        /// <param name="map">The map object containing information about the target map</param>
        /// <remarks>
        /// The method constructs a map navigation command and sends it to the server.
        /// Any exceptions during the process are caught and reported through the ExceptionThrown event.
        /// </remarks>
        public static async Task NavigateToMap(TarkovDev.Map map)
        {
            // Construct the map navigation payload
            var payload = new JsonObject
            {
                ["type"] = "command",
                ["data"] = new JsonObject
                {
                    ["type"] = "map",
                    ["value"] = map.NormalizedName
                }
            };

            try
            {
                await Send(payload);
            }
            catch (Exception ex)
            {
                // Trigger the exception event with context about the map navigation
                ExceptionThrown?.Invoke(payload, new(ex, $"navigating to map {map.Name}"));
            }
        }
    }
}
