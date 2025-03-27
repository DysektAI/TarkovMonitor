using Refit;

namespace TarkovMonitor
{
    /// <summary>
    /// Handles automatic version checking functionality for the TarkovMonitor application.
    /// This class periodically checks GitHub for new releases and notifies the application when updates are available.
    /// </summary>
    internal class UpdateCheck
    {
        /// <summary>
        /// Interface defining the GitHub API endpoints used for version checking.
        /// Implemented automatically by Refit.
        /// </summary>
        internal interface IGitHubAPI
        {
            /// <summary>
            /// Gets the latest release information from the GitHub repository.
            /// Uses a custom user-agent to identify the application in API requests.
            /// </summary>
            /// <returns>Task containing release data including version and URL</returns>
            [Get("/releases/latest")]
            [Headers("user-agent: tarkov-monitor")]
            Task<ReleaseData> GetLatestRelease();
        }

        // GitHub repository path for the application
        private static readonly string repo = "the-hideout/TarkovMonitor";

        // Timer that triggers periodic update checks
        private static readonly System.Timers.Timer updateCheckTimer;

        // Refit-generated API client for GitHub
        private static readonly IGitHubAPI api = RestService.For<IGitHubAPI>($"https://api.github.com/repos/{repo}");

        /// <summary>
        /// Event triggered when a new version is detected.
        /// Subscribers can handle this to notify users of available updates.
        /// </summary>
        public static event EventHandler<NewVersionEventArgs>? NewVersion;

        /// <summary>
        /// Event triggered when an error occurs during version checking.
        /// Subscribers can handle this to log errors or notify users of issues.
        /// </summary>
        public static event EventHandler<ExceptionEventArgs>? Error;

        /// <summary>
        /// Static constructor that initializes the update check timer.
        /// Sets up a daily check for new versions.
        /// </summary>
        static UpdateCheck()
        {
            updateCheckTimer = new(TimeSpan.FromDays(1).TotalMilliseconds)
            {
                AutoReset = true,  // Timer will automatically restart after each elapsed event
                Enabled = true     // Timer starts immediately
            };
            updateCheckTimer.Elapsed += UpdateCheckTimer_Elapsed;
        }

        /// <summary>
        /// Handler for timer elapsed events.
        /// Triggers the version check when the timer interval has passed.
        /// </summary>
        private static void UpdateCheckTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            CheckForNewVersion();
        }

        /// <summary>
        /// Performs the actual version check by comparing local version with the latest GitHub release.
        /// This method:
        /// 1. Fetches the latest release from GitHub
        /// 2. Compares the remote version with the local version
        /// 3. Raises the NewVersion event if a newer version is available
        /// </summary>
        public static async void CheckForNewVersion()
        {
            try
            {
                // Get latest release information from GitHub
                var release = await api.GetLatestRelease();
                Version remoteVersion = new Version(release.Tag_name);
                Version localVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ??
                    throw new Exception("Could not retrieve version from assembly");

                // Compare versions - if local version is older (-1), notify about new version
                if (localVersion.CompareTo(remoteVersion) == -1)
                {
                    NewVersion?.Invoke(null, new() { Version = remoteVersion, Uri = new(release.Html_url) });
                }
            }
            catch (ApiException ex)
            {
                // Handle API-specific errors (e.g., rate limiting, invalid responses)
                Error?.Invoke(null, new(new Exception($"Invalid GitHub API response code: {ex.Message}"), "checking for new version"));
            }
            catch (Exception ex)
            {
                // Handle general errors
                Error?.Invoke(null, new(new Exception($"GitHub API error: {ex.Message}"), "checking for new version"));
            }
        }

        /// <summary>
        /// Data structure representing the relevant information from a GitHub release.
        /// </summary>
        public class ReleaseData
        {
            /// <summary>
            /// The version tag of the release (e.g., "1.0.0")
            /// </summary>
            public required string Tag_name { get; set; }

            /// <summary>
            /// The web URL for the release page
            /// </summary>
            public required string Html_url { get; set; }
        }
    }

    /// <summary>
    /// Event arguments for when a new version is detected.
    /// Contains the new version number and the URL where it can be downloaded.
    /// </summary>
    public class NewVersionEventArgs : EventArgs
    {
        public required Version Version { get; set; }
        public required Uri Uri { get; set; }
    }
}

/* Release Process Instructions:
 * To release a new version:
 * 1. Checkout main/master (assuming everything is merged already)
 * 2. Tag the current commit (eg. git tag 1.0.1.2)
 * 3. Push the tag to GitHub (git push origin 1.0.1.2)
 */
