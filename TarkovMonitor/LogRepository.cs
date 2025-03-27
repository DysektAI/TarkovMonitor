namespace TarkovMonitor
{
    // An Event Delegate and Arguments for when a new event is added to the LogRepository
    public delegate void NewLogLine(object source, NewLogLineArgs e);

    public class NewLogLineArgs : EventArgs
    {
        public string Type { get; }

        /// <summary>
        /// Event Args for the NewLogLine event
        /// </summary>
        /// <param name="Type">The type of the log line (e.g. "Application", "Notifications", "Traces")</param>
        public NewLogLineArgs(string Type)
        {
            this.Type = Type;
        }
    }

    internal class LogRepository
    {
        public event NewLogLine NewLog = delegate { };

        /// <summary>
        /// A repository for all log lines received from the game and other sources.
        /// </summary>
        /// <remarks>
        /// This is a simple repository that stores all log lines and notifies any listeners
        /// of new log lines. It is intended to be used as a single source of truth for all
        /// log lines in the application.
        /// </remarks>
        public LogRepository()
        {
            Logs = new List<LogLine>();
        }
        public List<LogLine> Logs { get; set; }

        /// <summary>
        /// Add a new log line to the repository
        /// </summary>
        /// <param name="message">The log line to add</param>
        /// <remarks>
        /// This method is thread-safe and will notify any listeners of the change.
        /// </remarks>
        public void AddLog(LogLine message)
        {
            Logs.Add(message);

            // Throw event to let watchers know something has changed
            NewLog(this, new NewLogLineArgs(message.Type));
        }

        /// <summary>
        /// Add a new log line to the repository
        /// </summary>
        /// <param name="message">The log line to add</param>
        /// <param name="type">The type of the log line (e.g. "Application", "Notifications", "Traces"). If null, the type will be an empty string.</param>
        /// <remarks>
        /// This method is thread-safe and will notify any listeners of the change.
        /// </remarks>
        public void AddLog(string message, string? type = null)
        {
            Logs.Add(new LogLine(message, type));

            // Throw event to let watchers know something has changed
            type ??= "";
            NewLog(this, new NewLogLineArgs(type));
        }
    }
}
