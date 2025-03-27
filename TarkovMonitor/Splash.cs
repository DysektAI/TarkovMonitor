using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing.Imaging;

namespace TarkovMonitor
{
    /// <summary>
    /// A custom splash screen implementation that supports transparency and smooth fade effects.
    /// This class creates a layered window that can display a bitmap with alpha channel support.
    /// Based on the implementation from https://stackoverflow.com/a/61332356
    /// </summary>
    class Splash : Form
    {
        // Controls the opacity of the splash screen (0.0 to 1.0)
        private float _opacity = 1.0f;

        // The bitmap to be displayed in the splash screen
        public Bitmap? BackgroundBitmap;

        /// <summary>
        /// Registry keys to check for WebView2 Runtime installation.
        /// These keys are checked in order of preference (machine-wide 64-bit, machine-wide 32-bit, per-user)
        /// </summary>
        private readonly string[] _webview2RegKeys =
        {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
            @"HKEY_CURRENT_USER\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
        };

        // Timer to control how long the splash screen is displayed
        private readonly System.Timers.Timer splashTimer = new();

        /// <summary>
        /// Property to control the opacity of the splash screen.
        /// When set, it triggers a redraw of the window with the new opacity value.
        /// </summary>
        public new float Opacity
        {
            get => _opacity;
            set
            {
                _opacity = value;
                // Only update the bitmap if it exists
                BackgroundBitmap?.Let(SelectBitmap);
            }
        }

        /// <summary>
        /// Constructor for the splash screen.
        /// </summary>
        /// <param name="bitmap">The image to display in the splash screen</param>
        /// <param name="splashTime">Duration in milliseconds to show the splash screen</param>
        public Splash(Bitmap bitmap, int splashTime)
        {
            // Configure window properties for a splash screen
            TopMost = true;                    // Keep window on top
            ShowInTaskbar = false;             // Hide from taskbar
            Size = bitmap.Size;                // Match window size to bitmap
            StartPosition = FormStartPosition.Manual;

            // Center the splash screen on the primary monitor
            var screen = Screen.AllScreens[0];
            Top = (screen.Bounds.Height - Height) / 2;
            Left = (screen.Bounds.Width - Width) / 2;

            // Only set up the bitmap if we're showing the splash screen
            // (splashTime > 1 indicates normal splash screen, 1ms means skip)
            if (splashTime > 1)
            {
                // Must set bitmap before calling SelectBitmap
                BackgroundBitmap = bitmap;
                SelectBitmap(BackgroundBitmap);
            }
            BackColor = Color.Red;  // Set a default background color

            // Ensure working directory is set to the application's base directory
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            // Check if WebView2 Runtime is installed by checking registry keys
            var existing = _webview2RegKeys.Any(key => Registry.GetValue(key, "pv", null) != null);
            if (!existing)
            {
                // Install WebView2 Runtime if not present
                Task.Run(InstallWebview2Runtime).Wait();
            }

            // Set up the timer to close the splash screen
            splashTimer = new(splashTime)
            {
                AutoReset = false,  // Only trigger once
                Enabled = true      // Start the timer immediately
            };
            // When the timer elapses, close the splash screen on the UI thread
            splashTimer.Elapsed += (_, _) => Invoke(() => Close());
        }

        /// <summary>
        /// Downloads and installs the Microsoft Edge WebView2 Runtime.
        /// This component is required for the application's web-based UI components.
        /// </summary>
        private async Task InstallWebview2Runtime()
        {
            // Download the WebView2 Runtime bootstrapper
            using var client = new HttpClient();
            using var response = await client.GetAsync("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            await using var fs = new FileStream("MicrosoftEdgeWebview2Setup.exe", FileMode.Create);
            await response.Content.CopyToAsync(fs);

            // Configure the process to run the installer silently
            var startInfo = new ProcessStartInfo
            (
                "MicrosoftEdgeWebview2Setup.exe"
            )
            {
                CreateNoWindow = false,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = "/install"
            };

            try
            {
                // Run the installer and wait for it to complete
                using var exeProcess = Process.Start(startInfo);
                exeProcess?.WaitForExit();
            }
            catch (Exception)
            {
                Debug.WriteLine("Could not install WebView");
            }

            try
            {
                // Clean up the installer file
                File.Delete("MicrosoftEdgeWebview2Setup.exe");
            }
            catch { }
        }

        /// <summary>
        /// Updates the window with a new bitmap, handling all necessary Win32 API calls
        /// to properly display the layered window with transparency.
        /// </summary>
        /// <param name="bitmap">The bitmap to display (must be 32bpp with alpha channel)</param>
        public void SelectBitmap(Bitmap bitmap)
        {
            // Verify the bitmap has an alpha channel
            if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
            {
                throw new ApplicationException("The bitmap must be 32bpp with alpha-channel.");
            }

            // Get device contexts for screen and memory operations
            var screenDc = SplashAPIHelp.GetDC(IntPtr.Zero);
            var memDc = SplashAPIHelp.CreateCompatibleDC(screenDc);
            var hBitmap = IntPtr.Zero;
            var hOldBitmap = IntPtr.Zero;

            try
            {
                // Create and select the bitmap into the memory DC
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                hOldBitmap = SplashAPIHelp.SelectObject(memDc, hBitmap);

                // Configure the layered window update parameters
                var newSize = new SplashAPIHelp.Size(bitmap.Width, bitmap.Height);
                var sourceLocation = new SplashAPIHelp.Point(0, 0);
                var newLocation = new SplashAPIHelp.Point(Left, Top);

                // Set up alpha blending parameters
                var blend = new SplashAPIHelp.BLENDFUNCTION
                {
                    BlendOp = SplashAPIHelp.AC_SRC_OVER,          // Alpha blending
                    BlendFlags = 0,                                // Must be 0
                    SourceConstantAlpha = (byte)(Opacity * 255),   // Overall opacity
                    AlphaFormat = SplashAPIHelp.AC_SRC_ALPHA      // Use source alpha channel
                };

                // Update the layered window with the new bitmap and blend settings
                SplashAPIHelp.UpdateLayeredWindow(
                    Handle, screenDc, ref newLocation, ref newSize,
                    memDc, ref sourceLocation, 0, ref blend, SplashAPIHelp.ULW_ALPHA);
            }
            finally
            {
                // Clean up all GDI resources
                SplashAPIHelp.ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    SplashAPIHelp.SelectObject(memDc, hOldBitmap);
                    SplashAPIHelp.DeleteObject(hBitmap);
                }
                SplashAPIHelp.DeleteDC(memDc);
            }
        }

        /// <summary>
        /// Overrides the window creation parameters to add the layered window style.
        /// This is required for windows that support transparency.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var createParams = base.CreateParams;
                createParams.ExStyle |= SplashAPIHelp.WS_EX_LAYERED;  // Add layered window style
                return createParams;
            }
        }

        /// <summary>
        /// Handles window messages to make the entire window draggable.
        /// This makes the splash screen moveable by clicking and dragging anywhere on it.
        /// </summary>
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == SplashAPIHelp.WM_NCHITTEST)
            {
                // Tell Windows that any point on the window is treated as the title bar
                message.Result = (IntPtr)SplashAPIHelp.HTCAPTION;
            }
            else
            {
                base.WndProc(ref message);
            }
        }
    }

    /// <summary>
    /// Extension methods to provide null-safe operations.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Executes an action on an object only if it is not null.
        /// This helps reduce null-checking boilerplate code.
        /// </summary>
        /// <typeparam name="T">The type of the object</typeparam>
        /// <param name="obj">The object to check</param>
        /// <param name="action">The action to perform if the object is not null</param>
        public static void Let<T>(this T? obj, Action<T> action) where T : class
        {
            if (obj != null) action(obj);
        }
    }
}