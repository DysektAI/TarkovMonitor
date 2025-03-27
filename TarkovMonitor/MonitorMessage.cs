using MudBlazor;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Timers;
using MudColor = MudBlazor.Color;

namespace TarkovMonitor
{
    /// <summary>
    /// Represents a message in the Tarkov Monitor system that can contain text, buttons, and select dropdowns.
    /// This class is designed to be used with MudBlazor UI components to display interactive messages to users.
    /// </summary>
    public class MonitorMessage
    {
        /// <summary>
        /// The main text content of the message to be displayed
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Timestamp when the message was created
        /// </summary>
        public DateTime Time { get; set; } = DateTime.Now;

        /// <summary>
        /// Type/category of the message (e.g., "exception", "info", etc.)
        /// Used for message categorization and special handling
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Associated URL with the message, if any
        /// Can be used for linking to external resources or documentation
        /// </summary>
        public string Url { get; set; } = "";

        /// <summary>
        /// Action to be executed when the message itself is clicked
        /// </summary>
        public Action? OnClick { get; set; } = null;

        /// <summary>
        /// Collection of interactive buttons associated with this message
        /// Supports dynamic addition and removal of buttons
        /// </summary>
        public ObservableCollection<MonitorMessageButton> Buttons { get; set; } = new();

        /// <summary>
        /// Collection of dropdown select controls associated with this message
        /// Allows for user input through dropdown selections
        /// </summary>
        public ObservableCollection<MonitorMessageSelect> Selects { get; set; } = new();

        /// <summary>
        /// Creates a new MonitorMessage with basic message text.
        /// Sets up event handlers for button collection changes to manage button expiration events.
        /// </summary>
        /// <param name="message">The text content to display in the message</param>
        public MonitorMessage(string message)
        {
            Message = message;
            // Set up collection change handling for buttons to manage their expiration events
            Buttons.CollectionChanged += (object? sender, NotifyCollectionChangedEventArgs e) =>
            {
                // When new buttons are added, subscribe to their expiration events
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    if (e.NewItems == null)
                    {
                        return;
                    }
                    foreach (MonitorMessageButton button in e.NewItems.Cast<MonitorMessageButton>().ToList())
                    {
                        button.Expired += ButtonExpired;
                    }
                }
                // When buttons are removed, unsubscribe from their expiration events
                if (e.Action == NotifyCollectionChangedAction.Remove)
                {
                    if (e.OldItems == null)
                    {
                        return;
                    }
                    foreach (MonitorMessageButton button in e.OldItems.Cast<MonitorMessageButton>().ToList())
                    {
                        button.Expired -= ButtonExpired;
                    }
                }
            };
        }

        /// <summary>
        /// Creates a new MonitorMessage with additional type and URL information.
        /// Provides special handling for exception-type messages by adding default action buttons.
        /// </summary>
        /// <param name="message">The text content to display</param>
        /// <param name="type">Message type/category (optional)</param>
        /// <param name="url">Associated URL (optional)</param>
        public MonitorMessage(string message, string? type = "", string? url = "") : this(message)
        {
            Type = type ?? "";
            Url = url ?? "";

            // Special handling for exception messages - adds copy and report buttons
            if (Type == "exception")
            {
                // Add a button to copy the exception message
                Buttons.Add(new("Copy", () =>
                {
                    Clipboard.SetText(Message);
                }, Icons.Material.Filled.CopyAll));

                // Add a button to report the issue on GitHub
                Buttons.Add(new("Report", () =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "https://github.com/the-hideout/TarkovMonitor/issues",
                        UseShellExecute = true,
                    };
                    Process.Start(psi);
                }, Icons.Material.Filled.BugReport));
            }
        }

        /// <summary>
        /// Event handler for button expiration.
        /// Removes expired buttons from the Buttons collection to clean up the UI.
        /// </summary>
        /// <param name="sender">The button that expired</param>
        /// <param name="e">Event arguments</param>
        private void ButtonExpired(object? sender, EventArgs e)
        {
            if (sender == null)
            {
                return;
            }
            Buttons.Remove((MonitorMessageButton)sender);
        }
    }

    /// <summary>
    /// Represents an interactive button that can be added to a MonitorMessage.
    /// Supports icons, click actions, confirmation dialogs, and automatic expiration.
    /// </summary>
    public class MonitorMessageButton
    {
        /// <summary>
        /// The text displayed on the button
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Material icon identifier for the button
        /// </summary>
        public string Icon { get; set; } = "";

        /// <summary>
        /// The MudBlazor color theme for the button
        /// </summary>
        public MudColor Color { get; set; } = MudColor.Default;

        /// <summary>
        /// Action to execute when the button is clicked
        /// </summary>
        public Action? OnClick { get; set; }

        /// <summary>
        /// Indicates if the button is currently disabled
        /// </summary>
        public bool Disabled { get; set; } = false;

        /// <summary>
        /// Optional confirmation dialog settings for the button
        /// </summary>
        public MonitorMessageButtonConfirm? Confirm { get; set; }

        private System.Timers.Timer? buttonTimer;
        private double? timeout = null;

        /// <summary>
        /// Gets or sets the timeout duration for the button.
        /// When set, starts a timer that will trigger button expiration after the specified duration.
        /// </summary>
        public double? Timeout
        {
            get
            {
                return timeout;
            }
            set
            {
                timeout = value;
                if (buttonTimer != null)
                {
                    buttonTimer.Stop();
                    buttonTimer.Dispose();
                }
                if (value == null || value == 0)
                {
                    buttonTimer = null;
                }
                else
                {
                    buttonTimer = new(timeout ?? 0)
                    {
                        AutoReset = true,
                        Enabled = true,
                    };
                    buttonTimer.Elapsed += (object? sender, ElapsedEventArgs e) =>
                    {
                        Expired?.Invoke(this, e);
                    };
                }
            }
        }

        /// <summary>
        /// Event triggered when the button expires (either due to timeout or manual expiration)
        /// </summary>
        public event EventHandler? Expired;

        /// <summary>
        /// Creates a new button with specified text, click action, and icon
        /// </summary>
        public MonitorMessageButton(string text, Action? onClick = null, string icon = "")
        {
            Text = text;
            Icon = icon;
            OnClick = onClick;
        }

        /// <summary>
        /// Creates a new button with only text and icon (no click action)
        /// </summary>
        public MonitorMessageButton(string text, string icon = "") : this(text, null, icon) { }

        /// <summary>
        /// Manually triggers button expiration
        /// </summary>
        public void Expire()
        {
            buttonTimer?.Stop();
            Expired?.Invoke(this, new());
        }
    }

    /// <summary>
    /// Represents confirmation dialog settings for a button
    /// Used to show a confirmation prompt before executing the button's action
    /// </summary>
    public class MonitorMessageButtonConfirm
    {
        /// <summary>
        /// Title of the confirmation dialog
        /// </summary>
        public string Title { get; set; } = "Confirm";

        /// <summary>
        /// Message displayed in the confirmation dialog
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Text for the confirmation/accept button
        /// </summary>
        public string YesText { get; set; }

        /// <summary>
        /// Text for the cancel button
        /// </summary>
        public string CancelText { get; set; } = "Cancel";

        public MonitorMessageButtonConfirm(string title, string message, string yesText, string cancelText)
        {
            Title = title;
            Message = message;
            YesText = yesText;
            CancelText = cancelText;
        }
    }

    /// <summary>
    /// Represents a dropdown select control that can be added to a MonitorMessage
    /// Allows users to choose from a list of options
    /// </summary>
    public class MonitorMessageSelect
    {
        /// <summary>
        /// Available options in the dropdown
        /// </summary>
        public List<MonitorMessageSelectOption> Options { get; set; } = new();

        /// <summary>
        /// Event triggered when the selected option changes
        /// </summary>
        public event EventHandler<MonitorMessageSelectChangedEventArgs>? SelectionChanged;

        /// <summary>
        /// Currently selected option
        /// </summary>
        public MonitorMessageSelectOption? Selected { get; private set; }

        /// <summary>
        /// Placeholder text shown when no option is selected
        /// </summary>
        public string Placeholder { get; set; } = "";

        /// <summary>
        /// Updates the selected option and triggers the SelectionChanged event
        /// </summary>
        public void ChangeSelection(MonitorMessageSelectOption selected)
        {
            Selected = selected;
            SelectionChanged?.Invoke(this, new MonitorMessageSelectChangedEventArgs() { Selected = selected });
        }
    }

    /// <summary>
    /// Represents an option in a MonitorMessageSelect dropdown
    /// </summary>
    public class MonitorMessageSelectOption
    {
        /// <summary>
        /// Display text for the option
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Value associated with the option
        /// </summary>
        public string Value { get; set; }

        public override string ToString()
        {
            return Text;
        }

        public MonitorMessageSelectOption(string text, string value)
        {
            Text = text;
            Value = value;
        }
    }

    /// <summary>
    /// Event arguments for the MonitorMessageSelect.SelectionChanged event
    /// Contains information about the newly selected option
    /// </summary>
    public class MonitorMessageSelectChangedEventArgs : EventArgs
    {
        public MonitorMessageSelectOption Selected { get; set; } = null!;
    }
}