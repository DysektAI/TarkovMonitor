using System.Runtime.InteropServices;

namespace TarkovMonitor
{
    /// <summary>
    /// Helper class that provides P/Invoke declarations and structures for Windows API calls
    /// used in creating and managing layered windows with alpha transparency.
    /// Based on the implementation from https://stackoverflow.com/a/61332356
    /// </summary>
    /// <remarks>
    /// This class contains the necessary Win32 API declarations for:
    /// - Creating and updating layered windows
    /// - Managing GDI device contexts and objects
    /// - Handling window messages for custom window behavior
    /// </remarks>
    unsafe partial class SplashAPIHelp
    {
        /// <summary>Extended window style for layered windows (WS_EX_LAYERED)</summary>
        /// <remarks>
        /// Layered windows are used to create windows with transparency effects.
        /// This style must be set for UpdateLayeredWindow to work.
        /// </remarks>
        public const int WS_EX_LAYERED = 0x80000;

        /// <summary>Hit-test value for the window caption area</summary>
        /// <remarks>
        /// Used in WM_NCHITTEST message handling to indicate the mouse
        /// is over what Windows should treat as the title bar area.
        /// </remarks>
        public const int HTCAPTION = 0x02;

        /// <summary>Windows message for hit-testing</summary>
        /// <remarks>
        /// Sent by Windows to determine what part of the window the mouse is over.
        /// Used for implementing custom window dragging behavior.
        /// </remarks>
        public const int WM_NCHITTEST = 0x84;

        /// <summary>Flag for alpha-blended layered windows</summary>
        /// <remarks>
        /// Used with UpdateLayeredWindow to indicate that the window
        /// should use alpha channel information for transparency.
        /// </remarks>
        public const int ULW_ALPHA = 0x02;

        /// <summary>Alpha blending operation flag for source-over composition</summary>
        /// <remarks>
        /// Specifies that the source bitmap should be blended over the destination
        /// using the alpha channel values.
        /// </remarks>
        public const byte AC_SRC_OVER = 0x00;

        /// <summary>Flag indicating the source bitmap has an alpha channel</summary>
        /// <remarks>
        /// When set in BLENDFUNCTION.AlphaFormat, indicates that the source bitmap
        /// contains pre-multiplied alpha channel data.
        /// </remarks>
        public const byte AC_SRC_ALPHA = 0x01;

        /// <summary>
        /// Boolean enumeration for Win32 API functions that return BOOL
        /// </summary>
        public enum Bool
        {
            False = 0,
            True = 1
        }

        /// <summary>
        /// Structure representing a point in screen coordinates (x, y)
        /// </summary>
        /// <remarks>
        /// Used by UpdateLayeredWindow to specify source and destination positions
        /// Maps to the Windows POINT structure
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int x;
            public int y;

            public Point(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }

        /// <summary>
        /// Structure representing a size with width (cx) and height (cy)
        /// </summary>
        /// <remarks>
        /// Used by UpdateLayeredWindow to specify the dimensions of the layered window
        /// Maps to the Windows SIZE structure
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        public struct Size
        {
            public int cx;
            public int cy;

            public Size(int cx, int cy)
            {
                this.cx = cx;
                this.cy = cy;
            }
        }

        /// <summary>
        /// Structure representing a 32-bit ARGB color value
        /// </summary>
        /// <remarks>
        /// Used internally for color manipulation
        /// The byte ordering matches the Windows GDI+ color structure
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct ARGB
        {
            public byte Blue;
            public byte Green;
            public byte Red;
            public byte Alpha;
        }

        /// <summary>
        /// Structure that controls alpha blending parameters
        /// </summary>
        /// <remarks>
        /// Used by UpdateLayeredWindow to specify how the source bitmap
        /// should be blended with the destination
        /// Maps to the Windows BLENDFUNCTION structure
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;           // Must be AC_SRC_OVER
            public byte BlendFlags;        // Must be 0
            public byte SourceConstantAlpha; // Overall opacity (0-255)
            public byte AlphaFormat;       // AC_SRC_ALPHA for per-pixel alpha
        }

        /// <summary>
        /// Updates the content and position of a layered window
        /// </summary>
        /// <remarks>
        /// This is the main function used to display and update the transparent window.
        /// It combines the source bitmap with the destination using alpha blending.
        /// </remarks>
        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern Bool UpdateLayeredWindow(
            IntPtr hwnd,                   // Window handle
            IntPtr hdcDst,                // Screen DC
            ref Point pptDst,             // Window position
            ref Size psize,               // Window size
            IntPtr hdcSrc,                // Source DC
            ref Point pprSrc,             // Source position
            int crKey,                  // Color key (unused)
            ref BLENDFUNCTION pblend,     // Blend function
            int dwFlags                 // ULW_ALPHA
        );

        /// <summary>
        /// Creates a memory device context compatible with the specified device
        /// </summary>
        /// <remarks>
        /// Used to create a DC for off-screen rendering of the window content
        /// </remarks>
        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        /// <summary>
        /// Retrieves a handle to a device context for the specified window
        /// </summary>
        /// <remarks>
        /// Used to get the screen DC for updating the layered window
        /// </remarks>
        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        /// <summary>
        /// Releases a device context handle obtained by GetDC
        /// </summary>
        /// <remarks>
        /// Must be called to free the DC when it's no longer needed
        /// </remarks>
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        /// <summary>
        /// Deletes a device context created by CreateCompatibleDC
        /// </summary>
        /// <remarks>
        /// Must be called to free memory DCs when they're no longer needed
        /// </remarks>
        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern Bool DeleteDC(IntPtr hdc);

        /// <summary>
        /// Selects an object into a device context
        /// </summary>
        /// <remarks>
        /// Used to select bitmaps into memory DCs for rendering
        /// Returns the previously selected object
        /// </remarks>
        [LibraryImport("gdi32.dll", SetLastError = true)]
        public static partial IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        /// <summary>
        /// Deletes a GDI object
        /// </summary>
        /// <remarks>
        /// Used to clean up GDI objects (bitmaps, etc.) when they're no longer needed
        /// </remarks>
        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern Bool DeleteObject(IntPtr hObject);
    }
}
