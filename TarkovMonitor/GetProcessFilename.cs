using System.Runtime.InteropServices;
using System.Text;

namespace TarkovMonitor
{
    /// <summary>
    /// A utility class that provides functionality to retrieve the full file path of a running process.
    /// This class uses Windows API calls through P/Invoke to access process information.
    /// </summary>
    internal class GetProcessFilename
    {
        /// <summary>
        /// Defines the access rights needed when opening a process.
        /// QueryLimitedInformation (0x00001000) allows access to limited process information
        /// while maintaining security and avoiding full process access.
        /// </summary>
        [Flags]
        private enum ProcessAccessFlags : uint
        {
            QueryLimitedInformation = 0x00001000
        }

        /// <summary>
        /// P/Invoke declaration for QueryFullProcessImageName Windows API function.
        /// This function retrieves the full path of the executable file for a specified process.
        /// </summary>
        /// <param name="hProcess">Handle to the process</param>
        /// <param name="dwFlags">Additional flags (0 for win32 path)</param>
        /// <param name="lpExeName">Buffer to receive the path</param>
        /// <param name="lpdwSize">Size of the buffer in characters (input/output parameter)</param>
        /// <returns>True if successful, false otherwise</returns>
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(
              [In] IntPtr hProcess,
              [In] int dwFlags,
              [Out] StringBuilder lpExeName,
              [In, Out] ref int lpdwSize);

        /// <summary>
        /// P/Invoke declaration for OpenProcess Windows API function.
        /// Opens an existing local process object with specified access rights.
        /// </summary>
        /// <param name="processAccess">Desired access rights</param>
        /// <param name="bInheritHandle">If true, processes created by this process inherit the handle</param>
        /// <param name="processId">Process ID of the target process</param>
        /// <returns>Handle to the process if successful, or IntPtr.Zero if failed</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
         ProcessAccessFlags processAccess,
         bool bInheritHandle,
         int processId);

        /// <summary>
        /// Gets the full path and filename of a process.
        /// This method combines OpenProcess and QueryFullProcessImageName to safely retrieve
        /// the complete file path of a running process.
        /// </summary>
        /// <param name="p">The process to query. Must be a valid running process.</param>
        /// <returns>
        /// The full path and filename of the process executable.
        /// Returns an empty string if the operation fails (e.g., insufficient permissions or invalid process).
        /// </returns>
        /// <remarks>
        /// The method uses a 2000-character buffer which should be sufficient for most Windows paths.
        /// It uses QueryLimitedInformation to maintain security while accessing process information.
        /// </remarks>
        public static string GetFilename(System.Diagnostics.Process p)
        {
            // Initialize a StringBuilder with sufficient capacity for most Windows paths
            int capacity = 2000;
            StringBuilder builder = new(capacity);

            // Open the process with minimal required access rights
            IntPtr ptr = OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, p.Id);

            // Attempt to query the process's full image name
            if (!QueryFullProcessImageName(ptr, 0, builder, ref capacity))
            {
                return string.Empty;
            }

            // Return the full path if successful
            return builder.ToString();
        }
    }
}
