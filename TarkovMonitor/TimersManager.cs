using System.Diagnostics;

namespace TarkovMonitor
{
    /// <summary>
    /// Manages various in-game timers for Escape from Tarkov, including raid duration,
    /// run-through prevention time, and Scav cooldown periods.
    /// This class coordinates with the game state to provide accurate timing information.
    /// </summary>
    internal class TimersManager
    {
        /// <summary>
        /// Event triggered when the raid timer value changes (every second while in raid)
        /// </summary>
        public event EventHandler<TimerChangedEventArgs>? RaidTimerChanged;

        /// <summary>
        /// Event triggered when the run-through prevention timer changes
        /// Helps players track time needed to avoid "run-through" raid status
        /// </summary>
        public event EventHandler<TimerChangedEventArgs>? RunThroughTimerChanged;

        /// <summary>
        /// Event triggered when the Scav cooldown timer changes
        /// Tracks the cooldown period between Scav raids
        /// </summary>
        public event EventHandler<TimerChangedEventArgs>? ScavCooldownTimerChanged;

        // Timer state tracking variables
        private TimeSpan RunThroughRemainingTime;  // Time remaining to avoid run-through status
        private TimeSpan TimeInRaidTime;           // Current time spent in the raid
        private TimeSpan ScavCooldownTime;         // Current Scav cooldown duration

        // System.Threading.Timer instances for different timer functionalities
        private readonly System.Threading.Timer timerRaid;           // Tracks overall raid time
        private readonly System.Threading.Timer timerRunThrough;     // Tracks run-through prevention time
        private readonly System.Threading.Timer timerScavCooldown;   // Tracks Scav cooldown period

        // Cancellation token for graceful shutdown of timers
        private readonly CancellationTokenSource cancellationTokenSource = new();

        // Reference to the game watcher instance for game state monitoring
        private readonly GameWatcher eft;

        /// <summary>
        /// Initializes a new instance of the TimersManager class.
        /// Sets up timer instances and subscribes to game events.
        /// </summary>
        /// <param name="eft">GameWatcher instance for monitoring game state</param>
        public TimersManager(GameWatcher eft)
        {
            this.eft = eft;

            // Initialize timers with infinite delay (disabled state)
            // Timer intervals set to 1 second (1000ms) when activated
            timerRaid = new System.Threading.Timer(TimerRaid_Elapsed, null, Timeout.Infinite, 1000);
            timerRunThrough = new System.Threading.Timer(TimerRunThrough_Elapsed, null, Timeout.Infinite, 1000);
            timerScavCooldown = new System.Threading.Timer(TimerScavCooldown_Elapsed, null, Timeout.Infinite, 1000);

            // Make sure TarkovDev data is initialized before calculating cooldown
            if (TarkovDev.Traders.Count == 0 || TarkovDev.Stations.Count == 0)
            {
                Debug.WriteLine("TarkovDev data not initialized yet. Setting up event handlers.");

                // Initialize Scav cooldown time when API data is available
                TarkovTracker.ProgressRetrieved += (sender, e) =>
                {
                    ScavCooldownTime = TimeSpan.FromSeconds(TarkovDev.ScavCooldownSeconds());
                    Debug.WriteLine($"ScavCooldownTime updated from ProgressRetrieved: {ScavCooldownTime}");
                };
            }
            else
            {
                // Data is already available, initialize cooldown directly
                ScavCooldownTime = TimeSpan.FromSeconds(TarkovDev.ScavCooldownSeconds());
                Debug.WriteLine($"ScavCooldownTime initialized directly: {ScavCooldownTime}");
            }

            // Check for persistent Scav cooldown time
            int remainingSeconds = TarkovDev.GetRemainingScavCooldownSeconds();
            if (remainingSeconds > 0)
            {
                ScavCooldownTime = TimeSpan.FromSeconds(remainingSeconds);
                timerScavCooldown.Change(0, 1000);
                Debug.WriteLine($"Restored Scav cooldown timer: {ScavCooldownTime}");
            }

            // Load run-through time from user settings
            RunThroughRemainingTime = Properties.Settings.Default.runthroughTime;

            // Subscribe to game state events
            this.eft.RaidStarted += Eft_RaidStarted;
            this.eft.RaidEnded += Eft_RaidEnded;
        }

        /// <summary>
        /// Handles the start of a new raid.
        /// Resets and starts relevant timers for raid duration and run-through prevention.
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Raid information event arguments</param>
        private void Eft_RaidStarted(object? sender, RaidInfoEventArgs e)
        {
            // Skip timer initialization for reconnection to existing raid
            if (e.RaidInfo.Reconnected)
                return;

            // Reset timer values
            TimeInRaidTime = TimeSpan.Zero;
            RunThroughRemainingTime = Properties.Settings.Default.runthroughTime;

            // Start raid and run-through timers
            timerRaid.Change(0, 1000);
            timerRunThrough.Change(0, 1000);

            // Notify subscribers of initial timer values
            RaidTimerChanged?.Invoke(this, new TimerChangedEventArgs()
            {
                TimerValue = TimeInRaidTime
            });

            RunThroughTimerChanged?.Invoke(this, new TimerChangedEventArgs()
            {
                TimerValue = RunThroughRemainingTime
            });
        }

        /// <summary>
        /// Handles the end of a raid.
        /// Stops raid timers and initiates Scav cooldown if applicable.
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Raid information event arguments</param>
        private void Eft_RaidEnded(object? sender, RaidInfoEventArgs e)
        {
            // Add null check for RaidInfo
            if (e.RaidInfo == null)
            {
                Debug.WriteLine("Eft_RaidEnded: RaidInfo is null");
                return;
            }

            // Reset and stop run-through timer
            RunThroughRemainingTime = TimeSpan.Zero;
            timerRunThrough.Change(Timeout.Infinite, Timeout.Infinite);
            timerRaid.Change(Timeout.Infinite, Timeout.Infinite);

            Debug.WriteLine($"Eft_RaidEnded: {e.RaidInfo.RaidType}");

            // Start Scav cooldown timer if applicable (only for Scav or PVE raids, excluding reconnections)
            if (!e.RaidInfo.Reconnected && (e.RaidInfo.RaidType == RaidType.Scav || e.RaidInfo.RaidType == RaidType.PVE))
            {
                // Reset the Scav cooldown time to the full duration
                int cooldownSeconds = TarkovDev.ResetScavCoolDown(); // This now also persists the end time
                ScavCooldownTime = TimeSpan.FromSeconds(cooldownSeconds);
                Debug.WriteLine($"Starting Scav cooldown timer: {ScavCooldownTime}");

                // Start the timer
                timerScavCooldown.Change(0, 1000);

                // Notify subscribers of the initial cooldown time
                ScavCooldownTimerChanged?.Invoke(this, new TimerChangedEventArgs()
                {
                    TimerValue = ScavCooldownTime
                });
            }

            // Notify subscribers of final timer values
            RunThroughTimerChanged?.Invoke(this, new TimerChangedEventArgs()
            {
                TimerValue = RunThroughRemainingTime
            });
        }

        /// <summary>
        /// Timer callback for updating the raid duration.
        /// Increments the raid time every second and notifies subscribers.
        /// </summary>
        private void TimerRaid_Elapsed(object? state)
        {
            if (cancellationTokenSource.IsCancellationRequested)
                return;

            TimeInRaidTime += TimeSpan.FromSeconds(1);

            RaidTimerChanged?.Invoke(this, new TimerChangedEventArgs()
            {
                TimerValue = TimeInRaidTime
            });
        }

        /// <summary>
        /// Timer callback for updating the run-through prevention timer.
        /// Decrements the remaining time and stops when zero is reached.
        /// </summary>
        private void TimerRunThrough_Elapsed(object? state)
        {
            if (cancellationTokenSource.IsCancellationRequested)
                return;

            if (RunThroughRemainingTime > TimeSpan.Zero)
            {
                RunThroughRemainingTime -= TimeSpan.FromSeconds(1);
            }
            else
            {
                timerRunThrough.Change(Timeout.Infinite, Timeout.Infinite);
            }

            RunThroughTimerChanged?.Invoke(this, new TimerChangedEventArgs()
            {
                TimerValue = RunThroughRemainingTime
            });
        }

        /// <summary>
        /// Timer callback for updating the Scav cooldown timer.
        /// Decrements the cooldown time and resets when zero is reached.
        /// </summary>
        private void TimerScavCooldown_Elapsed(object? state)
        {
            if (cancellationTokenSource.IsCancellationRequested)
                return;

            if (ScavCooldownTime > TimeSpan.Zero)
            {
                ScavCooldownTime -= TimeSpan.FromSeconds(1);
            }
            else
            {
                timerScavCooldown.Change(Timeout.Infinite, Timeout.Infinite);
                ScavCooldownTime = TimeSpan.FromSeconds(TarkovDev.ScavCooldownSeconds());
            }

            ScavCooldownTimerChanged?.Invoke(this, new TimerChangedEventArgs()
            {
                TimerValue = ScavCooldownTime
            });
        }

        /// <summary>
        /// Manually starts or synchronizes the Scav cooldown timer with the current cooldown duration.
        /// Useful for external systems to ensure the timer is accurate.
        /// </summary>
        /// <returns>The current cooldown time in seconds</returns>
        public int StartScavCooldownTimer()
        {
            // Get the latest remaining cooldown time from TarkovDev
            int remainingSeconds = TarkovDev.GetRemainingScavCooldownSeconds();

            // Update the timer value
            ScavCooldownTime = TimeSpan.FromSeconds(remainingSeconds);

            // Only start the timer if there's time remaining
            if (remainingSeconds > 0)
            {
                timerScavCooldown.Change(0, 1000);

                // Debug logging with null check for ScavAvailableTime
                Debug.WriteLine($"Starting Scav cooldown timer with {remainingSeconds} seconds remaining.");

                // Notify subscribers of the updated time
                ScavCooldownTimerChanged?.Invoke(this, new TimerChangedEventArgs()
                {
                    TimerValue = ScavCooldownTime
                });
            }

            return remainingSeconds;
        }
    }

    /// <summary>
    /// Event arguments for timer change notifications.
    /// Contains the current value of the timer that triggered the event.
    /// </summary>
    public class TimerChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The current value of the timer
        /// </summary>
        public TimeSpan TimerValue { get; set; }
    }
}
