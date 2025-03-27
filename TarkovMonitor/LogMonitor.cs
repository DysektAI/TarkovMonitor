using System.Text;

namespace TarkovMonitor
{
    /// <summary>
    /// Monitors a log file for changes and raises events when new data is available.
    /// This class implements a file-watching mechanism that continuously checks for
    /// new content added to specified log files in the Escape from Tarkov game.
    /// </summary>
    internal class LogMonitor
    {
        /// <summary>
        /// The full path to the log file being monitored
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// The type of game log being monitored (e.g., Application, Notifications, Traces)
        /// </summary>
        public GameLogType Type { get; set; }

        /// <summary>
        /// Event raised when the initial read of the log file is complete
        /// </summary>
        public event EventHandler? InitialReadComplete;

        /// <summary>
        /// Event raised when new log data is detected and read from the file
        /// </summary>
        public event EventHandler<NewLogDataEventArgs>? NewLogData;

        /// <summary>
        /// Event raised when an exception occurs during log monitoring
        /// </summary>
        public event EventHandler<ExceptionEventArgs>? Exception;

        /// <summary>
        /// Flag to control the monitoring loop - when set to true, monitoring will stop
        /// </summary>
        private bool cancel;

        /// <summary>
        /// Maximum number of bytes to read in a single chunk when processing the log file
        /// </summary>
        private readonly int MaxBufferLength = 1024;

        /// <summary>
        /// Initializes a new instance of the LogMonitor class
        /// </summary>
        /// <param name="path">The full path to the log file to monitor</param>
        /// <param name="logType">The type of game log being monitored</param>
        public LogMonitor(string path, GameLogType logType)
        {
            Path = path;
            Type = logType;
            cancel = false;
        }

        /// <summary>
        /// Starts monitoring the log file for changes asynchronously.
        /// This method runs continuously until Stop() is called, checking for new content
        /// every 5 seconds and raising events when new data is found.
        /// </summary>
        /// <returns>A Task representing the asynchronous operation</returns>
        public async Task Start()
        {
            await Task.Run(async () =>
            {
                // Tracks how many bytes we've read from the file so far
                long fileBytesRead = 0;

                // For non-Application logs, we start reading from the end of the current file
                if (Type != GameLogType.Application)
                {
                    try
                    {
                        // Get the current file size to start monitoring from this point
                        fileBytesRead = new FileInfo(this.Path).Length;
                        InitialReadComplete?.Invoke(this, new());
                    }
                    catch (Exception ex)
                    {
                        // If we can't get the initial file size, raise an exception event and retry
                        Exception?.Invoke(this, new(ex, $"getting initial {this.Type} log data size"));
                        Thread.Sleep(5000);
                        await Start();
                        return;
                    }
                }

                // Main monitoring loop
                while (true)
                {
                    if (cancel) break;
                    try
                    {
                        // Check if the file has grown
                        var fileSize = new FileInfo(this.Path).Length;
                        if (fileSize > fileBytesRead)
                        {
                            // Open the file in a way that doesn't lock it from other processes
                            using var fs = new FileStream(this.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            // Seek to where we last stopped reading
                            fs.Seek(fileBytesRead, SeekOrigin.Begin);

                            var buffer = new byte[MaxBufferLength];
                            var chunks = new List<string>();
                            var bytesRead = fs.Read(buffer, 0, buffer.Length);
                            var newBytesRead = 0;

                            // Read the new content in chunks
                            while (bytesRead > 0)
                            {
                                newBytesRead += bytesRead;
                                var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                                chunks.Add(text);
                                bytesRead = fs.Read(buffer, 0, buffer.Length);
                            }

                            // Raise event with the new log data
                            NewLogData?.Invoke(this, new NewLogDataEventArgs
                            {
                                Type = this.Type,
                                Data = string.Join("", chunks.ToArray()),
                                InitialRead = fileBytesRead == 0
                            });

                            // If this was our first read, raise the InitialReadComplete event
                            if (fileBytesRead == 0)
                            {
                                InitialReadComplete?.Invoke(this, new());
                            }

                            // Update our position in the file
                            fileBytesRead += newBytesRead;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Raise an exception event if anything goes wrong during monitoring
                        Exception?.Invoke(this, new(ex, $"reading {this.Type} log data"));
                    }
                    // Wait before checking for new content again
                    Thread.Sleep(5000);
                }
            });
        }

        /// <summary>
        /// Stops monitoring for new log data by setting the cancel flag.
        /// The monitoring loop will complete its current iteration and then exit.
        /// </summary>
        public void Stop()
        {
            cancel = true;
        }
    }

    /// <summary>
    /// Event arguments class for the NewLogData event, containing information about new log entries
    /// </summary>
    public class NewLogDataEventArgs : EventArgs
    {
        /// <summary>
        /// The type of log data (Application, Notifications, Traces)
        /// </summary>
        public GameLogType Type { get; set; }

        /// <summary>
        /// The actual content/text of the new log data
        /// </summary>
        public string Data { get; set; }

        /// <summary>
        /// Indicates whether this data comes from the first read when the monitor starts
        /// </summary>
        public bool InitialRead { get; set; }

        /// <summary>
        /// Initializes a new instance of NewLogDataEventArgs with an empty Data string
        /// </summary>
        public NewLogDataEventArgs()
        {
            Data = string.Empty;
        }
    }
}
