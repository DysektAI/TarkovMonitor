// This namespace contains classes related to the Tarkov Monitor application
namespace TarkovMonitor
{
    /// <summary>
    /// Represents a single log entry in the monitoring system.
    /// This class encapsulates all the information needed for a log message,
    /// including the message content, timestamp, and message type.
    /// </summary>
    public class LogLine
    {
        /// <summary>
        /// Gets or sets the actual content/text of the log message.
        /// This contains the main information that needs to be logged.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when this log entry was created.
        /// This is automatically set to the current time when a new LogLine is instantiated.
        /// </summary>
        public DateTime Time { get; set; }

        /// <summary>
        /// Gets or sets the type/category of the log message.
        /// This can be used to categorize logs (e.g., "ERROR", "INFO", "WARNING", etc.).
        /// If not specified, it defaults to an empty string.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Creates a new instance of a LogLine with the specified message and optional type.
        /// </summary>
        /// <param name="message">The log message content that needs to be stored.</param>
        /// <param name="type">Optional parameter specifying the type/category of the log message. 
        /// If not provided (null), it defaults to an empty string.</param>
        /// <remarks>
        /// The timestamp (Time property) is automatically set to the current time
        /// when this constructor is called.
        /// </remarks>
        public LogLine(string message, string? type = null)
        {
            Message = message;
            Time = DateTime.Now;
            // If no type is specified, use an empty string rather than null
            // This ensures the Type property is never null
            if (type == null)
            {
                Type = "";
            }
            else
            {
                Type = type;
            }
        }
    }
}