using System.IO;
using System.Text.Json;
using System.Windows;

namespace WinFolderLock
{
    public partial class App : Application
    {
        private const string WizardInstanceMutexName = @"Global\WinFolderLock.Wizard";
        private static readonly string ProgramDataDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinFolderLock");
        private static readonly string PasswordsFilePath = Path.Combine(ProgramDataDirectoryPath, "passwords.json");

        private static readonly JsonSerializerOptions JsonSerializerOptions = new()
        {
            WriteIndented = true
        };

        private Mutex? _wizardMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool isWizardLaunch = e.Args.Length == 0;
            if (isWizardLaunch)
            {
                _wizardMutex = new Mutex(initiallyOwned: true, WizardInstanceMutexName, out bool isFirstWizardInstance);
                if (!isFirstWizardInstance)
                {
                    _wizardMutex.Dispose();

                    _ = MessageBox.Show("A wizard is already running.", "WinFolderLock", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }

                MainWindow = new WizardWindow();
                MainWindow.Show();
                return;
            }

            // Handle maintenance operations
            if (e.Args.Length > 0)
            {
                if (e.Args[0] == "/install")
                {
                    HandleInstall();
                    Shutdown();
                    return;
                }

                if (e.Args[0] == "/uninstall")
                {
                    HandleUninstall();
                    Shutdown();
                    return;
                }
            }

            // Check for command line switches for different modes
            bool isPermanentUnlock = e.Args.Length > 0 && e.Args[0] == "/permunlock";
            int fileArgIndex = isPermanentUnlock ? 1 : 0;

            if (e.Args.Length <= fileArgIndex)
            {
                Shutdown();
                return;
            }

            string filePath = e.Args[fileArgIndex];

            // Determine the appropriate window mode based on whether it's a file or folder
            WindowMode mode = WindowMode.LockFolder;
            if (FolderLocker.IsLockedFile(filePath))
            {
                mode = isPermanentUnlock ? WindowMode.PermanentlyUnlockFolder : WindowMode.UnlockFolder;
            }

            PasswordInputWindow passwordWindow = new(mode);

            while (true)
            {
                bool isConfirmed = passwordWindow.ShowDialog() == true;

                if (!isConfirmed)
                {
                    Shutdown();
                    return;
                }

                string password = passwordWindow.Password;

                if (string.IsNullOrWhiteSpace(password))
                {
                    _ = MessageBox.Show("Password cannot be empty. Please try again.", "WinFolderLock", MessageBoxButton.OK, MessageBoxImage.Warning);
                    passwordWindow = new(mode);
                    continue;
                }

                // Validate password if unlocking
                if (mode is WindowMode.UnlockFolder or WindowMode.PermanentlyUnlockFolder)
                {
                    List<PasswordEntry> entries = LoadPasswordEntries();
                    FileInfo lockedFileInfo = new(filePath);
                    string folderName = Path.GetFileNameWithoutExtension(filePath);

                    PasswordEntry? entry = entries.FirstOrDefault(x =>
                        x.FolderName.Equals(folderName, StringComparison.OrdinalIgnoreCase));

                    if (entry == null || !entry.Password.Equals(password))
                    {
                        _ = MessageBox.Show("Incorrect password. Please try again.", "WinFolderLock", MessageBoxButton.OK, MessageBoxImage.Error);
                        passwordWindow = new(mode);
                        continue;
                    }
                }

                // Password is correct or this is a lock operation
                try
                {
                    if (mode == WindowMode.LockFolder)
                    {
                        // Lock folder
                        SavePasswordEntry(filePath, password);

                        string normalizedDirectoryPath = Path.GetFullPath(filePath);
                        string trimmedDirectoryPath = normalizedDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        string folderNameForLock = Path.GetFileName(trimmedDirectoryPath);
                        if (string.IsNullOrWhiteSpace(folderNameForLock))
                        {
                            folderNameForLock = trimmedDirectoryPath;
                        }

                        string? parentDir = Path.GetDirectoryName(trimmedDirectoryPath);
                        if (string.IsNullOrWhiteSpace(parentDir))
                        {
                            parentDir = Path.GetPathRoot(trimmedDirectoryPath) ?? Environment.CurrentDirectory;
                        }

                        string lockedFilePath = Path.Combine(parentDir, folderNameForLock + ".wflck");

                        if (File.Exists(lockedFilePath))
                        {
                            _ = MessageBox.Show($"A locked file already exists: {lockedFilePath}", "WinFolderLock", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        else
                        {
                            FolderLocker.LockFolder(normalizedDirectoryPath, lockedFilePath);
                        }
                    }
                    else if (mode == WindowMode.UnlockFolder)
                    {
                        // Temporary unlock - extract to an app-managed session folder and open in Explorer
                        string sessionFolderPath = FolderLocker.UnlockFolderToTemp(filePath);

                        // Launch Explorer to the session folder in a new window
                        try
                        {
                            _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = "/n,\"" + sessionFolderPath + "\"",
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            Helper.ExceptionHandler(ex);
                            _ = MessageBox.Show($"Could not open Explorer for the session folder: {ex.Message}", "WinFolderLock", MessageBoxButton.OK, MessageBoxImage.Error);
                        }

                        // Automatically re-lock after the Explorer window(s) showing the session folder are closed
                        const int pollMs = 500;
                        int consecutiveNoWindowMs = 0;
                        const int requiredNoWindowMs = 1500;

                        while (true)
                        {
                            try
                            {
                                if (!IsShellWindowShowingPath(sessionFolderPath))
                                {
                                    consecutiveNoWindowMs += pollMs;
                                }
                                else
                                {
                                    consecutiveNoWindowMs = 0;
                                }

                                if (consecutiveNoWindowMs >= requiredNoWindowMs)
                                {
                                    // attempt to re-lock with retries if files are in use
                                    int attempts = 0;
                                    const int maxAttempts = 10;
                                    const int attemptDelayMs = 300;
                                    bool relocked = false;

                                    while (attempts < maxAttempts)
                                    {
                                        try
                                        {
                                            FolderLocker.LockFolder(sessionFolderPath, filePath, overwriteExisting: true);
                                            relocked = true;
                                            break;
                                        }
                                        catch (IOException)
                                        {
                                            attempts++;
                                            System.Threading.Thread.Sleep(attemptDelayMs);
                                        }
                                        catch (Exception ex)
                                        {
                                            Helper.ExceptionHandler(ex);
                                            break;
                                        }
                                    }

                                    if (relocked)
                                    {
                                        try { AdminUtils.NotifyShellOfChange(Path.GetDirectoryName(filePath) ?? string.Empty); } catch { }
                                    }

                                    break;
                                }
                            }
                            catch { }

                            System.Threading.Thread.Sleep(pollMs);
                        }

                        // Clean up all session folders after the session
                        try
                        {
                            string sessionsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinFolderLock", "Sessions");
                            if (Directory.Exists(sessionsRoot))
                            {
                                foreach (string dir in Directory.EnumerateDirectories(sessionsRoot))
                                {
                                    try { Directory.Delete(dir, recursive: true); } catch { }
                                }
                            }
                        }
                        catch { }
                    }
                    else if (mode == WindowMode.PermanentlyUnlockFolder)
                    {
                        // Permanent unlock (extract and delete .wflck file)
                        string? destinationFolderPath = Path.GetDirectoryName(filePath);
                        if (string.IsNullOrWhiteSpace(destinationFolderPath))
                        {
                            destinationFolderPath = Environment.CurrentDirectory;
                        }

                        string folderName = Path.GetFileNameWithoutExtension(filePath);
                        destinationFolderPath = Path.Combine(destinationFolderPath, folderName);

                        if (Directory.Exists(destinationFolderPath))
                        {
                            _ = MessageBox.Show($"Destination folder already exists: {destinationFolderPath}", "WinFolderLock", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        else
                        {
                            FolderLocker.UnlockFolder(filePath, destinationFolderPath, deleteLockedFile: true);
                            // Notify shell to refresh and show the restored folder
                            AdminUtils.NotifyShellOfChange(destinationFolderPath);

                            // Remove stored password entry for this folder since it was permanently unlocked
                            try
                            {
                                RemovePasswordEntry(destinationFolderPath);
                            }
                            catch (Exception ex)
                            {
                                Helper.LogError(ex);
                            }
                        }
                    }

                    break; // Exit the retry loop on success
                }
                catch (ArgumentException ex)
                {
                    Helper.ExceptionHandler(ex);
                    break;
                }
                catch (IOException ex)
                {
                    Helper.ExceptionHandler(ex);
                    break;
                }
                catch (UnauthorizedAccessException ex)
                {
                    Helper.ExceptionHandler(ex);
                    break;
                }
                catch (System.Text.Json.JsonException ex)
                {
                    Helper.ExceptionHandler(ex);
                    break;
                }
                catch (NotSupportedException ex)
                {
                    Helper.ExceptionHandler(ex);
                    break;
                }
                catch (Exception ex)
                {
                    Helper.ExceptionHandler(ex);
                    break;
                }
            }

            Shutdown();
        }

        private static void HandleInstall()
        {
            try
            {
                AdminUtils.InstallApplicationFiles();
                AdminUtils.AddLockFolderContextMenu();
                AdminUtils.AddUnlockFolderContextMenu();
                AdminUtils.AddPermanentUnlockFolderContextMenu();
            }
            catch (Exception ex)
            {
                Helper.ExceptionHandler(ex);
            }
        }

        private static void HandleUninstall()
        {
            try
            {
                // Show confirmation dialog
                MessageBoxResult result = MessageBox.Show(
                    "Uninstalling WinFolderLock will permanently unlock all currently locked folders.\n\nDo you want to continue?",
                    "WinFolderLock - Uninstall Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    // User cancelled uninstall - exit with code 1
                    Environment.Exit(1);
                    return;
                }

                // Unlock all locked folders
                UnlockAllFolders();

                // Remove registry entries and application files
                AdminUtils.RemoveLockFolderContextMenu();
                AdminUtils.RemoveUnlockFolderContextMenu();
                AdminUtils.RemovePermanentUnlockFolderContextMenu();
                AdminUtils.RemoveInstalledApplicationFiles();

                // Successfully uninstalled - exit with code 0
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Helper.ExceptionHandler(ex);
                // Error occurred - exit with code 2
                Environment.Exit(2);
            }
        }

        private static void UnlockAllFolders()
        {
            List<PasswordEntry> entries = LoadPasswordEntries();
            if (entries.Count == 0)
            {
                return; // No folders to unlock
            }

            int successCount = 0;
            int failureCount = 0;

            foreach (PasswordEntry entry in entries)
            {
                try
                {
                    string lockedFilePath = Path.Combine(
                        Path.GetDirectoryName(entry.DirectoryLocation) ?? Environment.CurrentDirectory,
                        entry.FolderName + ".wflck");

                    if (File.Exists(lockedFilePath))
                    {
                        string destinationFolderPath = entry.DirectoryLocation;
                        if (!Directory.Exists(destinationFolderPath))
                        {
                            FolderLocker.UnlockFolder(lockedFilePath, destinationFolderPath, deleteLockedFile: true);
                            successCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Helper.LogError(ex);
                    failureCount++;
                }
            }

            // Clear password entries
            if (successCount > 0 || failureCount == 0)
            {
                try
                {
                    File.Delete(PasswordsFilePath);
                }
                catch (Exception ex)
                {
                    Helper.LogError(ex);
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _wizardMutex?.ReleaseMutex();
            _wizardMutex?.Dispose();

            base.OnExit(e);
        }

        private static void SavePasswordEntry(string directoryLocation, string folderPassword)
        {
            if (string.IsNullOrWhiteSpace(directoryLocation))
            {
                throw new ArgumentException("Directory location is required.", nameof(directoryLocation));
            }

            if (string.IsNullOrWhiteSpace(folderPassword))
            {
                throw new ArgumentException("Password is required.", nameof(folderPassword));
            }

            string normalizedDirectoryPath = Path.GetFullPath(directoryLocation);
            string trimmedDirectoryPath = normalizedDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string folderName = Path.GetFileName(trimmedDirectoryPath);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                folderName = trimmedDirectoryPath;
            }

            _ = Directory.CreateDirectory(ProgramDataDirectoryPath);

            List<PasswordEntry> entries = LoadPasswordEntries();
            PasswordEntry entry = new()
            {
                FolderName = folderName,
                DirectoryLocation = normalizedDirectoryPath,
                Password = folderPassword
            };

            int existingIndex = entries.FindIndex(x => string.Equals(x.DirectoryLocation, normalizedDirectoryPath, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                entries[existingIndex] = entry;
            }
            else
            {
                entries.Add(entry);
            }

            string json = JsonSerializer.Serialize(entries, JsonSerializerOptions);
            File.WriteAllText(PasswordsFilePath, json);
        }

        private static List<PasswordEntry> LoadPasswordEntries()
        {
            if (!File.Exists(PasswordsFilePath))
            {
                return [];
            }

            string json = File.ReadAllText(PasswordsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            List<PasswordEntry> entries = JsonSerializer.Deserialize<List<PasswordEntry>>(json) ?? [];
            return entries;
        }

        private static void RemovePasswordEntry(string directoryLocation)
        {
            if (string.IsNullOrWhiteSpace(directoryLocation))
            {
                return;
            }

            string normalizedDirectoryPath = Path.GetFullPath(directoryLocation).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            List<PasswordEntry> entries = LoadPasswordEntries();
            int index = entries.FindIndex(x => string.Equals(x.DirectoryLocation, normalizedDirectoryPath, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                entries.RemoveAt(index);

                try
                {
                    if (entries.Count == 0)
                    {
                        File.Delete(PasswordsFilePath);
                    }
                    else
                    {
                        string json = JsonSerializer.Serialize(entries, JsonSerializerOptions);
                        File.WriteAllText(PasswordsFilePath, json);
                    }
                }
                catch (Exception ex)
                {
                    Helper.LogError(ex);
                }
            }
        }

        private static bool IsShellWindowShowingPath(string path)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    return false;
                }

                object? shell = Activator.CreateInstance(shellType);
                if (shell == null)
                {
                    return false;
                }

                foreach (dynamic window in ((dynamic)shell).Windows())
                {
                    try
                    {
                        string? locationUrl = window.LocationURL as string;
                        if (string.IsNullOrWhiteSpace(locationUrl))
                        {
                            continue;
                        }

                        string localPath;
                        try { localPath = new Uri(locationUrl).LocalPath; } catch { localPath = locationUrl; }
                        if (string.IsNullOrWhiteSpace(localPath))
                        {
                            continue;
                        }

                        string normalizedLocal = Path.GetFullPath(localPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        string normalizedTarget = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                        if (string.Equals(normalizedLocal, normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                            normalizedLocal.StartsWith(normalizedTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return false;
        }

        private sealed class PasswordEntry
        {
            public string FolderName { get; set; } = string.Empty;

            public string DirectoryLocation { get; set; } = string.Empty;

            public string Password { get; set; } = string.Empty;
        }
    }
}
