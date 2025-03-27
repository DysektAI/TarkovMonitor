using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;
using System.Globalization;
using Microsoft.Win32;

namespace TarkovMonitor
{
    /// <summary>
    /// GameWatcher is responsible for monitoring and tracking the Escape from Tarkov game process,
    /// its log files, and game events. It provides real-time information about the game state,
    /// raid status, and player actions.
    /// </summary>
    internal partial class GameWatcher
    {
        // Add the generated regex patterns at class level
        [GeneratedRegex(@"\d{4}-\d{2}-\d{2}\[\d{2}-\d{2}\]_(?<position>.+) \(\d\)\.png")]
        private static partial Regex ScreenshotPositionRegex();

        [GeneratedRegex(@"(?<x>-?[\d.]+), (?<y>-?[\d.]+), (?<z>-?[\d.]+)_.*")]
        private static partial Regex PositionDetailsRegex();

        // Core process and monitoring components
        private Process? process;  // Represents the EFT game process
        private readonly System.Timers.Timer processTimer;  // Timer for checking game process status
        private readonly FileSystemWatcher logFileCreateWatcher;  // Watches for new log file creation
        private readonly FileSystemWatcher screenshotWatcher;  // Watches for new screenshot creation

        // Path management
        private string _logsPath = "";  // Cached path to game logs

        // Current state tracking
        public Profile CurrentProfile { get; set; } = new();  // Current player profile
        public bool InitialLogsRead { get; private set; } = false;  // Flag indicating if initial logs have been processed

        // Message logging
        private MessageLog? messageLog;

        // Set the message log from the DI container
        public void SetMessageLog(MessageLog log)
        {
            messageLog = log;
        }

        /// <summary>
        /// Gets or sets the path to the game logs directory. If not explicitly set,
        /// attempts to find it from user settings or registry.
        /// </summary>
        public string LogsPath
        {
            get
            {
                if (_logsPath != "")
                {
                    return _logsPath;
                }
                if (Properties.Settings.Default.customLogsPath != null && Properties.Settings.Default.customLogsPath != "")
                {
                    _logsPath = Properties.Settings.Default.customLogsPath;
                    return _logsPath;
                }
                try
                {
                    _logsPath = GetDefaultLogsFolder();
                }
                catch (Exception ex)
                {
                    ExceptionThrown?.Invoke(this, new ExceptionEventArgs(ex, "getting logs path"));
                }
                return _logsPath;
            }
            set
            {
                _logsPath = value;
                if (logFileCreateWatcher.EnableRaisingEvents)
                {
                    logFileCreateWatcher.Path = LogsPath;
                    _ = Task.Run(async () => await WatchLogsFolder(GetLatestLogFolder()));
                }
            }
        }

        /// <summary>
        /// Gets the current logs folder path based on active monitors
        /// </summary>
        public string CurrentLogsFolder
        {
            get
            {
                if (Monitors.Count == 0)
                {
                    return "";
                }
                try
                {
                    var logInfo = new FileInfo(Monitors[0].Path);
                    return logInfo.DirectoryName ?? "";
                }
                catch { }
                return "";
            }
        }

        // Game state tracking
        private readonly Dictionary<string, RaidInfo> Raids = new();  // Tracks active and completed raids

        /// <summary>
        /// Gets the path to the game's screenshots folder
        /// </summary>
        public string ScreenshotsPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Escape From Tarkov", "Screenshots");
            }
        }

        // Account tracking
        private int _accountId = 0;

        /// <summary>
        /// Gets the current account ID from logs. If not cached, attempts to find it in latest logs.
        /// </summary>
        public int AccountId
        {
            get
            {
                if (_accountId > 0)
                {
                    return _accountId;
                }
                List<LogDetails> details = GetLogDetails(GetLatestLogFolder());
                if (details.Count == 0)
                {
                    return 0;
                }
                _accountId = details[^1].AccountId;
                return details[^1].AccountId;
            }
        }

        // Log monitoring and event system
        internal readonly Dictionary<GameLogType, LogMonitor> Monitors;  // Active log monitors
        private RaidInfo raidInfo;  // Current raid information

        // Event declarations for various game states and actions
        public event EventHandler<NewLogDataEventArgs>? NewLogData;  // New log data received
        public event EventHandler<ExceptionEventArgs>? ExceptionThrown;  // Exception occurred
        public event EventHandler? GameStarted;  // Game process started

        // Group-related events
        public event EventHandler<LogContentEventArgs<GroupLogContent>>? GroupInviteAccept;
        public event EventHandler<LogContentEventArgs<GroupRaidSettingsLogContent>>? GroupRaidSettings;
        public event EventHandler<LogContentEventArgs<GroupMatchRaidReadyLogContent>>? GroupMemberReady;
        public event EventHandler? GroupDisbanded;
        public event EventHandler<LogContentEventArgs<GroupMatchUserLeaveLogContent>>? GroupUserLeave;

        // Raid-related events
        public event EventHandler? MapLoading;
        public event EventHandler<RaidInfoEventArgs>? MatchingStarted;
        public event EventHandler<RaidInfoEventArgs>? MatchFound;
        public event EventHandler<RaidInfoEventArgs>? MapLoaded;
        public event EventHandler<RaidInfoEventArgs>? MatchingAborted;
        public event EventHandler<RaidInfoEventArgs>? RaidStarting;
        public event EventHandler<RaidInfoEventArgs>? RaidStarted;
        public event EventHandler<RaidExitedEventArgs>? RaidExited;
        public event EventHandler<RaidInfoEventArgs>? RaidEnded;
        public event EventHandler<RaidInfoEventArgs>? ExitedPostRaidMenus;

        // Task-related events
        public event EventHandler<LogContentEventArgs<TaskStatusMessageLogContent>>? TaskModified;
        public event EventHandler<LogContentEventArgs<TaskStatusMessageLogContent>>? TaskStarted;
        public event EventHandler<LogContentEventArgs<TaskStatusMessageLogContent>>? TaskFailed;
        public event EventHandler<LogContentEventArgs<TaskStatusMessageLogContent>>? TaskFinished;

        // Market-related events
        public event EventHandler<LogContentEventArgs<FleaSoldMessageLogContent>>? FleaSold;
        public event EventHandler<LogContentEventArgs<FleaExpiredMessageLogContent>>? FleaOfferExpired;
        public event EventHandler<ManualFleaSoldEventArgs>? DirectFleaSold;

        // Player-related events
        public event EventHandler<PlayerPositionEventArgs>? PlayerPosition;
        public event EventHandler<ProfileEventArgs> ProfileChanged;
        public event EventHandler<ProfileEventArgs>? InitialReadComplete;
        public event EventHandler? LogParsed;

        // Profile tracking
        public Profile? Profile { get; set; }
        public string? Folder { get; set; }

        /// <summary>
        /// Gets the default logs folder path from the Windows Registry
        /// </summary>
        public static string GetDefaultLogsFolder()
        {
            using RegistryKey key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\EscapeFromTarkov") ?? throw new Exception("EFT install registry entry not found");
            return Path.Combine(key.GetValue("InstallLocation")?.ToString() ?? throw new Exception("InstallLocation registry value not found"), "Logs");
        }

        /// <summary>
        /// Initializes a new instance of the GameWatcher class
        /// Sets up file system watchers and timers for monitoring game activity
        /// </summary>
        public GameWatcher()
        {
            Monitors = new();
            raidInfo = new RaidInfo();

            // Initialize log file watcher
            logFileCreateWatcher = new FileSystemWatcher
            {
                Filter = "*.log",
                IncludeSubdirectories = true,
            };

            // Initialize process check timer
            processTimer = new System.Timers.Timer(TimeSpan.FromSeconds(30).TotalMilliseconds)
            {
                AutoReset = true,
                Enabled = false
            };

            // Initialize screenshot watcher
            screenshotWatcher = new FileSystemWatcher();
            ProfileChanged += delegate { };
        }

        /// <summary>
        /// Sets up the screenshot watcher to monitor for new screenshots taken in the game
        /// </summary>
        public void SetupScreenshotWatcher()
        {
            try
            {
                bool screensPathExists = Directory.Exists(ScreenshotsPath);
                string watchPath = screensPathExists ? ScreenshotsPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                screenshotWatcher.Path = watchPath;
                screenshotWatcher.IncludeSubdirectories = !screensPathExists;
                screenshotWatcher.Created -= ScreenshotWatcher_Created;
                screenshotWatcher.Created -= ScreenshotWatcher_FolderCreated;
                if (screensPathExists)
                {
                    screenshotWatcher.Filter = "*.png";
                    screenshotWatcher.Created += ScreenshotWatcher_Created;
                }
                else
                {
                    screenshotWatcher.Created += ScreenshotWatcher_FolderCreated;
                }
                screenshotWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                ExceptionThrown?.Invoke(this, new ExceptionEventArgs(ex, "initializing screenshot watcher"));
            }
        }

        /// <summary>
        /// Event handler for when the screenshots folder is created
        /// </summary>
        private void ScreenshotWatcher_FolderCreated(object sender, FileSystemEventArgs e)
        {
            if (e.FullPath == ScreenshotsPath)
            {
                SetupScreenshotWatcher();
            }
        }

        /// <summary>
        /// Event handler for when a new screenshot is created
        /// Parses the screenshot filename for position information and raises the PlayerPosition event
        /// </summary>
        private void ScreenshotWatcher_Created(object sender, FileSystemEventArgs e)
        {
            try
            {
                string filename = e.Name ?? "";
                var match = ScreenshotPositionRegex().Match(filename);
                if (!match.Success)
                {
                    return;
                }
                var position = PositionDetailsRegex().Match(match.Groups["position"].Value);
                if (!position.Success)
                {
                    return;
                }

                // Get raid information and raise position event
                var raid = raidInfo;
                if (raid.Map == null && Properties.Settings.Default.customMap != "")
                {
                    raid = new()
                    {
                        Map = Properties.Settings.Default.customMap,
                    };
                }
                if (raid.Map == null)
                {
                    return;
                }
                PlayerPosition?.Invoke(this, new(raid, CurrentProfile, new Position(position.Groups["x"].Value, position.Groups["y"].Value, position.Groups["z"].Value), filename));
                raid.Screenshots.Add(filename);
            }
            catch (Exception ex)
            {
                ExceptionThrown?.Invoke(this, new ExceptionEventArgs(ex, $"parsing screenshot {e.Name}"));
            }
        }

        /// <summary>
        /// Starts the GameWatcher, initializing log monitoring and process checking
        /// </summary>
        public async Task Start()
        {
            try
            {
                logFileCreateWatcher.Path = LogsPath;
                logFileCreateWatcher.Created += LogFileCreateWatcher_Created;
                logFileCreateWatcher.EnableRaisingEvents = true;
                processTimer.Elapsed += ProcessTimer_Elapsed;
                UpdateProcess();
                SetupScreenshotWatcher();
                processTimer.Enabled = true;
                if (Monitors.Count == 0)
                {
                    await WatchLogsFolder(GetLatestLogFolder());
                }
            }
            catch (Exception ex)
            {
                ExceptionThrown?.Invoke(this, new(ex, "starting game watcher"));
            }
        }

        /// <summary>
        /// Event handler for when new log files are created
        /// Sets up monitoring for application and notification logs
        /// </summary>
        private void LogFileCreateWatcher_Created(object sender, FileSystemEventArgs e)
        {
            string filename = e.Name ?? "";
            if (filename.Contains("application.log"))
            {
                _ = StartNewMonitor(e.FullPath);
                _accountId = 0;
            }
            if (filename.Contains("notifications.log"))
            {
                _ = StartNewMonitor(e.FullPath);
            }
        }

        /// <summary>
        /// Main event handler for processing new log data
        /// Parses log messages and raises appropriate events based on the content
        /// </summary>
        internal void GameWatcher_NewLogData(object? sender, NewLogDataEventArgs e)
        {
            try
            {
                NewLogData?.Invoke(this, e);

                // Direct check for flea market sales in the raw log data
                if (e.Data.Contains("items were bought by"))
                {
                    Console.WriteLine("\n=== DIRECT FLEA MARKET SALE DETECTED IN RAW LOG DATA ===");

                    // Add to message log directly to make sure it's visible in the UI
                    if (messageLog != null)
                    {
                        messageLog.AddMessage("DIRECT FLEA SALE DETECTED - Checking logs...", "flea");
                    }
                    else
                    {
                        // Fallback to reflection if messageLog wasn't set
                        var messageLogField = GetType().GetField("messageLog", System.Reflection.BindingFlags.Instance |
                                                        System.Reflection.BindingFlags.NonPublic);

                        if (messageLogField != null)
                        {
                            var msgLog = messageLogField.GetValue(this);
                            if (msgLog != null)
                            {
                                // Try to call AddMessage method
                                var addMethod = msgLog.GetType().GetMethod("AddMessage");
                                if (addMethod != null)
                                {
                                    addMethod.Invoke(msgLog, new object[] { "DIRECT FLEA SALE DETECTED - Checking logs...", "flea" });
                                }
                            }
                        }
                    }

                    // Use regex to extract product and buyer
                    var match = Regex.Match(e.Data, @"Your\s+(?<item>.+?)\s+\(x\d+\)\s+items were bought by\s+(?<buyer>.+?)[\.\n\r]");
                    if (match.Success)
                    {
                        string item = match.Groups["item"].Value.Trim();
                        string buyer = match.Groups["buyer"].Value.Trim();

                        Console.WriteLine($"DIRECT MATCH - Item: {item}, Buyer: {buyer}");

                        // Create a manual event arg and trigger event
                        var fleaSoldContent = new FleaSoldMessageLogContent();

                        // Try to set properties with reflection because they might be read-only
                        try
                        {
                            // Get the type
                            var type = typeof(FleaSoldMessageLogContent);

                            // Try to find fields instead of properties
                            var fields = type.GetFields(System.Reflection.BindingFlags.Instance |
                                                      System.Reflection.BindingFlags.NonPublic |
                                                      System.Reflection.BindingFlags.Public);

                            foreach (var field in fields)
                            {
                                if (field.Name.Contains("buyer", StringComparison.OrdinalIgnoreCase))
                                {
                                    field.SetValue(fleaSoldContent, buyer);
                                    Console.WriteLine($"Set buyer field: {field.Name}");
                                }
                                else if (field.Name.Contains("sold", StringComparison.OrdinalIgnoreCase) ||
                                        field.Name.Contains("item", StringComparison.OrdinalIgnoreCase))
                                {
                                    field.SetValue(fleaSoldContent, item);
                                    Console.WriteLine($"Set item field: {field.Name}");
                                }
                            }

                            // If we couldn't set via fields, try to access private property setters
                            var buyerProperty = type.GetProperty("Buyer",
                                System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.NonPublic |
                                System.Reflection.BindingFlags.Public);

                            var itemProperty = type.GetProperty("SoldItemId",
                                System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.NonPublic |
                                System.Reflection.BindingFlags.Public);

                            // Try to get the backing fields
                            if (buyerProperty != null)
                            {
                                var backingField = type.GetField($"<{buyerProperty.Name}>k__BackingField",
                                    System.Reflection.BindingFlags.Instance |
                                    System.Reflection.BindingFlags.NonPublic);

                                if (backingField != null)
                                {
                                    backingField.SetValue(fleaSoldContent, buyer);
                                    Console.WriteLine("Set buyer via backing field");
                                }
                            }

                            if (itemProperty != null)
                            {
                                var backingField = type.GetField($"<{itemProperty.Name}>k__BackingField",
                                    System.Reflection.BindingFlags.Instance |
                                    System.Reflection.BindingFlags.NonPublic);

                                if (backingField != null)
                                {
                                    backingField.SetValue(fleaSoldContent, item);
                                    Console.WriteLine("Set item via backing field");
                                }
                            }
                        }
                        catch (Exception rex)
                        {
                            Console.WriteLine($"Reflection error: {rex.Message}");
                        }

                        // Even if reflection failed, create a manual event
                        Console.WriteLine("Creating manual flea event");

                        // Create a manual log content event args
                        var args = new ManualFleaSoldEventArgs
                        {
                            BuyerName = buyer,
                            ItemName = item,
                            Timestamp = DateTime.Now
                        };

                        // Trigger a direct flea market sale event to skip serialization issues
                        DirectFleaSold?.Invoke(this, args);

                        // Also try the regular event if we were able to set some values
                        var handler = FleaSold;
                        if (handler != null)
                        {
                            try
                            {
                                var eventArgs = new LogContentEventArgs<FleaSoldMessageLogContent>()
                                {
                                    LogContent = fleaSoldContent,
                                    Profile = CurrentProfile
                                };

                                Console.WriteLine("Invoking regular FleaSold event");
                                handler.Invoke(this, eventArgs);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error invoking regular FleaSold: {ex.Message}");
                            }
                        }
                    }
                }

                // Regular expression pattern for parsing log entries
                var logPattern = @"(?<date>^\d{4}-\d{2}-\d{2}) (?<time>\d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2})\|(?<message>.+$)\s*(?<json>^{[\s\S]+?^})?";
                var logMessages = Regex.Matches(e.Data, logPattern, RegexOptions.Multiline);

#if DEBUG
                Debug.WriteLine("===log chunk start===");
                Debug.WriteLine(e.Data);
                Debug.WriteLine("===log chunk end===");
#endif

                foreach (Match logMessage in logMessages)
                {
                    // Parse timestamp from log message
                    var eventDate = new DateTime();
                    DateTime.TryParseExact(logMessage.Groups["date"].Value + " " + logMessage.Groups["time"].Value.Split(" ")[0],
                        "yyyy-MM-dd HH:mm:ss.fff",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal,
                        out eventDate);

                    var eventLine = logMessage.Groups["message"].Value;

                    // Process different types of log messages and raise appropriate events

                    // Session mode detection
                    if (eventLine.Contains("Session mode: "))
                    {
                        var modeMatch = Regex.Match(eventLine, @"Session mode: (?<mode>\w+)");
                        if (!modeMatch.Success)
                        {
                            continue;
                        }
                        CurrentProfile.Type = Enum.Parse<ProfileType>(modeMatch.Groups["mode"].Value, true);
                        raidInfo.ProfileType = CurrentProfile.Type;
                        continue;
                    }

                    // Profile selection detection
                    if (eventLine.Contains("SelectProfile ProfileId:"))
                    {
                        var profileIdMatch = Regex.Match(eventLine, @"SelectProfile ProfileId:(?<profileId>\w+)");
                        if (!profileIdMatch.Success)
                        {
                            continue;
                        }
                        CurrentProfile.Id = profileIdMatch.Groups["profileId"].Value;
                        if (!e.InitialRead)
                        {
                            if (raidInfo.StartedTime != null && raidInfo.EndedTime == null)
                            {
                                raidInfo.EndedTime = eventDate;
                                RaidEnded?.Invoke(this, new(raidInfo, CurrentProfile));
                            }
                            else
                            {
                                ProfileChanged?.Invoke(this, new(CurrentProfile));
                            }
                        }
                        continue;
                    }

                    // Skip processing remaining messages if this is initial read
                    if (e.InitialRead)
                    {
                        continue;
                    }

                    // Parse JSON content if present
                    var jsonString = "{}";
                    if (logMessage.Groups["json"].Success)
                    {
                        jsonString = logMessage.Groups["json"].Value;
                    }
                    var jsonNode = JsonNode.Parse(jsonString);

                    // Process various game events and raise corresponding events
                    if (eventLine.Contains("Got notification | GroupMatchInviteAccept"))
                    {
                        // GroupMatchInviteAccept occurs when someone you send an invite accepts
                        // GroupMatchInviteSend occurs when you receive an invite and either accept or decline
                        GroupInviteAccept?.Invoke(this, new LogContentEventArgs<GroupLogContent>() { LogContent = jsonNode?.AsObject().Deserialize<GroupLogContent>() ?? throw new Exception("Error parsing GroupEventArgs"), Profile = CurrentProfile });
                    }
                    if (eventLine.Contains("Got notification | GroupMatchUserLeave"))
                    {
                        // User left the group
                        GroupUserLeave?.Invoke(this, new LogContentEventArgs<GroupMatchUserLeaveLogContent>() { LogContent = jsonNode?.AsObject().Deserialize<GroupMatchUserLeaveLogContent>() ?? throw new Exception("Error parsing GroupMatchUserLeaveEventArgs"), Profile = CurrentProfile });
                    }
                    if (eventLine.Contains("Got notification | GroupMatchWasRemoved"))
                    {
                        // When the group is disbanded
                        GroupDisbanded?.Invoke(this, new());
                    }
                    if (eventLine.Contains("Got notification | GroupMatchRaidSettings"))
                    {
                        // Occurs when group leader invites members to be ready
                        GroupRaidSettings?.Invoke(this, new LogContentEventArgs<GroupRaidSettingsLogContent>() { LogContent = jsonNode?.AsObject().Deserialize<GroupRaidSettingsLogContent>() ?? throw new Exception("Error parsing GroupRaidSettingsEventArgs"), Profile = CurrentProfile });
                    }
                    if (eventLine.Contains("Got notification | GroupMatchRaidReady"))
                    {
                        // Occurs for each other member of the group when ready
                        GroupMemberReady?.Invoke(this, new LogContentEventArgs<GroupMatchRaidReadyLogContent>() { LogContent = jsonNode?.AsObject().Deserialize<GroupMatchRaidReadyLogContent>() ?? throw new Exception("Error parsing GroupMatchRaidReadyEventArgs"), Profile = CurrentProfile });
                    }
                    if (eventLine.Contains("application|Matching with group id"))
                    {
                        MapLoading?.Invoke(this, new());
                    }
                    if (eventLine.Contains("application|LocationLoaded"))
                    {
                        // The map has been loaded and the game is searching for a match
                        raidInfo = new()
                        {
                            MapLoadTime = float.Parse(Regex.Match(eventLine, @"LocationLoaded:[0-9.,]+ real:(?<loadTime>[0-9.,]+)").Groups["loadTime"].Value.Replace(",", "."), CultureInfo.InvariantCulture),
                            ProfileType = CurrentProfile.Type,
                        };
                        MatchingStarted?.Invoke(this, new(raidInfo, CurrentProfile));
                    }
                    if (eventLine.Contains("application|MatchingCompleted"))
                    {
                        // Matching is complete and we are locked to a server with other players
                        // Just the queue time is available so far
                        // Occurs on initial raid load and when the user cancels matching
                        // Does not occur when the user re-connects to a raid in progress
                        var queueTimeMatch = Regex.Match(eventLine, @"MatchingCompleted:[0-9.,]+ real:(?<queueTime>[0-9.,]+)");
                        raidInfo.QueueTime = float.Parse(queueTimeMatch.Groups["queueTime"].Value.Replace(",", "."), CultureInfo.InvariantCulture);
                    }
                    if (eventLine.Contains("application|TRACE-NetworkGameCreate profileStatus"))
                    {
                        // Immediately after matching is complete
                        // Sufficient information is available to raise the MatchFound event
                        raidInfo.Map = Regex.Match(eventLine, "Location: (?<map>[^,]+)").Groups["map"].Value;
                        raidInfo.Online = eventLine.Contains("RaidMode: Online");
                        raidInfo.RaidId = Regex.Match(eventLine, @"shortId: (?<raidId>[A-Z0-9]{6})").Groups["raidId"].Value;
                        if (Raids.ContainsKey(raidInfo.RaidId))
                        {
                            raidInfo = Raids[raidInfo.RaidId];
                            raidInfo.Reconnected = true;
                        }
                        else
                        {
                            Raids.Add(raidInfo.RaidId, raidInfo);
                        }
                        if (!raidInfo.Reconnected && raidInfo.Online && raidInfo.QueueTime > 0)
                        {
                            // Raise the MatchFound event only if we queued; not if we are re-loading back into a raid
                            MatchFound?.Invoke(this, new(raidInfo, CurrentProfile));
                        }
                        MapLoaded?.Invoke(this, new(raidInfo, CurrentProfile));
                    }
                    if (eventLine.Contains("application|GameStarting"))
                    {
                        // GameStarting always happens for PMCs and sometimes happens for scavs.
                        // For PMCs, it corresponds with the start of the countdown timer.
                        if (!raidInfo.Reconnected)
                        {
                            raidInfo.StartingTime = eventDate;
                        }
                        RaidStarting?.Invoke(this, new(raidInfo, CurrentProfile));
                    }
                    if (eventLine.Contains("application|GameStarted"))
                    {
                        // Raid begins, either at the end of the countdown for PMC, or immediately as a scav
                        if (!raidInfo.Reconnected)
                        {
                            raidInfo.StartedTime = eventDate;
                        }
                        RaidStarted?.Invoke(this, new(raidInfo, CurrentProfile));
                        //raidInfo = new();
                    }
                    if (eventLine.Contains("application|Network game matching aborted") || eventLine.Contains("application|Network game matching cancelled"))
                    {
                        // User cancelled matching
                        MatchingAborted?.Invoke(this, new(raidInfo, CurrentProfile));
                        raidInfo = new()
                        {
                            ProfileType = CurrentProfile.Type,
                        };
                    }
                    if (eventLine.Contains("Got notification | UserMatchOver"))
                    {
                        RaidExited?.Invoke(this, new RaidExitedEventArgs { Map = jsonNode?["location"]?.ToString(), RaidId = jsonNode?["shortId"]?.ToString() });
                        raidInfo = new()
                        {
                            ProfileType = CurrentProfile.Type,
                        };
                    }
                    if (eventLine.Contains("application|Init: pstrGameVersion: "))
                    {
                        if (raidInfo.EndedTime != null)
                        {
                            ExitedPostRaidMenus?.Invoke(this, new(raidInfo, CurrentProfile));
                            raidInfo = new()
                            {
                                ProfileType = CurrentProfile.Type,
                            };
                        }
                    }
                    if (eventLine.Contains("Got notification | ChatMessageReceived"))
                    {
                        try
                        {
                            Console.WriteLine("=== CHAT MESSAGE PROCESSING START ===");
                            Console.WriteLine($"Processing chat message: {eventLine}");

                            var messageEvent = jsonNode?.AsObject().Deserialize<ChatMessageLogContent>();
                            if (messageEvent == null || messageEvent.Message == null)
                            {
                                Console.WriteLine("WARNING: Failed to deserialize ChatMessageLogContent or Message is null");
                                continue;
                            }

                            Console.WriteLine($"Message Type: {messageEvent.Message.Type}");

                            if (messageEvent.Message.Type == MessageType.PlayerMessage)
                            {
                                Console.WriteLine("Skipping player message");
                                continue;
                            }

                            var systemMessageEvent = jsonNode?.AsObject().Deserialize<SystemChatMessageLogContent>();
                            if (systemMessageEvent == null || systemMessageEvent.Message == null)
                            {
                                Console.WriteLine("WARNING: Failed to deserialize SystemChatMessageLogContent or Message is null");
                                continue;
                            }

                            Console.WriteLine($"System Message Type: {systemMessageEvent.Message.Type}, TemplateId: {systemMessageEvent.Message.TemplateId}");

                            if (messageEvent.Message.Type == MessageType.FleaMarket)
                            {
                                Console.WriteLine($"Processing flea market message with TemplateId: {systemMessageEvent.Message.TemplateId}");
                                Console.WriteLine($"Full JSON content: {jsonString}");

                                // Special check for milk
                                if (jsonString.Contains("milk") && jsonString.Contains("bought"))
                                {
                                    Console.WriteLine("======= MILK SALE DETECTED =======");
                                    Console.WriteLine($"Message type: {messageEvent.Message.Type}");
                                    Console.WriteLine($"Template ID: {systemMessageEvent.Message.TemplateId}");
                                }

                                // Check for flea market sale with original template ID
                                bool isFleaSold = systemMessageEvent.Message.TemplateId == "5bdabfb886f7743e152e867e 0";

                                // Alternative check - see if template contains "bought" or if the message text contains indicators of a sale
                                if (!isFleaSold && jsonString.Contains("were bought by"))
                                {
                                    Console.WriteLine("Detected flea market sale via message content!");
                                    isFleaSold = true;
                                }

                                if (isFleaSold)
                                {
                                    try
                                    {
                                        Console.WriteLine("------------------------");
                                        Console.WriteLine("PROCESSING FLEA MARKET SALE EVENT");
                                        Console.WriteLine("------------------------");

                                        // Try to manually extract information if needed
                                        try
                                        {
                                            var jsonObj = System.Text.Json.JsonDocument.Parse(jsonString).RootElement;
                                            Console.WriteLine("Direct JSON access - trying to extract info:");

                                            if (jsonObj.TryGetProperty("data", out var data))
                                            {
                                                Console.WriteLine("Found data property");

                                                if (data.TryGetProperty("Message", out var message))
                                                {
                                                    Console.WriteLine("Found message property");

                                                    if (message.TryGetProperty("text", out var text))
                                                    {
                                                        Console.WriteLine($"Message text: {text}");
                                                    }

                                                    if (message.TryGetProperty("templateId", out var templateId))
                                                    {
                                                        Console.WriteLine($"Template ID: {templateId}");
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception jsonEx)
                                        {
                                            Console.WriteLine($"Error exploring JSON: {jsonEx.Message}");
                                        }

                                        var fleaSoldContent = jsonNode?.AsObject().Deserialize<FleaSoldMessageLogContent>();
                                        Console.WriteLine("------------------------");
                                        Console.WriteLine("PROCESSING FLEA MARKET SALE EVENT");
                                        Console.WriteLine("------------------------");

                                        if (fleaSoldContent == null)
                                        {
                                            Console.WriteLine("Flea sale content is null after deserialization - trying fallback");

                                            // Fallback: Try to extract sale info directly from the message text
                                            string messageText = "";
                                            string buyer = "Unknown";
                                            string itemId = "Unknown";

                                            try
                                            {
                                                var jsonObj = System.Text.Json.JsonDocument.Parse(jsonString).RootElement;
                                                if (jsonObj.TryGetProperty("data", out var data) &&
                                                    data.TryGetProperty("Message", out var message) &&
                                                    message.TryGetProperty("text", out var text))
                                                {
                                                    messageText = text.GetString() ?? "";
                                                    Console.WriteLine($"Extracted message text: {messageText}");

                                                    // Try to extract buyer name and item from the message text
                                                    // Expected format: "Your [ItemName] (x[Count]) items were bought by [BuyerName]."
                                                    if (messageText.Contains("were bought by"))
                                                    {
                                                        // Extract buyer name (everything after "bought by")
                                                        buyer = messageText.Split("bought by ").LastOrDefault()?.Trim('.') ?? "Unknown";
                                                        Console.WriteLine($"Extracted buyer: {buyer}");

                                                        // Extract item name (between "Your" and "items were bought")
                                                        var itemMatch = System.Text.RegularExpressions.Regex.Match(messageText,
                                                            @"Your\s+(?<item>.+?)\s+(?:\(x\d+\))?\s+items were bought");

                                                        if (itemMatch.Success)
                                                        {
                                                            itemId = itemMatch.Groups["item"].Value.Trim();
                                                            Console.WriteLine($"Extracted item: {itemId}");
                                                        }

                                                        // Create a direct log message
                                                        Console.WriteLine($"=== SPECIAL NOTIFICATION: FLEA SALE: {buyer} bought {itemId} ===");

                                                        // Create a manual FleaSoldMessageLogContent with the extracted info
                                                        fleaSoldContent = new FleaSoldMessageLogContent();

                                                        // Use reflection to set the properties since they're likely read-only
                                                        try
                                                        {
                                                            var type = fleaSoldContent.GetType();

                                                            // Set buyer
                                                            var buyerProperty = type.GetProperty("Buyer");
                                                            if (buyerProperty != null && buyerProperty.CanWrite)
                                                            {
                                                                buyerProperty.SetValue(fleaSoldContent, buyer);
                                                            }

                                                            // Set item ID
                                                            var itemProperty = type.GetProperty("SoldItemId");
                                                            if (itemProperty != null && itemProperty.CanWrite)
                                                            {
                                                                itemProperty.SetValue(fleaSoldContent, itemId);
                                                            }

                                                            Console.WriteLine("Successfully created fallback FleaSoldMessageLogContent");
                                                        }
                                                        catch (Exception reflectionEx)
                                                        {
                                                            Console.WriteLine($"Error using reflection: {reflectionEx.Message}");
                                                            // If reflection fails, we'll still have a non-null fleaSoldContent, but its properties will be default values
                                                        }
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Fallback extraction failed: {ex.Message}");
                                            }
                                        }
                                        else if (fleaSoldContent.Message == null)
                                        {
                                            Console.WriteLine("Flea sale message is null after deserialization");
                                        }
                                        else
                                        {
                                            // Create strong reference to the event to avoid delegate being garbage collected
                                            var handler = FleaSold;

                                            Console.WriteLine($"Flea sale from buyer: {fleaSoldContent.Buyer}, Item: {fleaSoldContent.SoldItemId}");
                                            Console.WriteLine($"Number of FleaSold event handlers: {(handler?.GetInvocationList().Length ?? 0)}");

                                            if (handler != null)
                                            {
                                                var args = new LogContentEventArgs<FleaSoldMessageLogContent>()
                                                {
                                                    LogContent = fleaSoldContent,
                                                    Profile = CurrentProfile
                                                };

                                                // Force UI update on main thread
                                                Console.WriteLine("Invoking FleaSold event handlers...");
                                                handler.Invoke(this, args);
                                                Console.WriteLine("FleaSold event invoked successfully");
                                            }
                                            else
                                            {
                                                Console.WriteLine("ERROR: No handlers registered for FleaSold event!");
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error processing flea sale: {ex.Message}");
                                        Console.WriteLine($"Stack trace: {ex.StackTrace}");
                                        ExceptionThrown?.Invoke(this, new ExceptionEventArgs(ex, "Error processing flea market sale"));
                                    }
                                }
                            }

                            if (systemMessageEvent.Message.Type >= MessageType.TaskStarted && systemMessageEvent.Message.Type <= MessageType.TaskFinished)
                            {
                                try
                                {
                                    var args = jsonNode?.AsObject().Deserialize<TaskStatusMessageLogContent>();
                                    if (args != null && args.Message != null)
                                    {
                                        TaskModified?.Invoke(this, new LogContentEventArgs<TaskStatusMessageLogContent>() { LogContent = args, Profile = CurrentProfile });

                                        if (args.Status == TaskStatus.Started)
                                        {
                                            TaskStarted?.Invoke(this, new LogContentEventArgs<TaskStatusMessageLogContent>() { LogContent = args, Profile = CurrentProfile });
                                        }
                                        if (args.Status == TaskStatus.Failed)
                                        {
                                            TaskFailed?.Invoke(this, new LogContentEventArgs<TaskStatusMessageLogContent>() { LogContent = args, Profile = CurrentProfile });
                                        }
                                        if (args.Status == TaskStatus.Finished)
                                        {
                                            TaskFinished?.Invoke(this, new LogContentEventArgs<TaskStatusMessageLogContent>() { LogContent = args, Profile = CurrentProfile });
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error processing task event: {ex.Message}");
                                    ExceptionThrown?.Invoke(this, new ExceptionEventArgs(ex, "Error processing task event"));
                                }
                            }

                            if (systemMessageEvent.Message.TemplateId == "5bdabfe486f7743e1665df6e 0")
                            {
                                try
                                {
                                    var fleaExpiredContent = jsonNode?.AsObject().Deserialize<FleaExpiredMessageLogContent>();
                                    if (fleaExpiredContent != null && fleaExpiredContent.Message != null)
                                    {
                                        FleaOfferExpired?.Invoke(this, new LogContentEventArgs<FleaExpiredMessageLogContent>()
                                        {
                                            LogContent = fleaExpiredContent,
                                            Profile = CurrentProfile
                                        });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error processing flea expiration: {ex.Message}");
                                    ExceptionThrown?.Invoke(this, new ExceptionEventArgs(ex, "Error processing flea market expiration"));
                                }
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing chat message: {ex.Message}");
                            ExceptionThrown?.Invoke(this, new ExceptionEventArgs(ex, $"Error processing chat message: {eventLine}"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionThrown?.Invoke(this, new ExceptionEventArgs(ex, $"parsing {e.Type} log data {e.Data}"));
            }
        }

        private void ProcessTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            UpdateProcess();
        }

        public Dictionary<DateTime, string> GetLogFolders()
        {
            Dictionary<DateTime, string> folderDictionary = new();
            if (LogsPath == "")
            {
                return folderDictionary;
            }

            // Find all of the log folders in the Logs directory
            var logFolders = Directory.GetDirectories(LogsPath);
            // For each log folder, get the timestamp from the folder name
            foreach (string folderName in logFolders)
            {
                var dateTimeString = new Regex(@"log_(?<timestamp>\d+\.\d+\.\d+_\d+-\d+-\d+)").Match(folderName).Groups["timestamp"].Value;
                DateTime folderDate = DateTime.ParseExact(dateTimeString, "yyyy.MM.dd_H-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
                folderDictionary.Add(folderDate, folderName);
            }
            // Return the dictionary sorted by the timestamp
            return folderDictionary.OrderByDescending(key => key.Key).ToDictionary(x => x.Key, x => x.Value);
        }

        public class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public GameLogType LogType { get; set; }
            public string Content { get; set; }

            public LogEntry(DateTime timestamp, GameLogType logType, string content)
            {
                Timestamp = timestamp;
                LogType = logType;
                Content = content;
            }
        }

        // Process the log files in the specified folder
        public async Task ProcessLogs(LogDetails target, List<LogDetails> profiles)
        {
            for (var i = 0; i < profiles.Count; i++)
            {
                var logProfile = profiles[i];
                if (logProfile.Profile.Id != target.Profile.Id)
                {
                    continue;
                }
                var endDate = DateTime.Now.AddYears(1);
                if (profiles.Count > 1 && i + 1 < profiles.Count)
                {
                    endDate = profiles[i + 1].Date;
                }

                // Collect all log entries first
                List<LogEntry> allEntries = new();
                if (string.IsNullOrEmpty(logProfile.Folder)) continue;
                var logFiles = Directory.GetFiles(logProfile.Folder);

                foreach (string logFile in logFiles)
                {
                    GameLogType logType;
                    // Check which type of log file this is by the filename
                    if (logFile.Contains("application.log"))
                    {
                        logType = GameLogType.Application;
                    }
                    else if (logFile.Contains("notifications.log"))
                    {
                        logType = GameLogType.Notifications;
                    }
                    else if (logFile.Contains("traces.log"))
                    {
                        // logType = GameLogType.Traces;
                        // Traces are not currently used, so skip them
                        continue;
                    }
                    else
                    {
                        // We're not a known log type, so skip this file
                        continue;
                    }

                    // Read the file into memory using UTF-8 encoding
                    using var fileStream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var textReader = new StreamReader(fileStream, Encoding.UTF8);
                    var fileContents = await textReader.ReadToEndAsync();

                    var logPattern = @"(?<date>^\d{4}-\d{2}-\d{2}) (?<time>\d{2}:\d{2}:\d{2}\.\d{3}) (?<timeOffset>[+-]\d{2}:\d{2})\|(?<message>.+$)\s*(?<json>^{[\s\S]+?^})?";
                    var logMessages = Regex.Matches(fileContents, logPattern, RegexOptions.Multiline);

                    foreach (Match match in logMessages)
                    {
                        var dateTimeString = match.Groups["date"].Value + " " + match.Groups["time"].Value;
                        DateTime logMessageDate = DateTime.ParseExact(dateTimeString, "yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

                        if (logMessageDate < logProfile.Date || logMessageDate >= endDate)
                        {
                            continue;
                        }

                        allEntries.Add(new LogEntry(logMessageDate, logType, match.Value));
                    }
                }

                // Sort all entries by timestamp and process them in order
                foreach (var entry in allEntries.OrderBy(e => e.Timestamp))
                {
                    GameWatcher_NewLogData(this, new NewLogDataEventArgs { Type = entry.LogType, Data = entry.Content });
                }
            }
        }

        public List<LogDetails> GetLogDetails(string folderPath)
        {
            List<LogDetails> logDetails = new();
            if (!Directory.Exists(folderPath))
            {
                return logDetails;
            }
            var appLogPath = "";
            var files = Directory.GetFiles(folderPath) ?? Array.Empty<string>();
            foreach (var file in files)
            {
                if (file.EndsWith("application.log"))
                {
                    appLogPath = file;
                    break;
                }
            }
            if (appLogPath == "")
            {
                return logDetails;
            }
            using var fileStream = new FileStream(appLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var textReader = new StreamReader(fileStream, Encoding.UTF8);
            var applicationLog = textReader.ReadToEnd();
            var matches = Regex.Matches(applicationLog, @"(?<date>^\d{4}-\d{2}-\d{2}) (?<time>\d{2}:\d{2}:\d{2}\.\d{3}) (?<timeOffset>[+-]\d{2}:\d{2})\|(?<version>\d+\.\d+\.\d+\.\d+)\.\d+\|(?<logLevel>[^|]+)\|(?<logType>[^|]+)\|SelectProfile ProfileId:(?<profileId>[a-f0-9]+) AccountId:(?<accountId>\d+)", RegexOptions.Multiline);
            if (matches.Count == 0)
            {
                return logDetails;
            }
            var profileTypeMatches = Regex.Matches(applicationLog, @"(?<date>^\d{4}-\d{2}-\d{2}) (?<time>\d{2}:\d{2}:\d{2}\.\d{3}) (?<timeOffset>[+-]\d{2}:\d{2})\|(?<version>\d+\.\d+\.\d+\.\d+)\.\d+\|(?<logLevel>[^|]+)\|(?<logType>[^|]+)\|Session mode: (?<profileType>\w+)", RegexOptions.Multiline);
            for (var i = 0; i < matches.Count; i++)
            {
                Match match = matches[i];
                var dateTimeString = match.Groups["date"].Value + " " + match.Groups["time"].Value;
                DateTime profileDate = DateTime.ParseExact(dateTimeString, "yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
                ProfileType profileType = ProfileType.Regular;
                if (matches.Count == profileTypeMatches.Count)
                {
                    profileType = Enum.Parse<ProfileType>(profileTypeMatches[i].Groups["profileType"].Value, true);
                }
                logDetails.Add(new LogDetails()
                {
                    Profile = new() { Id = match.Groups["profileId"].Value, Type = profileType },
                    AccountId = Int32.Parse(match.Groups["accountId"].Value),
                    Date = profileDate,
                    Version = new Version(match.Groups["version"].Value),
                    Folder = folderPath,
                });
            }
            return logDetails;
        }

        public List<LogDetails> GetLogBreakpoints(string profileId)
        {
            List<LogDetails> breakpoints = new();
            if (profileId == "")
            {
                return breakpoints;
            }
            foreach (var kvp in GetLogFolders().OrderBy(key => key.Key).ToDictionary(x => x.Key, x => x.Value))
            {
                List<LogDetails> folderBreakpoints = GetLogDetails(kvp.Value);
                foreach (var breakpoint in folderBreakpoints)
                {
                    if (breakpoint.Profile.Id != profileId)
                    {
                        continue;
                    }
                    var matchingBreakpoint = breakpoints.Where((bp) => bp.Version == breakpoint.Version && bp.Profile.Id == breakpoint.Profile.Id).FirstOrDefault();
                    if (matchingBreakpoint == null)
                    {
                        breakpoints.Add(breakpoint);
                    }
                }
            }
            return breakpoints;
        }

        public async Task ProcessLogsFromBreakpoint(LogDetails breakpoint)
        {
            List<List<LogDetails>> logDetails = new();
            var logFolders = Directory.GetDirectories(LogsPath);
            // For each log folder, get the details
            foreach (string folderName in logFolders)
            {
                var details = GetLogDetails(folderName);
                if (details.Count == 0)
                {
                    continue;
                }
                if (!details.Any(d => d.Profile.Id == breakpoint.Profile.Id))
                {
                    continue;
                }
                if (!details.Any(d => d.Date >= breakpoint.Date))
                {
                    continue;
                }
                logDetails.Add(details);
            }
            logDetails = logDetails.OrderBy(det => det[0].Date).ToList();
            foreach (var details in logDetails)
            {
                await ProcessLogs(breakpoint, details);
            }
        }

        /// <summary>
        /// Updates the process tracking state
        /// Checks if the game is still running and updates internal state accordingly
        /// </summary>
        private void UpdateProcess()
        {
            try
            {
                if (process != null)
                {
                    if (!process.HasExited)
                    {
                        return;
                    }
                    process = null;
                }
                raidInfo = new();
                var processes = Process.GetProcessesByName("EscapeFromTarkov");
                if (processes.Length == 0)
                {
                    process = null;
                    return;
                }
                GameStarted?.Invoke(this, new EventArgs());
                process = processes.First();
            }
            catch (Exception ex)
            {
                ExceptionThrown?.Invoke(this, new(ex, "watching for EFT process"));
            }
        }

        /// <summary>
        /// Gets the path to the most recent log folder
        /// </summary>
        private string GetLatestLogFolder()
        {
            var logFolders = Directory.GetDirectories(LogsPath);
            var latestDate = new DateTime(0);
            var latestLogFolder = logFolders.LastOrDefault() ?? "";
            foreach (var logFolder in logFolders)
            {
                var dateTimeMatch = Regex.Match(logFolder, @"log_(?<timestamp>\d+\.\d+\.\d+_\d+-\d+-\d+)").Groups["timestamp"];
                if (!dateTimeMatch.Success)
                {
                    continue;
                }
                var dateTimeString = dateTimeMatch.Value;

                var logDate = DateTime.ParseExact(dateTimeString, "yyyy.MM.dd_H-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
                if (logDate > latestDate)
                {
                    latestDate = logDate;
                    latestLogFolder = logFolder;
                }
            }
            return latestLogFolder ?? "";
        }

        /// <summary>
        /// Sets up monitoring for a specific logs folder
        /// Initializes monitors for application and notification logs
        /// </summary>
        private async Task WatchLogsFolder(string folderPath)
        {
            var files = System.IO.Directory.GetFiles(folderPath);
            var monitorsStarted = 0;
            var monitorsCompletedInitialRead = 0;
            List<string> monitoringLogs = new() { "notifications.log", "application.log" };
            foreach (var file in files)
            {
                foreach (var logType in monitoringLogs)
                {
                    monitorsStarted++;
                    if (!file.Contains(logType))
                    {
                        monitorsCompletedInitialRead++;
                        continue;
                    }
                    var monitor = await StartNewMonitor(file);
                    if (monitor == null || InitialLogsRead)
                    {
                        monitorsCompletedInitialRead++;
                        break;
                    }
                    monitor.InitialReadComplete += (object? sender, EventArgs e) =>
                    {
                        monitorsCompletedInitialRead++;
                        if (monitorsCompletedInitialRead == monitorsStarted)
                        {
                            InitialLogsRead = true;
                            InitialReadComplete?.Invoke(this, new(CurrentProfile));
                        }
                    };
                    break;
                }
            }
        }

        /// <summary>
        /// Starts a new log monitor for the specified file
        /// </summary>
        /// <param name="path">Path to the log file to monitor</param>
        /// <returns>The created LogMonitor instance, or null if the file type is not supported</returns>
        private async Task<LogMonitor?> StartNewMonitor(string path)
        {
            GameLogType? newType = null;
            if (path.Contains("application.log"))
            {
                newType = GameLogType.Application;
                CurrentProfile = new();
            }
            if (path.Contains("notifications.log"))
            {
                newType = GameLogType.Notifications;
            }
            if (path.Contains("traces.log"))
            {
                newType = GameLogType.Traces;
            }
            if (newType == null)
            {
                return null;
            }

            // Stop existing monitor if it exists
            if (Monitors.ContainsKey((GameLogType)newType))
            {
                Monitors[(GameLogType)newType].Stop();
            }

            // Create and start new monitor
            var newMon = new LogMonitor(path, (GameLogType)newType);
            newMon.NewLogData += GameWatcher_NewLogData;
            newMon.Exception += (sender, e) =>
            {
                ExceptionThrown?.Invoke(sender, e);
            };
            await newMon.Start();
            Monitors[(GameLogType)newType] = newMon;
            return newMon;
        }

        // Public methods to subscribe and unsubscribe from events
        public void SubscribeToFleaSold(EventHandler<LogContentEventArgs<FleaSoldMessageLogContent>> handler)
        {
            if (handler != null)
            {
                FleaSold += handler;
            }
        }

        public void UnsubscribeFromFleaSold(EventHandler<LogContentEventArgs<FleaSoldMessageLogContent>> handler)
        {
            if (handler != null)
            {
                FleaSold -= handler;
            }
        }

        public int GetFleaSoldHandlerCount()
        {
            return FleaSold?.GetInvocationList().Length ?? 0;
        }

        // DirectFleaSold event subscription methods
        public void SubscribeToDirectFleaSold(EventHandler<ManualFleaSoldEventArgs> handler)
        {
            if (handler != null)
            {
                DirectFleaSold += handler;
            }
        }

        public void UnsubscribeFromDirectFleaSold(EventHandler<ManualFleaSoldEventArgs> handler)
        {
            if (handler != null)
            {
                DirectFleaSold -= handler;
            }
        }

        public int GetDirectFleaSoldHandlerCount()
        {
            return DirectFleaSold?.GetInvocationList().Length ?? 0;
        }
    }

    /// <summary>
    /// Represents the different types of game logs that can be monitored
    /// </summary>
    public enum GameLogType
    {
        Application,
        Notifications,
        Traces
    }

    /// <summary>
    /// Represents the different types of messages that can be received in the game
    /// </summary>
    public enum MessageType
    {
        PlayerMessage = 1,
        Insurance = 2,
        FleaMarket = 4,
        InsuranceReturn = 8,
        TaskStarted = 10,
        TaskFailed = 11,
        TaskFinished = 12,
        TwitchDrop = 13,
    }

    /// <summary>
    /// Represents the different states a task can be in
    /// </summary>
    public enum TaskStatus
    {
        None = 0,
        Started = 10,
        Failed = 11,
        Finished = 12
    }

    /// <summary>
    /// Represents the different types of raids that can be played
    /// </summary>
    public enum RaidType
    {
        Unknown,
        PMC,
        Scav,
        PVE,
    }

    /// <summary>
    /// Represents the different types of group invites
    /// </summary>
    public enum GroupInviteType
    {
        Accepted,
        Sent
    }

    /// <summary>
    /// Contains information about a raid session
    /// </summary>
    public class RaidInfo
    {
        public string? Map { get; set; }
        public string RaidId { get; set; } = "";
        public bool Online { get; set; }
        public float MapLoadTime { get; set; }
        public float QueueTime { get; set; }
        public bool Reconnected { get; set; }

        /// <summary>
        /// Gets the type of raid based on profile type and timing information
        /// </summary>
        public RaidType RaidType
        {
            get
            {
                if (this.ProfileType == ProfileType.PVE)
                {
                    return RaidType.PVE;
                }
                // if raid hasn't started, we don't have enough info to know what type it is
                if (StartedTime == null)
                {
                    return RaidType.Unknown;
                }

                // if GameStarting appeared, could be PMC or scav
                // check time elapsed between the two to account for the PMC countdown
                if (StartingTime != null && (StartedTime - StartingTime)?.TotalSeconds > 3)
                {
                    return RaidType.PMC;
                }

                // not PMC, so must be scav
                return RaidType.Scav;
            }
        }

        public DateTime? StartingTime { get; set; }
        public DateTime? StartedTime { get; set; }
        public DateTime? EndedTime { get; set; }
        public List<string> Screenshots { get; set; } = new();
        public ProfileType ProfileType { get; set; } = ProfileType.Regular;

        public RaidInfo()
        {
            Map = "";
            Online = false;
            RaidId = "";
            MapLoadTime = 0;
            QueueTime = 0;
            Reconnected = false;
        }
    }

    /// <summary>
    /// Represents a 3D position in the game world
    /// </summary>
    public class Position
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public Position(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Position(string x, string y, string z)
        {
            X = float.Parse(x, CultureInfo.InvariantCulture);
            Y = float.Parse(y, CultureInfo.InvariantCulture);
            Z = float.Parse(z, CultureInfo.InvariantCulture);
        }
    }

    // Event argument classes for various events
    public class RaidExitedEventArgs : EventArgs
    {
        public string? Map { get; set; }
        public string? RaidId { get; set; }
    }

    public class RaidInfoEventArgs : EventArgs
    {
        public RaidInfo RaidInfo { get; set; }
        public Profile Profile { get; set; }
        public RaidInfoEventArgs(RaidInfo raidInfo, Profile profile)
        {
            RaidInfo = raidInfo;
            Profile = profile;
        }
    }

    public class ExceptionEventArgs : EventArgs
    {
        public Exception Exception { get; set; }
        public string Context { get; set; }
        public ExceptionEventArgs(Exception ex, string context)
        {
            this.Exception = ex;
            Context = context;
        }
    }

    public class PlayerPositionEventArgs : RaidInfoEventArgs
    {
        public Position Position { get; set; }
        public string Filename { get; set; }
        public PlayerPositionEventArgs(RaidInfo raidInfo, Profile profile, Position position, string filename) : base(raidInfo, profile)
        {
            this.Position = position;
            this.Filename = filename;
        }
    }

    /// <summary>
    /// Contains details about a specific log entry including profile, account, and version information
    /// </summary>
    public class LogDetails
    {
        public Profile Profile { get; set; } = new Profile();
        public int AccountId { get; set; }
        public DateTime Date { get; set; }
        public Version? Version { get; set; }
        public string? Folder { get; set; }
    }

    /// <summary>
    /// Represents the different types of profiles in the game
    /// </summary>
    public enum ProfileType
    {
        PVE,
        Regular,
    }

    /// <summary>
    /// Contains information about a player profile
    /// </summary>
    public class Profile
    {
        public string Id { get; set; } = "";
        public ProfileType Type { get; set; } = ProfileType.Regular;
    }

    public class ProfileEventArgs : EventArgs
    {
        public Profile Profile { get; set; }
        public ProfileEventArgs(Profile profile)
        {
            Profile = profile;
        }
    }

    public class LogContentEventArgs<T> : EventArgs where T : JsonLogContent
    {
        public T? LogContent { get; set; }
        public Profile Profile { get; set; } = new Profile();
    }

    public class ManualFleaSoldEventArgs : EventArgs
    {
        public string? BuyerName { get; set; }
        public string? ItemName { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
