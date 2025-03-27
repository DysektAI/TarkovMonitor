using MudBlazor.Services;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using TarkovMonitor.Groups;
using System.Globalization;
using System.ComponentModel;
using MudBlazor;
using System.Timers;
using TarkovMonitor.Properties;
using System.Threading.Tasks;

namespace TarkovMonitor
{
    /// <summary>
    /// MainBlazorUI is the primary form class that integrates Blazor WebView with Windows Forms
    /// to create a hybrid desktop application for monitoring Escape from Tarkov game events.
    /// This class handles game events, user interface, and integration with external services.
    /// </summary>
    public partial class MainBlazorUI : Form
    {
        // Core components for monitoring and managing game state
        private MessageLog? messageLog;
        private LogRepository? logRepository;
        private GroupManager? groupManager;
        private GameWatcher? eft;
        private TimersManager? timersManager;
        private System.Timers.Timer? runthroughTimer;
        private System.Timers.Timer? scavCooldownTimer;

        // Define constants for currency IDs
        private const string RoubleId = "5449016a4bdc2d6f028b456f";
        private const string DollarId = "5696686a4bdc2da3298b456a";
        private const string EuroId = "569668774bdc2da2298b4568";


        /// <summary>
        /// Initializes the main form and sets up all necessary components and event handlers
        /// for monitoring Escape from Tarkov game events.
        /// </summary>
        public MainBlazorUI()
        {
            InitializeComponent();

            // Handle application settings upgrade if needed
            if (Properties.Settings.Default.upgradeRequired)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.upgradeRequired = false;
                Properties.Settings.Default.Save();
            }

            // Set window behavior based on settings
            this.TopMost = Properties.Settings.Default.stayOnTop;

            // Initialize objects synchronously
            messageLog = new MessageLog();
            logRepository = new LogRepository();
            eft = new GameWatcher();
            groupManager = new GroupManager();
            timersManager = new TimersManager(eft); // Initialize TimersManager early

            InitializeTimers();
            SetupGameWatcherEvents();

            // Set up Blazor services
            SetupBlazorServices();

            // Start initialization asynchronously
            Task.Run(async () => await InitializeAsync()).ConfigureAwait(false);
        }

        private async Task InitializeAsync()
        {
            try
            {
                if (eft == null) return;
                await Task.Run(async () =>
                {
                    await eft.Start();

                    try
                    {
                        messageLog?.AddMessage("Loading Tarkov.dev API data...", "info");
                        await TarkovDev.UpdateApiData();
                        messageLog?.AddMessage($"Loaded {TarkovDev.Maps.Count} maps from Tarkov.dev API", "info");

                        // Log all maps to help diagnose any map detection issues
                        foreach (var map in TarkovDev.Maps)
                        {
                            messageLog?.AddMessage($"Map loaded: {map.Name} (ID: {map.NameId})", "debug");
                        }
                    }
                    catch (Exception apiEx)
                    {
                        messageLog?.AddMessage($"Error loading Tarkov.dev API data: {apiEx.Message}", "exception");
                    }

                    TarkovDev.StartAutoUpdates();
                    UpdateCheck.CheckForNewVersion();
                });
                // TimersManager is already initialized in the constructor
                // No need to reinitialize here
            }
            catch (Exception ex)
            {
                // Log full exception details
                messageLog?.AddMessage($"Error during initialization: {ex}", "exception");
            }
        }

        private async void ScavCooldownTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (Properties.Settings.Default.scavCooldownAlert)
            {
                try
                {
                    await Sound.Play("scav_available");
                    messageLog?.AddMessage("Player scav available", "info");
                }
                catch (Exception ex)
                {
                    // Log full exception details
                    messageLog?.AddMessage($"Error playing scav cooldown sound: {ex}", "exception");
                }
            }
        }

        private async void RunthroughTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (Properties.Settings.Default.runthroughAlert)
            {
                try
                {
                    await Sound.Play("runthrough_over");
                    messageLog?.AddMessage("Runthrough period over", "info");
                }
                catch (Exception ex)
                {
                    // Log full exception details
                    messageLog?.AddMessage($"Error playing runthrough sound: {ex}", "exception");
                }
            }
        }

        private async void Eft_RaidEnded(object? sender, RaidInfoEventArgs e)
        {
            if (groupManager != null) groupManager.Stale = true;

            var mapName = e.RaidInfo?.Map; // Use null-conditional operator
            if (mapName != null)
            {
                var map = TarkovDev.Maps.Find(m => m.NameId == mapName);
                if (map != null) mapName = map.Name;
            }
            else
            {
                mapName = "Unknown Map"; // Handle null map name
            }

            MonitorMessage monMessage = new($"Ended {mapName} raid");

            if (e.RaidInfo?.Screenshots != null && e.RaidInfo.Screenshots.Count > 0) // Check RaidInfo and Screenshots for null
            {
                await Task.Run(() => Handle_Screenshots(e, monMessage));
            }

            messageLog?.AddMessage(monMessage);
            runthroughTimer?.Stop();

            if (Properties.Settings.Default.scavCooldownAlert && e.RaidInfo != null && (e.RaidInfo.RaidType == RaidType.Scav || e.RaidInfo.RaidType == RaidType.PVE)) // Check RaidInfo for null
            {
                // Calculate the cooldown duration once to keep it consistent
                int cooldownSeconds = TarkovDev.ResetScavCoolDown();

                // Set up the notification timer
                scavCooldownTimer?.Stop();
                if (scavCooldownTimer != null)
                {
                    scavCooldownTimer.Interval = TimeSpan.FromSeconds(cooldownSeconds).TotalMilliseconds;
                    scavCooldownTimer.Start();

                    // Debug logging to help diagnose timer issues
                    messageLog?.AddMessage($"Scav cooldown started: {TimeSpan.FromSeconds(cooldownSeconds).ToString(@"hh\:mm\:ss")}", "debug");
                }
            }
        }

        private async void Eft_MapLoaded(object? sender, RaidInfoEventArgs e)
        {
            try
            {
                if (Properties.Settings.Default.autoNavigateMap && e.RaidInfo?.Map != null) // Check RaidInfo and Map
                {
                    var map = TarkovDev.Maps.Find(m => m.NameId == e.RaidInfo.Map);
                    if (map != null)
                    {
                        await SocketClient.NavigateToMap(map);
                    }
                }

                if (Properties.Settings.Default.raidStartAlert && e.RaidInfo?.StartingTime == null) // Check RaidInfo
                {
                    await Sound.Play("raid_starting");
                }

                var currentProfile = eft?.CurrentProfile; // Capture profile safely
                if (Properties.Settings.Default.submitQueueTime && e.RaidInfo != null && e.RaidInfo.QueueTime > 0 &&
                    e.RaidInfo.RaidType != RaidType.Unknown && e.RaidInfo.Map != null &&
                    currentProfile != null) // Use captured profile
                {
                    await TarkovDev.PostQueueTime(
                        e.RaidInfo.Map,
                        (int)Math.Round(e.RaidInfo.QueueTime),
                        e.RaidInfo.RaidType.ToString().ToLower(),
                        currentProfile.Type); // Use captured profile type
                }
            }
            catch (Exception ex)
            {
                // Log full exception details
                messageLog?.AddMessage($"Error in map loaded handler: {ex}", "exception");
            }
        }

        /// <summary>
        /// Handles profile changes in the game, updating the TarkovTracker profile accordingly.
        /// </summary>
        private async void Eft_ProfileChanged(object? sender, ProfileEventArgs e)
        {
            if (e.Profile == null) return; // Add null check for safety

            if (e.Profile.Id == TarkovTracker.CurrentProfileId)
            {
                return;
            }
            messageLog?.AddMessage($"Using {e.Profile.Type} profile");
            await TarkovTracker.SetProfile(e.Profile.Id);
        }

        /// <summary>
        /// Handles the event when player exits post-raid menus, triggering air filter alerts if enabled.
        /// </summary>
        private async void Eft_ExitedPostRaidMenus(object? sender, RaidInfoEventArgs e)
        {
            if (Properties.Settings.Default.airFilterAlert && TarkovTracker.HasAirFilter())
            {
                try
                {
                    await Sound.Play("air_filter_off");
                }
                catch (Exception ex)
                {
                    messageLog?.AddMessage($"Error playing air filter sound: {ex}", "exception");
                }
            }
        }

        /// <summary>
        /// Handles screenshot deletion after raids, either automatically or through user interaction.
        /// </summary>
        private void Delete_Screenshots(RaidInfoEventArgs e, MonitorMessage? monMessage = null, MonitorMessageButton? screenshotButton = null)
        {
            if (e.RaidInfo?.Screenshots == null) return; // Add null check

            try
            {
                string screenshotsBasePath = eft?.ScreenshotsPath ?? ""; // Safely get path
                foreach (var filename in e.RaidInfo.Screenshots)
                {
                    if (filename != null) // Check if filename itself is null
                    {
                        File.Delete(Path.Combine(screenshotsBasePath, filename));
                    }
                }
                messageLog?.AddMessage($"Deleted {e.RaidInfo.Screenshots.Count} screenshots");
            }
            catch (Exception ex)
            {
                // Log full exception details
                messageLog?.AddMessage($"Error deleting screenshot: {ex}", "exception");
            }

            if (monMessage?.Buttons != null && screenshotButton != null) // Check Buttons collection
            {
                monMessage.Buttons.Remove(screenshotButton);
            }
        }

        /// <summary>
        /// Manages screenshot handling after raids based on user settings.
        /// </summary>
        private void Handle_Screenshots(RaidInfoEventArgs e, MonitorMessage monMessage)
        {
            if (e.RaidInfo?.Screenshots == null || e.RaidInfo.Screenshots.Count == 0) return; // Add null/empty check

            var automaticallyDelete = Properties.Settings.Default.automaticallyDeleteScreenshotsAfterRaid;
            if (automaticallyDelete)
            {
                Delete_Screenshots(e);
                return;
            }

            MonitorMessageButton screenshotButton = new($"Delete {e.RaidInfo.Screenshots.Count} Screenshots", Icons.Material.Filled.Delete);
            screenshotButton.OnClick = () =>
            {
                Delete_Screenshots(e, monMessage, screenshotButton);
            };
            screenshotButton.Timeout = TimeSpan.FromMinutes(120).TotalMilliseconds;
            monMessage.Buttons.Add(screenshotButton); // Assumes Buttons is initialized
        }

        /// <summary>
        /// Handles group raid settings changes by clearing the current group state.
        /// </summary>
        private void Eft_GroupRaidSettings(object? sender, LogContentEventArgs<GroupRaidSettingsLogContent> e)
        {
            // e.LogContent is not used here, so no null check needed for it.
            groupManager?.ClearGroup();
        }

        /// <summary>
        /// Handles socket client exceptions by logging them to the message log.
        /// </summary>
        private void SocketClient_ExceptionThrown(object? sender, ExceptionEventArgs e)
        {
            // Log full exception details
            messageLog?.AddMessage($"Error {e.Context}: {e.Exception}", "exception");
        }

        /// <summary>
        /// Handles form shown event, implementing minimize at startup functionality if enabled.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            try
            {
                if (Properties.Settings.Default.minimizeAtStartup)
                {
                    WindowState = FormWindowState.Minimized;
                }
            }
            catch (Exception ex)
            {
                // Log full exception details
                messageLog?.AddMessage($"Error minimizing at startup: {ex}", "exception");
            }
        }

        private async void Eft_MapLoading(object? sender, EventArgs e)
        {
            // Use null-conditional operators for safer navigation
            if (TarkovTracker.Progress?.Data?.TasksProgress == null)
            {
                return;
            }
            try
            {
                var failedTasks = new List<TarkovDev.Task>();
                foreach (var taskStatus in TarkovTracker.Progress.Data.TasksProgress)
                {
                    if (taskStatus == null || !taskStatus.Failed) // Add null check for taskStatus
                    {
                        continue;
                    }
                    var task = TarkovDev.Tasks.Find(match: t => t.Id == taskStatus.Id);
                    if (task?.Restartable == true) // Use null-conditional operator
                    {
                        failedTasks.Add(task);
                    }
                }
                if (Properties.Settings.Default.airFilterAlert && TarkovTracker.HasAirFilter())
                {
                    await Sound.Play("air_filter_on");
                }
                if (Properties.Settings.Default.questItemsAlert)
                {
                    await Sound.Play("quest_items");
                }
                if (failedTasks.Count == 0)
                {
                    return;
                }
                foreach (var task in failedTasks)
                {
                    // Ensure task and Name are not null before logging
                    if (task?.Name != null)
                    {
                        messageLog?.AddMessage($"Failed task {task.Name} should be restarted", "quest", task.WikiLink);
                    }
                }
                if (Properties.Settings.Default.restartTaskAlert)
                {
                    await Sound.Play("restart_failed_tasks");
                }
            }
            catch (Exception ex)
            {
                // Log full exception details
                messageLog?.AddMessage($"Error on matching started: {ex}", "exception");
            }
        }

        private void Eft_GroupUserLeave(object? sender, LogContentEventArgs<GroupMatchUserLeaveLogContent> e)
        {
            // Use null-conditional operator ?. and null-coalescing operator ??
            var nickname = e.LogContent?.Nickname;
            if (nickname != null && nickname != "You")
            {
                groupManager?.RemoveGroupMember(nickname);
            }
            messageLog?.AddMessage($"{nickname ?? "Someone"} left the group.", "group");
        }

        private void Eft_GroupInviteAccept(object? sender, LogContentEventArgs<GroupLogContent> e)
        {
            // Use null-conditional operators for safer navigation
            var info = e.LogContent?.Info;
            if (info != null)
            {
                messageLog?.AddMessage($"{info.Nickname} ({info.Side?.ToUpper() ?? "N/A"} {info.Level}) accepted group invite.", "group");
            }
        }

        private void Eft_GroupDisbanded(object? sender, EventArgs e)
        {
            // e.LogContent is not used here.
            groupManager?.ClearGroup();
        }

        private void TarkovTracker_ProgressRetrieved(object? sender, EventArgs e)
        {
            // Use null-conditional operators for safer navigation
            var progressData = TarkovTracker.Progress?.Data;
            if (progressData != null)
            {
                messageLog?.AddMessage($"Retrieved {progressData.DisplayName} level {progressData.PlayerLevel} {progressData.PmcFaction} progress from Tarkov Tracker", "update", "https://tarkovtracker.io");
            }
            else
            {
                messageLog?.AddMessage($"Retrieved progress from Tarkov Tracker (details unavailable)", "update", "https://tarkovtracker.io");
            }
        }

        private void Eft_GroupStaleEvent(object? sender, EventArgs e)
        {
            if (groupManager != null) groupManager.Stale = true;
        }

        private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            // Check if WebView and CoreWebView2 are available before accessing properties
            if (e.IsSuccess && blazorWebView1.WebView?.CoreWebView2 != null)
            {
                if (Debugger.IsAttached) blazorWebView1.WebView.CoreWebView2.OpenDevToolsWindow();
            }
            else
            {
                messageLog?.AddMessage($"WebView2 initialization failed: {e.InitializationException}", "exception");
            }
        }

        /// <summary>
        /// Handles match found events, playing alerts and logging match information.
        /// </summary>
        private async void Eft_MatchFound(object? sender, RaidInfoEventArgs e)
        {
            if (Properties.Settings.Default.matchFoundAlert)
            {
                try
                {
                    await Sound.Play("match_found");
                }
                catch (Exception ex)
                {
                    messageLog?.AddMessage($"Error playing match found sound: {ex}", "exception");
                }
            }

            var mapName = e.RaidInfo?.Map; // Use null-conditional operator
            if (mapName != null)
            {
                var map = TarkovDev.Maps.Find(m => m.NameId == mapName);
                if (map != null) mapName = map.Name;
            }
            else
            {
                mapName = "Unknown Map"; // Handle null map name
            }
            // Use null-conditional operator for RaidInfo
            messageLog?.AddMessage($"Matching complete on {mapName} after {e.RaidInfo?.QueueTime ?? 0} seconds");
        }

        /// <summary>
        /// Handles new log data events by adding them to the log repository.
        /// </summary>
        private void Eft_NewLogData(object? sender, NewLogDataEventArgs e)
        {
            try
            {
                // Check if Data is null before adding
                if (e.Data != null)
                {
                    logRepository?.AddLog(e.Data, e.Type.ToString());
                }
            }
            catch (Exception ex)
            {
                // Log full exception details
                messageLog?.AddMessage($"{ex.GetType().Name} adding raw log to repository: {ex}", "exception");
            }
        }

        /// <summary>
        /// Handles group member ready events by updating group state and logging the event.
        /// </summary>
        private void Eft_GroupMemberReady(object? sender, LogContentEventArgs<GroupMatchRaidReadyLogContent> e)
        {
            // Use null-conditional operators for safer navigation
            var extendedProfile = e.LogContent?.ExtendedProfile;
            var info = extendedProfile?.Info;
            var visualInfo = extendedProfile?.PlayerVisualRepresentation?.Info;

            if (info != null && visualInfo != null)
            {
                // Pass e.LogContent only if it's not null
                if (e.LogContent != null)
                {
                    groupManager?.UpdateGroupMember(e.LogContent);
                }
                messageLog?.AddMessage($"{info.Nickname} ({visualInfo.Side?.ToUpper() ?? "N/A"} {visualInfo.Level}) ready.", "group");
            }
        }

        /// <summary>
        /// Handles task completion events by updating TarkovTracker and logging the event.
        /// </summary>
        private async void Eft_TaskFinished(object? sender, LogContentEventArgs<TaskStatusMessageLogContent> e)
        {
            // Use null-conditional operator
            var taskId = e.LogContent?.TaskId;
            if (taskId != null)
            {
                var task = TarkovDev.Tasks.Find(t => t.Id == taskId);
                if (task != null)
                {
                    // Use null-conditional operator for messageLog
                    messageLog?.AddMessage($"Completed task {task.Name}", "quest", $"https://tarkov.dev/task/{task.NormalizedName}");

                    if (TarkovTracker.ValidToken)
                    {
                        try
                        {
                            await TarkovTracker.SetTaskComplete(task.Id);
                        }
                        catch (Exception ex)
                        {
                            // Log full exception details
                            messageLog?.AddMessage($"Error updating Tarkov Tracker task progression: {ex}", "exception");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles task failure events by updating TarkovTracker and logging the event.
        /// </summary>
        private async void Eft_TaskFailed(object? sender, LogContentEventArgs<TaskStatusMessageLogContent> e)
        {
            // Use null-conditional operator
            var taskId = e.LogContent?.TaskId;
            if (taskId != null)
            {
                var task = TarkovDev.Tasks.Find(t => t.Id == taskId);
                if (task != null)
                {
                    messageLog?.AddMessage($"Failed task {task.Name}", "quest", $"https://tarkov.dev/task/{task.NormalizedName}");

                    if (TarkovTracker.ValidToken)
                    {
                        try
                        {
                            await TarkovTracker.SetTaskFailed(task.Id);
                        }
                        catch (Exception ex)
                        {
                            // Log full exception details
                            messageLog?.AddMessage($"Error updating Tarkov Tracker task progression: {ex}", "exception");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles task start events by updating TarkovTracker and logging the event.
        /// </summary>
        private async void Eft_TaskStarted(object? sender, LogContentEventArgs<TaskStatusMessageLogContent> e)
        {
            // Use null-conditional operator
            var taskId = e.LogContent?.TaskId;
            if (taskId != null)
            {
                var task = TarkovDev.Tasks.Find(t => t.Id == taskId);
                if (task != null)
                {
                    messageLog?.AddMessage($"Started task {task.Name}", "quest", $"https://tarkov.dev/task/{task.NormalizedName}");

                    if (TarkovTracker.ValidToken)
                    {
                        try
                        {
                            await TarkovTracker.SetTaskStarted(task.Id);
                        }
                        catch (Exception ex)
                        {
                            // Log full exception details
                            messageLog?.AddMessage($"Error updating Tarkov Tracker task progression: {ex}", "exception");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles flea market sale events by updating stats and logging the transaction.
        /// </summary>
        private void Eft_FleaSold(object? sender, LogContentEventArgs<FleaSoldMessageLogContent> e)
        {
            // Add direct console message for debugging
            Console.WriteLine("MainBlazorUI.Eft_FleaSold called");

            try
            {
                // Existing null checks are good
                if (e?.LogContent != null && e.Profile != null)
                {
                    Console.WriteLine($"MainBlazorUI: Processing sale from {e.LogContent.Buyer} for item {e.LogContent.SoldItemId}");

                    // Add to stats
                    Stats.AddFleaSale(e.LogContent, e.Profile);

                    if (TarkovDev.Items != null && e.LogContent.ReceivedItems != null) // Add check for ReceivedItems
                    {
                        List<string> received = new();
                        foreach (var kvp in e.LogContent.ReceivedItems) // Iterate safely
                        {
                            var receivedId = kvp.Key;
                            var amount = kvp.Value;
                            string formattedAmount;
                            string itemName = "";
                            switch (receivedId)
                            {
                                case RoubleId:
                                    formattedAmount = amount.ToString("C0", CultureInfo.CreateSpecificCulture("ru-RU"));
                                    received.Add(formattedAmount);
                                    continue; // Skip further item lookup
                                case DollarId:
                                    formattedAmount = amount.ToString("C0", CultureInfo.CreateSpecificCulture("en-US"));
                                    received.Add(formattedAmount);
                                    continue; // Skip further item lookup
                                case EuroId:
                                    formattedAmount = amount.ToString("C0", CultureInfo.CreateSpecificCulture("de-DE"));
                                    received.Add(formattedAmount);
                                    continue; // Skip further item lookup
                                default:
                                    var receivedItem = TarkovDev.Items.Find(item => item.Id == receivedId);
                                    itemName = receivedItem?.Name ?? "Unknown Item";
                                    formattedAmount = String.Format("{0:n0}", amount);
                                    break;
                            }
                            received.Add($"{formattedAmount} {itemName}");
                        }

                        var soldItem = TarkovDev.Items.Find(item => item.Id == e.LogContent.SoldItemId);
                        if (soldItem?.Name != null) // Link null check removed as it's not critical for the message
                        {
                            Console.WriteLine($"MainBlazorUI: Adding message for sale of {soldItem.Name}");
                            messageLog?.AddMessage($"MainUI: {e.LogContent.Buyer} purchased {String.Format("{0:n0}", e.LogContent.SoldItemCount)} {soldItem.Name} for {String.Join(", ", received.ToArray())}", "flea", soldItem.Link); // Keep link if available
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in MainBlazorUI.Eft_FleaSold: {ex.Message}");
                messageLog?.AddMessage($"Error processing flea market sale in MainBlazorUI: {ex.Message}", "exception");
            }
        }


        /// <summary>
        /// Handles flea market offer expiration events by logging the expired item.
        /// </summary>
        private void Eft_FleaOfferExpired(object? sender, LogContentEventArgs<FleaExpiredMessageLogContent> e)
        {
            // Use null-conditional operator for e.LogContent
            if (TarkovDev.Items == null || e.LogContent?.ItemId == null)
            {
                return;
            }
            var unsoldItem = TarkovDev.Items.Find(item => item.Id == e.LogContent.ItemId);
            // Check Name before accessing Link
            if (unsoldItem?.Name != null)
            {
                messageLog?.AddMessage($"Your offer for {unsoldItem.Name} (x{e.LogContent.ItemCount}) expired", "flea", unsoldItem.Link); // Keep link if available
            }
        }

        private void Eft_ExceptionThrown(object? sender, ExceptionEventArgs e)
        {
            // Log full exception details
            messageLog?.AddMessage($"Error {e.Context}: {e.Exception}", "exception");
        }

        private async void Eft_RaidStarting(object? sender, RaidInfoEventArgs e)
        {
            if (Properties.Settings.Default.raidStartAlert)
            {
                try
                {
                    // always notify if the GameStarting event appeared
                    await Sound.Play("raid_starting");
                }
                catch (Exception ex)
                {
                    // Log full exception details
                    messageLog?.AddMessage($"Error playing raid starting sound: {ex}", "exception");
                }
            }
        }

        private async void Eft_RaidStart(object? sender, RaidInfoEventArgs e)
        {
            // Add null check for e.RaidInfo early
            if (e.RaidInfo == null)
            {
                messageLog?.AddMessage("Error: Raid start event received with null RaidInfo.", "exception");
                return;
            }

            Stats.AddRaid(e); // Assumes Stats.AddRaid handles potential nulls internally if needed

            var mapName = e.RaidInfo.Map;

            // Debug logging to diagnose the map selection issue
            messageLog?.AddMessage($"DEBUG: Raw map name from raid info: {mapName}", "debug");
            messageLog?.AddMessage($"DEBUG: Available maps count: {TarkovDev.Maps.Count}", "debug");
            if (TarkovDev.Maps.Count > 0)
            {
                messageLog?.AddMessage($"DEBUG: Available map IDs: {string.Join(", ", TarkovDev.Maps.Select(m => m.NameId))}", "debug");
            }

            // Try case-insensitive match first
            var map = TarkovDev.Maps.Find(m => string.Equals(m.NameId, mapName, StringComparison.OrdinalIgnoreCase));

            // If not found, try exact match as before
            if (map == null)
            {
                map = TarkovDev.Maps.Find(m => m.NameId == mapName);

                // Still not found after both attempts
                if (map == null)
                {
                    mapName = "Unknown Map"; // Handle case where map couldn't be identified
                }
                else
                {
                    mapName = map.Name;
                }
            }
            else
            {
                mapName = map.Name;
            }

            if (!e.RaidInfo.Reconnected && e.RaidInfo.RaidType != RaidType.Unknown)
            {
                MonitorMessage monMessage = new($"Starting {e.RaidInfo.RaidType} raid on {mapName}");
                if (map != null && e.RaidInfo.StartedTime != null && map.HasGoons())
                {
                    AddGoonsButton(monMessage, e.RaidInfo);
                }
                else if (map == null) // Handle case where map couldn't be identified
                {
                    monMessage.Message = $"Starting {e.RaidInfo.RaidType} raid on:";
                    MonitorMessageSelect select = new();

                    // Debug the maps data
                    messageLog?.AddMessage($"DEBUG: Creating dropdown with {TarkovDev.Maps.Count} options", "debug");

                    // Add options with clearer naming
                    foreach (var gameMap in TarkovDev.Maps)
                    {
                        select.Options.Add(new(gameMap.Name, gameMap.NameId));
                        // Debug log each map added to dropdown
                        messageLog?.AddMessage($"DEBUG: Added map option: {gameMap.Name} (ID: {gameMap.NameId})", "debug");
                    }

                    select.Placeholder = "Select map";
                    monMessage.Selects.Add(select);
                    MonitorMessageButton mapButton = new("Set map", Icons.Material.Filled.Map);
                    mapButton.OnClick += async () =>
                    {
                        var selectedOption = select.Selected; // Capture selected value
                        if (selectedOption?.Value == null)
                        {
                            return;
                        }
                        var selectedValue = selectedOption.Value;
                        var currentRaidInfo = e.RaidInfo; // Capture raid info

                        if (currentRaidInfo != null) // Check captured raid info
                        {
                            currentRaidInfo.Map = selectedValue;
                            monMessage.Message = $"Starting {currentRaidInfo.RaidType} raid on {selectedOption.Text}";
                            monMessage.Buttons.Clear();
                            monMessage.Selects.Clear();
                            if (Properties.Settings.Default.autoNavigateMap)
                            {
                                var selectedMap = TarkovDev.Maps.Find(m => m.NameId == selectedValue);
                                if (selectedMap != null)
                                {
                                    await SocketClient.NavigateToMap(selectedMap);
                                }
                            }
                        }
                    };
                    monMessage.Buttons.Add(mapButton);
                }
                messageLog?.AddMessage(monMessage);

                // This check seems redundant with Eft_RaidStarting, consider removing or clarifying logic
                if (Properties.Settings.Default.raidStartAlert && e.RaidInfo.StartingTime == null)
                {
                    // await Task.Run(() => Sound.Play("raid_starting")); // Already played in Eft_RaidStarting?
                }
            }
            else // Reconnected or Unknown RaidType
            {
                messageLog?.AddMessage($"Re-entering raid on {mapName}");
            }

            if (Properties.Settings.Default.runthroughAlert && !e.RaidInfo.Reconnected && (e.RaidInfo.RaidType == RaidType.PMC || e.RaidInfo.RaidType == RaidType.PVE))
            {
                runthroughTimer?.Stop();
                runthroughTimer?.Start();
            }

            // Safely capture profile
            var currentProfile = eft?.CurrentProfile;
            if (Properties.Settings.Default.submitQueueTime && e.RaidInfo.QueueTime > 0 && e.RaidInfo.RaidType != RaidType.Unknown && e.RaidInfo.Map != null && currentProfile != null)
            {
                try
                {
                    // Use captured profile
                    await Task.Run(() => TarkovDev.PostQueueTime(e.RaidInfo.Map, (int)Math.Round(e.RaidInfo.QueueTime), e.RaidInfo.RaidType.ToString().ToLower(), currentProfile.Type));
                }
                catch (Exception ex)
                {
                    // Log full exception details
                    messageLog?.AddMessage($"Error submitting queue time: {ex}", "exception");
                }
            }
        }


        private void AddGoonsButton(MonitorMessage monMessage, RaidInfo raidInfo)
        {
            // Add null checks for raidInfo and map
            if (raidInfo?.Map == null || raidInfo.StartedTime == null) return;

            var mapName = raidInfo.Map;
            var map = TarkovDev.Maps.Find(m => m.NameId == mapName);
            if (map == null || !map.HasGoons()) return; // Exit if map not found or no goons

            mapName = map.Name; // Use the found map's name

            MonitorMessageButton goonsButton = new($"Report Goons", Icons.Material.Filled.Groups);
            goonsButton.OnClick = async () =>
            {
                // Capture necessary info safely before async operation
                var currentEft = eft;
                var currentProfile = currentEft?.CurrentProfile;
                var currentMapId = raidInfo.Map; // Already checked not null
                var currentStartedTime = raidInfo.StartedTime; // Already checked not null

                if (currentEft != null && currentProfile != null)
                {
                    try
                    {
                        int accountId = currentEft.AccountId;
                        var profileType = currentProfile.Type;
                        // Use captured non-null values
                        await TarkovDev.PostGoonsSighting(currentMapId, (DateTime)currentStartedTime, accountId, profileType);
                        messageLog?.AddMessage($"Goons reported on {mapName}", "info"); // Use updated mapName
                    }
                    catch (Exception ex)
                    {
                        // Log full exception details
                        messageLog?.AddMessage($"Error reporting goons: {ex}", "exception");
                    }
                }
                else
                {
                    messageLog?.AddMessage($"Cannot report goons: EFT instance or profile not available.", "warning");
                }

                // Safely remove button
                if (monMessage?.Buttons != null)
                {
                    monMessage.Buttons.Remove(goonsButton);
                }
            };
            goonsButton.Confirm = new(
                $"Report Goons on {mapName}", // Use updated mapName
                "<p>Please only submit a report if you saw the goons in this raid.</p><p><strong>Notice:</strong> By submitting a goons report, you consent to collection of your IP address and EFT account id for report verification purposes.</p>",
                "Submit report", "Cancel"
            );
            goonsButton.Timeout = TimeSpan.FromMinutes(120).TotalMilliseconds;
            monMessage.Buttons.Add(goonsButton); // Assumes Buttons is initialized
        }


        private void Eft_RaidExited(object? sender, RaidExitedEventArgs e)
        {
            if (groupManager != null) groupManager.Stale = true;
            runthroughTimer?.Stop();
            try
            {
                var mapName = e.Map; // Can e.Map be null? Assume yes for safety.
                if (mapName != null)
                {
                    var map = TarkovDev.Maps.Find(m => m.NameId == mapName);
                    if (map != null) mapName = map.Name;
                }
                else
                {
                    mapName = "Unknown Map";
                }
                // Use Task.Run only if AddMessage is potentially blocking, otherwise call directly
                messageLog?.AddMessage($"Exited {mapName} raid", "raidleave");
                // If AddMessage needs to be offloaded:
                // await Task.Run(() => messageLog?.AddMessage($"Exited {mapName} raid", "raidleave"));
            }
            catch (Exception ex)
            {
                // Log full exception details
                messageLog?.AddMessage($"Error handling raid exit event: {ex}", "exception");
            }
        }

        /// <summary>
        /// Handles the window resize event, implementing minimize to tray functionality
        /// when enabled in settings.
        /// </summary>
        private void MainBlazorUI_Resize(object sender, EventArgs e)
        {
            try
            {
                if (this.WindowState == FormWindowState.Minimized && Properties.Settings.Default.minimizeToTray)
                {
                    Hide();
                    notifyIconTarkovMonitor.Visible = true;
                }
            }
            catch (Exception ex)
            {
                // Log full exception details
                messageLog?.AddMessage($"Error minimizing to tray: {ex}", "exception");
            }
        }

        /// <summary>
        /// Handles double-click on the tray icon to restore the window.
        /// </summary>
        private void notifyIconTarkovMonitor_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            try
            {
                Show();
                this.WindowState = FormWindowState.Normal;
                notifyIconTarkovMonitor.Visible = false;
            }
            catch (Exception ex)
            {
                // Log full exception details
                messageLog?.AddMessage($"Error restoring from tray: {ex}", "exception");
            }
        }

        /// <summary>
        /// Handles the quit menu item click event to close the application.
        /// </summary>
        private void menuItemQuit_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        /*private async Task UpdatePlayerLevel()
        {
            if (!TarkovTracker.ValidToken)
            {
                return;
            }
            var level = TarkovDev.GetLevel(await TarkovDev.GetExperience(eft.AccountId));
            if (level == TarkovTracker.Progress.Data.PlayerLevel)
            {
                return;
            }
        }*/

        private void SetupGameWatcherEvents()
        {
            if (eft == null) return;

            eft.FleaSold += Eft_FleaSold;
            eft.FleaOfferExpired += Eft_FleaOfferExpired;
            eft.ExceptionThrown += Eft_ExceptionThrown;
            eft.RaidStarting += Eft_RaidStarting;
            eft.RaidStarted += Eft_RaidStart;
            eft.RaidExited += Eft_RaidExited;
            eft.RaidEnded += Eft_RaidEnded;
            eft.ExitedPostRaidMenus += Eft_ExitedPostRaidMenus;
            eft.TaskStarted += Eft_TaskStarted;
            eft.TaskFailed += Eft_TaskFailed;
            eft.TaskFinished += Eft_TaskFinished;
            eft.NewLogData += Eft_NewLogData;
            eft.GroupInviteAccept += Eft_GroupInviteAccept;
            eft.GroupUserLeave += Eft_GroupUserLeave;
            eft.GroupRaidSettings += Eft_GroupRaidSettings;
            eft.GroupMemberReady += Eft_GroupMemberReady;
            eft.GroupDisbanded += Eft_GroupDisbanded;
            eft.MatchingAborted += Eft_GroupStaleEvent;
            eft.GameStarted += Eft_GroupStaleEvent;
            eft.MapLoading += Eft_MapLoading;
            eft.MatchFound += Eft_MatchFound;
            eft.MapLoaded += Eft_MapLoaded;
            eft.ProfileChanged += Eft_ProfileChanged;
        }

        private void InitializeTimers()
        {
            runthroughTimer = new System.Timers.Timer(Properties.Settings.Default.runthroughTime.TotalMilliseconds)
            {
                AutoReset = false,
                Enabled = false
            };
            runthroughTimer.Elapsed += RunthroughTimer_Elapsed;

            scavCooldownTimer = new System.Timers.Timer(TimeSpan.FromSeconds(TarkovDev.ScavCooldownSeconds()).TotalMilliseconds)
            {
                AutoReset = false,
                Enabled = false
            };
            scavCooldownTimer.Elapsed += ScavCooldownTimer_Elapsed;
        }

        private void SetupBlazorServices()
        {
            var services = new ServiceCollection();
            services.AddWindowsFormsBlazorWebView();
            services.AddMudServices();

            // Add core services - ensure they are initialized before adding if possible
            // If initialized async (like timersManager), consider alternative DI strategies
            // or handle potential nulls where injected.
            if (eft != null) services.AddSingleton<GameWatcher>(eft);
            if (messageLog != null)
            {
                services.AddSingleton<IMessageLog>(messageLog);
                services.AddSingleton<MessageLog>(messageLog); // Register concrete type as well
            }
            if (logRepository != null) services.AddSingleton<LogRepository>(logRepository);
            if (groupManager != null) services.AddSingleton<GroupManager>(groupManager);
            // Adding timersManager here might provide a null instance initially.
            // It's initialized in InitializeAsync. This might require adjustment based on usage.
            // For now, keep as is, assuming consumers handle potential null or delayed initialization.
            if (timersManager != null) services.AddSingleton<TimersManager>(timersManager);

            // Don't register any custom scroll handler - let the framework create its own

            blazorWebView1.HostPage = "wwwroot\\index.html";
            blazorWebView1.Services = services.BuildServiceProvider();
            blazorWebView1.RootComponents.Add<TarkovMonitor.Blazor.App>("#app");
            blazorWebView1.WebView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
        }
    }

    // Custom implementation for standard Blazor routing
    public class NoOpScrollToLocationHash : Microsoft.AspNetCore.Components.Routing.IScrollToLocationHash
    {
        public static void RefreshScrollPosition(Microsoft.AspNetCore.Components.NavigationManager _)
        {
            // No-op implementation
        }

        public Task RefreshScrollPositionForHash(string hash)
        {
            // No-op implementation
            return Task.CompletedTask;
        }
    }
}
