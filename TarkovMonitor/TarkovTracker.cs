using System.Net;
using System.Text.Json;
using System.Transactions;
using Refit;

// TO DO: Implement rate limit policy of 15 requests per minute

namespace TarkovMonitor
{
    internal class TarkovTracker
    {
        internal interface ITarkovTrackerAPI
        {
            HttpClient Client { get; }

            [Get("/token")]
            Task<TokenResponse> TestToken();

            [Get("/progress")]
            Task<ProgressResponse> GetProgress();

            [Post("/progress/task/{id}")]
            Task<string> SetTaskStatus(string id, [Body] TaskStatusBody body);

            [Post("/progress/tasks")]
            Task<string> SetTaskStatuses([Body] List<TaskStatusBody> body);
        }

        internal static readonly string[] AVAILABLE_DOMAINS = new[] { "tarkovtracker.io", "tarkovtracker.org" };
        private static string currentDomain = AVAILABLE_DOMAINS[0];
        private static ITarkovTrackerAPI? api;

        private static ITarkovTrackerAPI Api
        {
            get
            {
                if (api == null)
                {
                    InitializeApi();
                }
                return api!;
            }
        }

        private static void InitializeApi()
        {
            // Always use the domain from settings if available
            if (!string.IsNullOrEmpty(Properties.Settings.Default.tarkovTrackerDomain))
            {
                currentDomain = Properties.Settings.Default.tarkovTrackerDomain;
            }

            // Create a handler that adds the proper bearer token
            var handler = new AuthorizationMessageHandler()
            {
                InnerHandler = new HttpClientHandler()
            };
            handler.AuthorizationScheme = "Bearer";
            handler.GetToken = () => Task.FromResult(GetToken(currentProfile ?? ""));

            var client = new HttpClient(handler) { BaseAddress = new Uri($"https://{currentDomain}/api/v2") };
            api = RestService.For<ITarkovTrackerAPI>(client);
        }

        // Custom message handler for authorization
        private class AuthorizationMessageHandler : DelegatingHandler
        {
            public string AuthorizationScheme { get; set; } = "";
            public Func<Task<string>> GetToken { get; set; } = () => Task.FromResult("");

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var token = await GetToken();
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(AuthorizationScheme, token);
                }
                return await base.SendAsync(request, cancellationToken);
            }
        }

        static TarkovTracker()
        {
            tokens = JsonSerializer.Deserialize<Dictionary<string, string>>(Properties.Settings.Default.tarkovTrackerTokens) ?? tokens;
            if (!string.IsNullOrEmpty(Properties.Settings.Default.tarkovTrackerDomain))
            {
                currentDomain = Properties.Settings.Default.tarkovTrackerDomain;
            }
        }

        public static ProgressResponse Progress { get; private set; } = new();
        public static bool ValidToken { get; private set; } = false;
        public static bool HasWritePermission { get; private set; } = false;
        private static readonly Dictionary<string, string> tokens = new();
        private static string currentProfile = "";
        public static string CurrentProfileId { get { return currentProfile; } }

        public static event EventHandler<EventArgs>? TokenValidated;
        public static event EventHandler<EventArgs>? TokenInvalid;
        public static event EventHandler<EventArgs>? ProgressRetrieved;

        public static string GetToken(string profileId)
        {
            if (!tokens.ContainsKey(profileId))
            {
                return "";
            }
            return tokens[profileId];
        }

        public static void SetToken(string profileId, string token)
        {
            if (profileId == "")
            {
                throw new Exception("No PVP or PVE profile initialized, please launch Escape from Tarkov first");
            }
            tokens[profileId] = token;
            Properties.Settings.Default.tarkovTrackerTokens = JsonSerializer.Serialize(tokens);
            Properties.Settings.Default.Save();
        }

        public static async Task<ProgressResponse> SetProfile(string profileId)
        {
            if (profileId == "")
            {
                throw new Exception("Can't set PVP or PVE profile, please launch Escape from Tarkov and then restart this application");
            }

            if (currentProfile == profileId)
            {
                return Progress;
            }
            var newToken = GetToken(profileId);
            var oldToken = GetToken(currentProfile);
            currentProfile = profileId;
            if (oldToken == newToken)
            {
                return Progress;
            }
            if (newToken == "" || newToken.Length != 22)
            {
                ValidToken = false;
                Progress = new();
                return Progress;
            }
            await TestToken(newToken);
            return Progress;
        }

        private static void SyncStoredStatus(string questId, TaskStatus status)
        {
            var storedStatus = Progress.Data.TasksProgress.Find(ts => ts.Id == questId);
            if (storedStatus == null)
            {
                storedStatus = new()
                {
                    Id = questId,
                };
                Progress.Data.TasksProgress.Add(storedStatus);
            }
            if (status == TaskStatus.Finished && !storedStatus.Complete)
            {
                storedStatus.Complete = true;
                storedStatus.Failed = false;
                storedStatus.Invalid = false;
            }
            if (status == TaskStatus.Failed && !storedStatus.Failed)
            {
                storedStatus.Complete = false;
                storedStatus.Failed = true;
                storedStatus.Invalid = false;
            }
            if (status == TaskStatus.Started && (storedStatus.Failed || storedStatus.Invalid || storedStatus.Complete))
            {
                storedStatus.Complete = false;
                storedStatus.Failed = false;
                storedStatus.Invalid = false;
            }
        }

        private static async Task<T> TryApiCall<T>(Func<Task<T>> apiCall)
        {
            try
            {
                return await apiCall();
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Try other domain if unauthorized
                    string originalDomain = currentDomain;
                    string otherDomain = AVAILABLE_DOMAINS.First(d => d != originalDomain);

                    try
                    {
                        currentDomain = otherDomain;
                        api = null; // Force re-initialization with new domain
                        InitializeApi(); // Create new API with the new domain
                        var result = await apiCall();

                        // Save the working domain
                        Properties.Settings.Default.tarkovTrackerDomain = otherDomain;
                        Properties.Settings.Default.Save();

                        return result;
                    }
                    catch
                    {
                        // If other domain also fails, restore original and throw original exception
                        currentDomain = originalDomain;
                        api = null;
                        InitializeApi(); // Restore API with original domain
                        throw;
                    }
                }
                throw;
            }
        }

        public static async Task<string> SetTaskStatus(string questId, TaskStatus status)
        {
            if (!ValidToken)
            {
                throw new Exception("Invalid token");
            }
            if (!HasWritePermission)
            {
                throw new Exception("Your TarkovTracker API token does not have write permissions. Please generate a new token with write permissions.");
            }
            try
            {
                await TryApiCall(() => Api.SetTaskStatus(questId, TaskStatusBody.From(status)));
                SyncStoredStatus(questId, status);
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    InvalidTokenException();
                }
                if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new Exception("Rate limited by Tarkov Tracker API");
                }
                throw new Exception($"Invalid TarkovTracker API response code: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"TarkovTracker API error: {ex.Message}");
            }
            return "success";
        }

        public static async Task<string> SetTaskComplete(string questId)
        {
            await SetTaskStatus(questId, TaskStatus.Finished);
            try
            {
                TarkovDev.Tasks.ForEach(task =>
                {
                    foreach (var failCondition in task.FailConditions)
                    {
                        if (failCondition.Task == null)
                        {
                            continue;
                        }
                        if (failCondition.Task.Id == questId && failCondition.Status.Contains("complete"))
                        {
                            foreach (var taskStatus in Progress.Data.TasksProgress)
                            {
                                if (taskStatus.Id == failCondition.Task.Id)
                                {
                                    taskStatus.Failed = true;
                                    break;
                                }
                            }
                            break;
                        }
                    }
                });
            }
            catch (Exception)
            {
                // do something?
            }
            return "success";
        }

        public static async Task<string> SetTaskFailed(string questId)
        {
            return await SetTaskStatus(questId, TaskStatus.Failed);
        }

        public static async Task<string> SetTaskStarted(string questId)
        {
            foreach (var taskStatus in Progress.Data.TasksProgress)
            {
                if (taskStatus.Id != questId)
                {
                    continue;
                }
                if (taskStatus.Failed)
                {
                    return await SetTaskStatus(questId, TaskStatus.Started);
                }
                break;
            }
            return "task not marked as failed";
        }

        public static async Task<string> SetTaskStatuses(Dictionary<string, TaskStatus> statuses)
        {
            if (!ValidToken)
            {
                throw new Exception("Invalid token");
            }
            if (!HasWritePermission)
            {
                throw new Exception("Your TarkovTracker API token does not have write permissions. Please generate a new token with write permissions.");
            }
            try
            {
                List<TaskStatusBody> body = new();
                foreach (var kvp in statuses)
                {
                    TaskStatusBody status = TaskStatusBody.From(kvp.Value);
                    status.Id = kvp.Key;
                    body.Add(status);
                }
                await TryApiCall(() => Api.SetTaskStatuses(body));
                foreach (var kvp in statuses)
                {
                    SyncStoredStatus(kvp.Key, kvp.Value);
                }
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    InvalidTokenException();
                }
                if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new Exception("Rate limited by Tarkov Tracker API");
                }
                throw new Exception($"Invalid TarkovTracker API response code: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"TarkovTracker API error: {ex.Message}");
            }
            return "success";
        }

        public static async Task<ProgressResponse> GetProgress()
        {
            if (!ValidToken)
            {
                throw new Exception("Invalid token");
            }
            try
            {
                Progress = await TryApiCall(() => Api.GetProgress());
                ProgressRetrieved?.Invoke(null, new EventArgs());
                return Progress;
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    InvalidTokenException();
                }
                if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new Exception("Rate limited by Tarkov Tracker API");
                }
                throw new Exception($"Invalid TarkovTracker response code: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"TarkovTracker API error: {ex.Message}");
            }
        }

        public static async Task<TokenResponse> TestToken(string token)
        {
            if (token.Length != 22)
            {
                throw new Exception("Invalid token length");
            }

            // Try current domain first
            try
            {
                // Create a temporary client with the token for validation
                var handler = new AuthorizationMessageHandler()
                {
                    InnerHandler = new HttpClientHandler()
                };
                handler.AuthorizationScheme = "Bearer";
                handler.GetToken = () => Task.FromResult(token);

                var client = new HttpClient(handler) { BaseAddress = new Uri($"https://{currentDomain}/api/v2") };
                var tempApi = RestService.For<ITarkovTrackerAPI>(client);

                var response = await tempApi.TestToken();
                ValidToken = true;
                HasWritePermission = response.Permissions.Contains("WP");
                TokenValidated?.Invoke(null, EventArgs.Empty);
                return response;
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode != HttpStatusCode.Unauthorized)
                {
                    if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        throw new Exception("Rate limited by Tarkov Tracker API");
                    }
                    throw new Exception($"Invalid TarkovTracker API response code: {ex.Message}");
                }
            }

            // If current domain failed, try other domains
            string originalDomain = currentDomain;
            foreach (var domain in AVAILABLE_DOMAINS.Where(d => d != originalDomain))
            {
                try
                {
                    currentDomain = domain;

                    // Create a temporary client with the token for validation
                    var handler = new AuthorizationMessageHandler()
                    {
                        InnerHandler = new HttpClientHandler()
                    };
                    handler.AuthorizationScheme = "Bearer";
                    handler.GetToken = () => Task.FromResult(token);

                    var client = new HttpClient(handler) { BaseAddress = new Uri($"https://{domain}/api/v2") };
                    var tempApi = RestService.For<ITarkovTrackerAPI>(client);

                    var response = await tempApi.TestToken();
                    ValidToken = true;
                    HasWritePermission = response.Permissions.Contains("WP");

                    // Only save the domain to settings if we found a working one
                    Properties.Settings.Default.tarkovTrackerDomain = domain;
                    Properties.Settings.Default.Save();

                    api = null; // Force re-initialization with new domain

                    TokenValidated?.Invoke(null, EventArgs.Empty);
                    return response;
                }
                catch (ApiException ex)
                {
                    if (ex.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        continue; // Try next domain
                    }
                    if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        throw new Exception("Rate limited by Tarkov Tracker API");
                    }
                    throw new Exception($"Invalid TarkovTracker API response code: {ex.Message}");
                }
                catch (Exception)
                {
                    continue; // Try next domain
                }
            }

            // If no domain worked, restore the original domain
            currentDomain = originalDomain;
            api = null;

            ValidToken = false;
            HasWritePermission = false;
            TokenInvalid?.Invoke(null, EventArgs.Empty);
            throw new Exception("Token validation failed on all available domains");
        }

        private static void InvalidTokenException()
        {
            Progress = new();
            ValidToken = false;
            HasWritePermission = false;
            TokenInvalid?.Invoke(null, new EventArgs());
            throw new Exception("Tarkov Tracker API token is invalid");
        }

        public static bool HasAirFilter()
        {
            if (Progress == null)
            {
                return false;
            }
            var airFilterStation = TarkovDev.Stations.Find(s => s.NormalizedName == "air-filtering-unit");
            if (airFilterStation == null)
            {
                return false;
            }
            var stationLevel = airFilterStation.Levels.FirstOrDefault();
            if (stationLevel == null)
            {
                return false;
            }
            var built = Progress.Data.HideoutModulesProgress.Find(m => m.Id == stationLevel.Id && m.Complete);
            return built != null;
        }

        public class TokenResponse
        {
            public List<string> Permissions { get; set; } = new List<string>();
            public string Token { get; set; } = string.Empty;
        }

        public class ProgressResponse
        {
            public ProgressResponseData Data { get; set; } = new();
            public ProgressResponseMeta Meta { get; set; } = new();
        }

        public class ProgressResponseData
        {
            public List<ProgressResponseTask> TasksProgress { get; set; } = new();
            public List<ProgressResponseHideoutModules> HideoutModulesProgress { get; set; } = new();
            public string? DisplayName { get; set; }
            public string UserId { get; set; } = string.Empty;
            public int PlayerLevel { get; set; }
            public int GameEdition { get; set; }
            public string PmcFaction { get; set; } = string.Empty;
        }

        public class ProgressResponseTask
        {
            public string Id { get; set; } = string.Empty;
            public bool Complete { get; set; }
            public bool Invalid { get; set; }
            public bool Failed { get; set; }
        }
        public class ProgressResponseHideoutModules
        {
            public string Id { get; set; } = string.Empty;
            public bool Complete { get; set; }
        }
        public class ProgressResponseMeta
        {
            public string self { get; set; } = string.Empty;
        }
        public class TaskStatusBody
        {
            public string? Id { get; set; }
            public string State { get; private set; }
            private TaskStatusBody(string newState)
            {
                State = newState;
            }
            public static TaskStatusBody Completed => new("completed");
            public static TaskStatusBody Uncompleted => new("uncompleted");
            public static TaskStatusBody Failed => new("failed");
            public static TaskStatusBody From(TaskStatus code)
            {
                if (code == TaskStatus.Finished)
                {
                    return TaskStatusBody.Completed;
                }
                if (code == TaskStatus.Failed)
                {
                    return TaskStatusBody.Failed;
                }
                return TaskStatusBody.Uncompleted;
            }
            public static TaskStatusBody From(MessageType messageType)
            {
                return TaskStatusBody.From((TaskStatus)messageType);
            }
        }
    }
}
