// This namespace contains all the core functionality of the TarkovMonitor application
namespace TarkovMonitor
{
    /// <summary>
    /// The Program class serves as the main entry point for the TarkovMonitor application.
    /// This class is marked as internal to prevent access from outside the assembly and static
    /// since it only contains static members.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the Windows Forms application.
        /// This method initializes the application configuration and handles the startup sequence.
        /// </summary>
        /// <remarks>
        /// The [STAThread] attribute is required for Windows Forms applications.
        /// It ensures the application uses the STA (Single-Threaded Apartment) model,
        /// which is necessary for COM compatibility and proper UI behavior.
        /// </remarks>
        [STAThread]
        static void Main()
        {
            // Initialize the application configuration including high DPI settings and default font
            // This is a Windows Forms specific configuration step that should be called before
            // creating any windows or controls
            ApplicationConfiguration.Initialize();

            // Define the duration (in milliseconds) for how long the splash screen should be displayed
            var splashTime = 2000; // Default to 2 seconds

            // Check user preferences from application settings
            // If either skipSplash or minimizeAtStartup is enabled in the user settings,
            // reduce the splash screen duration to 1ms (effectively skipping it)
            if (Properties.Settings.Default.skipSplash || Properties.Settings.Default.minimizeAtStartup)
            {
                splashTime = 1;
            }

            // Launch sequence:
            // 1. Show the splash screen with the tarkov.dev logo for the specified duration
            // 2. After splash screen closes, launch the main Blazor UI
            Application.Run(new Splash(TarkovMonitor.Properties.Resources.tarkov_dev_logo, splashTime));
            Application.Run(new MainBlazorUI());
        }
    }
}