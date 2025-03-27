using NAudio.Wave;

namespace TarkovMonitor
{
    /// <summary>
    /// Sound management class for handling audio notifications in the Tarkov Monitor application.
    /// This class provides functionality for playing custom and built-in sound notifications,
    /// managing custom sound files, and handling audio playback devices.
    /// 
    /// To add new text to speech voices, leverage the site "https://ttsmp3.com/" and use 
    /// "British English / Brian" for results that match existing voices.
    /// </summary>
    internal class Sound
    {
        /// <summary>
        /// Gets the application data folder path where user-specific data is stored.
        /// </summary>
        public static string AppDataFolder => Application.UserAppDataPath;

        /// <summary>
        /// Gets the path to the custom sounds directory within the application data folder.
        /// This is where user-defined custom sound files are stored.
        /// </summary>
        public static string CustomSoundsPath => Path.Join(AppDataFolder, "sounds");

        /// <summary>
        /// Dictionary tracking whether specific sound keys have custom sound files.
        /// Key: Sound identifier, Value: Boolean indicating if a custom sound exists
        /// </summary>
        private static readonly Dictionary<string, bool> customSounds = new();

        /// <summary>
        /// Generates the full file path for a sound file based on its key.
        /// </summary>
        /// <param name="key">The identifier for the sound file</param>
        /// <returns>Full path to the sound file in the custom sounds directory</returns>
        public static string SoundPath(string key)
        {
            return Path.Join(CustomSoundsPath, $"{key}.mp3");
        }

        /// <summary>
        /// Sets a custom sound file for a specific sound key.
        /// Creates the custom sounds directory if it doesn't exist and copies the provided sound file.
        /// </summary>
        /// <param name="key">The identifier for the sound</param>
        /// <param name="path">Source path of the custom sound file to copy</param>
        public static void SetCustomSound(string key, string path)
        {
            if (!Directory.Exists(CustomSoundsPath))
            {
                Directory.CreateDirectory(CustomSoundsPath);
            }
            string customPath = SoundPath(key);
            File.Copy(path, customPath);
            customSounds[key] = true;
        }

        /// <summary>
        /// Removes a custom sound file for a specific sound key.
        /// If the sound isn't custom or doesn't exist, the method returns without action.
        /// </summary>
        /// <param name="key">The identifier for the sound to remove</param>
        public static void RemoveCustomSound(string key)
        {
            if (!customSounds.ContainsKey(key))
            {
                return;
            }
            if (!customSounds[key])
            {
                return;
            }
            File.Delete(SoundPath(key));
            customSounds[key] = false;
        }

        /// <summary>
        /// Checks if a custom sound file exists for the specified key.
        /// Caches the result in the customSounds dictionary for future lookups.
        /// </summary>
        /// <param name="key">The identifier for the sound to check</param>
        /// <returns>True if a custom sound exists, false otherwise</returns>
        public static bool IsCustom(string key)
        {
            if (!customSounds.ContainsKey(key))
            {
                customSounds[key] = File.Exists(SoundPath(key));
            }
            return customSounds[key];
        }

        /// <summary>
        /// Asynchronously plays a sound file identified by the provided key.
        /// First attempts to play a custom sound if it exists, falls back to built-in resources if not.
        /// Uses the configured audio playback device for output.
        /// </summary>
        /// <param name="key">The identifier for the sound to play</param>
        /// <returns>Task representing the asynchronous operation</returns>
        /// <exception cref="Exception">Thrown when the sound resource cannot be loaded</exception>
        public static async Task Play(string key)
        {
            await Task.Run(() =>
            {
                byte[]? resource = null;
                if (IsCustom(key))
                {
                    resource = File.ReadAllBytes(SoundPath(key));
                }
                resource ??= Properties.Resources.ResourceManager.GetObject(key) as byte[];
                if (resource == null)
                {
                    throw new Exception($"Could not load resource for {key}");
                }
                using Stream stream = new MemoryStream(resource);
                using var reader = new Mp3FileReader(stream);
                using var waveOut = new WaveOut();
                waveOut.DeviceNumber = Properties.Settings.Default.notificationsDevice;
                waveOut.Init(reader);
                waveOut.Play();
                while (waveOut.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(100);
                }
            });
        }

        /// <summary>
        /// Retrieves a dictionary of available audio playback devices on the system.
        /// Includes a "Default Device" option with device number -1.
        /// </summary>
        /// <returns>Dictionary mapping device numbers to device names</returns>
        public static Dictionary<int, string> GetPlaybackDevices()
        {
            Dictionary<int, string> devices = new()
            {
                { -1, "Default Device" }
            };
            for (var deviceNum = 0; deviceNum < WaveOut.DeviceCount; deviceNum++)
            {
                WaveOutCapabilities deviceInfo = WaveOut.GetCapabilities(deviceNum);
                devices.Add(deviceNum, deviceInfo.ProductName);
            }
            return devices;
        }

        /// <summary>
        /// Enumeration of available sound types in the application.
        /// Each value represents a specific notification sound that can be played.
        /// </summary>
        public enum SoundType
        {
            /// <summary>Air filter deactivation notification</summary>
            air_filter_off,
            /// <summary>Air filter activation notification</summary>
            air_filter_on,
            /// <summary>Match found notification</summary>
            match_found,
            /// <summary>Raid start notification</summary>
            raid_starting,
            /// <summary>Failed tasks restart notification</summary>
            restart_failed_tasks,
            /// <summary>Run-through completion notification</summary>
            runthrough_over,
            /// <summary>Scav availability notification</summary>
            scav_available,
            /// <summary>Quest items notification</summary>
            quest_items,
        }
    }
}
