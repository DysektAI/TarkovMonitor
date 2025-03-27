namespace TarkovMonitor
{
    // An Event Delegate and Arguments for when a new event is added to the MessageLog
    public delegate void NewLogMessage(object source, NewLogMessageArgs e);

    public class NewLogMessageArgs : EventArgs
    {
        public MonitorMessage Message { get; set; }
        /// <summary>
        /// Event Args for the NewLogMessage event
        /// </summary>
        /// <param name="Message">The MonitorMessage that was added to the MessageLog</param>
        public NewLogMessageArgs(MonitorMessage message)
        {
            Message = message;
        }
    }

    /// <summary>
    /// Interface for the message log service
    /// </summary>
    public interface IMessageLog
    {
        event NewLogMessage NewMessage;
        List<MonitorMessage> Messages { get; }
        void AddMessage(MonitorMessage message);
        void AddMessage(string message, string? type = "", string? url = null);
    }

    /// <summary>
    /// A simple class to hold a list of messages.
    /// </summary>
    internal class MessageLog : IMessageLog
    {
        public event NewLogMessage NewMessage = delegate { };

        public MessageLog()
        {
            Messages = new List<MonitorMessage>();
        }
        public List<MonitorMessage> Messages { get; set; }

        /// <summary>
        /// Adds a new message to the MessageLog. This method is thread-safe and will notify any listeners of the change.
        /// </summary>
        /// <param name="message">The MonitorMessage to add to the MessageLog</param>
        public void AddMessage(MonitorMessage message)
        {
            Messages.Add(message);
            // Throw event to let watchers know something has changed
            NewMessage(this, new NewLogMessageArgs(message));
        }

        /// <summary>
        /// Adds a new message to the MessageLog. This method is thread-safe and will notify any listeners of the change.
        /// </summary>
        /// <param name="message">The string message to add to the MessageLog</param>
        /// <param name="type">Optional: The type of the message (e.g. "exception", "warning", etc.). If not provided, the type will be an empty string.</param>
        /// <param name="url">Optional: The URL of the message. If not provided, the URL will be null.</param>
        public void AddMessage(string message, string? type = "", string? url = null)
        {
            var monMessage = new MonitorMessage(message, type, url);
            Messages.Add(monMessage);
            // Throw event to let watchers know something has changed
            NewMessage(this, new NewLogMessageArgs(monMessage));
        }
    }
}
