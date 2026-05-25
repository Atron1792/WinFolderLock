using System.IO;
using System.Text.Json;
using System.Windows;

namespace WinFolderLock
{
    public enum WindowMode
    {
        LockFolder,
        UnlockFolder,
        PermanentlyUnlockFolder
    }

    internal static class Helper
    {
        public static void ExceptionHandler(Exception ex)
        {
            string message = ex switch
            {
                ArgumentException or IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException => ex.Message,
                _ => "An unexpected error occurred."
            };

            MessageBox.Show(message, "WinFolderLock", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public static void LogError(Exception ex)
        {
            // Silent logging for non-critical errors (cleanup operations, ACL updates, etc.)
            // In the future, this could write to a log file or event viewer
            System.Diagnostics.Debug.WriteLine($"[WinFolderLock] {ex.GetType().Name}: {ex.Message}");
        }
    }
}
