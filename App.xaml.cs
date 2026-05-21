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

            var isWizardLaunch = e.Args.Length == 0;
            if (isWizardLaunch)
            {
                _wizardMutex = new Mutex(initiallyOwned: true, WizardInstanceMutexName, out var isFirstWizardInstance);
                if (!isFirstWizardInstance)
                {
                    _wizardMutex.Dispose();

                    MessageBox.Show("A wizard is already running.", "WinFolderLock", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }

                MainWindow = new WizardWindow();
                MainWindow.Show();
                return;
            }

            // Check for command line switches for different modes
            bool isPermanentUnlock = e.Args.Length > 0 && e.Args[0] == "/permunlock";
            int fileArgIndex = isPermanentUnlock ? 1 : 0;

            if (e.Args.Length <= fileArgIndex)
            {
                Shutdown();
                return;
            }

            var filePath = e.Args[fileArgIndex];

            // Determine the appropriate window mode based on whether it's a file or folder
            PasswordInputWindow.WindowMode mode = PasswordInputWindow.WindowMode.LockFolder;
            if (FolderLocker.IsLockedFile(filePath))
            {
                mode = isPermanentUnlock ? PasswordInputWindow.WindowMode.PermanentlyUnlockFolder : PasswordInputWindow.WindowMode.UnlockFolder;
            }

            PasswordInputWindow passwordWindow = new(mode);

            while (true)
            {
                var isConfirmed = passwordWindow.ShowDialog() == true;

                if (!isConfirmed)
                {
                    Shutdown();
                    return;
                }

                var password = passwordWindow.Password;

                if (string.IsNullOrWhiteSpace(password))
                {
                    MessageBox.Show("Password cannot be empty. Please try again.", "WinFolderLock", MessageBoxButton.OK, MessageBoxImage.Warning);
                    passwordWindow = new(mode);
                    continue;
                }

                // Validate password if unlocking
                if (mode == PasswordInputWindow.WindowMode.UnlockFolder || mode == PasswordInputWindow.WindowMode.PermanentlyUnlockFolder)
                {
                    var entries = LoadPasswordEntries();
                    var lockedFileInfo = new System.IO.FileInfo(filePath);
                    var folderName = Path.GetFileNameWithoutExtension(filePath);

                    var entry = entries.FirstOrDefault(x => 
                        x.FolderName.Equals(folderName, StringComparison.OrdinalIgnoreCase));

                    if (entry == null || !entry.Password.Equals(password))
                    {
                        MessageBox.Show("Incorrect password. Please try again.", "WinFolderLock", MessageBoxButton.OK, MessageBoxImage.Error);
                        passwordWindow = new(mode);
                        continue;
                    }
                }

                // Password is correct or this is a lock operation
                try
                {
                    if (mode == PasswordInputWindow.WindowMode.LockFolder)
                    {
                        // Lock folder
                        SavePasswordEntry(filePath, password);

                        var normalizedDirectoryPath = Path.GetFullPath(filePath);
                        var trimmedDirectoryPath = normalizedDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        var folderNameForLock = Path.GetFileName(trimmedDirectoryPath);
                        if (string.IsNullOrWhiteSpace(folderNameForLock))
                        {
                            folderNameForLock = trimmedDirectoryPath;
                        }

                        var parentDir = Path.GetDirectoryName(trimmedDirectoryPath);
                        if (string.IsNullOrWhiteSpace(parentDir))
                        {
                            parentDir = Path.GetPathRoot(trimmedDirectoryPath) ?? Environment.CurrentDirectory;
                        }

                        var lockedFilePath = Path.Combine(parentDir, folderNameForLock + ".wflck");

                        if (File.Exists(lockedFilePath))
                        {
                            MessageBox.Show($"A locked file already exists: {lockedFilePath}", "WinFolderLock", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        else
                        {
                            FolderLocker.LockFolder(normalizedDirectoryPath, lockedFilePath);
                        }
                    }
                    else if (mode == PasswordInputWindow.WindowMode.UnlockFolder)
                    {
                        // Temporary unlock - extract to temp location and open in Explorer
                        var tempFolderPath = FolderLocker.UnlockFolderToTemp(filePath);

                        // Open the temp folder in Windows Explorer
                        var explorerProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = tempFolderPath,
                            UseShellExecute = true
                        });

                        // Wait for Explorer window to close, then re-lock the modified contents
                        if (explorerProcess != null)
                        {
                            explorerProcess.WaitForExit();

                            // Wait for all Explorer processes to release the temp folder
                            int explorerWaitAttempts = 0;
                            const int maxExplorerWaitAttempts = 10;
                            while (explorerWaitAttempts < maxExplorerWaitAttempts)
                            {
                                try
                                {
                                    // Check if temp folder is still in use by attempting to access it
                                    if (Directory.Exists(tempFolderPath))
                                    {
                                        _ = Directory.GetFiles(tempFolderPath);
                                    }
                                    break; // Folder is accessible, Explorer has released it
                                }
                                catch
                                {
                                    explorerWaitAttempts++;
                                    if (explorerWaitAttempts < maxExplorerWaitAttempts)
                                    {
                                        System.Threading.Thread.Sleep(200); // Wait and retry
                                    }
                                }
                            }

                            // Re-lock the modified temp folder back to the original .wflck file
                            try
                            {
                                FolderLocker.LockFolder(tempFolderPath, filePath, overwriteExisting: true);
                            }
                            catch (Exception ex)
                            {
                                ExceptionHandler.Handle(ex);
                            }
                            finally
                            {
                                // Clean up temp folder and files with retry logic
                                try
                                {
                                    if (Directory.Exists(tempFolderPath))
                                    {
                                        // Give the system another moment to fully release locks
                                        System.Threading.Thread.Sleep(200);

                                        // Retry deletion with delay if it fails
                                        int retryCount = 0;
                                        const int maxRetries = 3;
                                        while (retryCount < maxRetries)
                                        {
                                            try
                                            {
                                                Directory.Delete(tempFolderPath, recursive: true);
                                                break; // Success, exit retry loop
                                            }
                                            catch (Exception deleteEx)
                                            {
                                                retryCount++;
                                                if (retryCount < maxRetries)
                                                {
                                                    System.Threading.Thread.Sleep(200 * retryCount); // Exponential backoff
                                                }
                                                else
                                                {
                                                    ExceptionHandler.LogError(deleteEx);
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ExceptionHandler.LogError(ex);
                                }
                            }
                        }
                    }
                    else if (mode == PasswordInputWindow.WindowMode.PermanentlyUnlockFolder)
                    {
                        // Permanent unlock (extract and delete .wflck file)
                        var destinationFolderPath = Path.GetDirectoryName(filePath);
                        if (string.IsNullOrWhiteSpace(destinationFolderPath))
                        {
                            destinationFolderPath = Environment.CurrentDirectory;
                        }

                        var folderName = Path.GetFileNameWithoutExtension(filePath);
                        destinationFolderPath = Path.Combine(destinationFolderPath, folderName);

                        if (Directory.Exists(destinationFolderPath))
                        {
                            MessageBox.Show($"Destination folder already exists: {destinationFolderPath}", "WinFolderLock", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        else
                        {
                            FolderLocker.UnlockFolder(filePath, destinationFolderPath, deleteLockedFile: true);
                            // Notify shell to refresh and show the restored folder
                            AdminUtils.NotifyShellOfChange(destinationFolderPath);
                        }
                    }

                    break; // Exit the retry loop on success
                }
                catch (ArgumentException ex)
                {
                    ExceptionHandler.Handle(ex);
                    break;
                }
                catch (IOException ex)
                {
                    ExceptionHandler.Handle(ex);
                    break;
                }
                catch (UnauthorizedAccessException ex)
                {
                    ExceptionHandler.Handle(ex);
                    break;
                }
                catch (System.Text.Json.JsonException ex)
                {
                    ExceptionHandler.Handle(ex);
                    break;
                }
                catch (NotSupportedException ex)
                {
                    ExceptionHandler.Handle(ex);
                    break;
                }
                catch (Exception ex)
                {
                    ExceptionHandler.Handle(ex);
                    break;
                }
            }

            Shutdown();
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

            var normalizedDirectoryPath = Path.GetFullPath(directoryLocation);
            var trimmedDirectoryPath = normalizedDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(trimmedDirectoryPath);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                folderName = trimmedDirectoryPath;
            }

            Directory.CreateDirectory(ProgramDataDirectoryPath);

            var entries = LoadPasswordEntries();
            var entry = new PasswordEntry
            {
                FolderName = folderName,
                DirectoryLocation = normalizedDirectoryPath,
                Password = folderPassword
            };

            var existingIndex = entries.FindIndex(x => string.Equals(x.DirectoryLocation, normalizedDirectoryPath, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                entries[existingIndex] = entry;
            }
            else
            {
                entries.Add(entry);
            }

            var json = JsonSerializer.Serialize(entries, JsonSerializerOptions);
            File.WriteAllText(PasswordsFilePath, json);
        }

        private static List<PasswordEntry> LoadPasswordEntries()
        {
            if (!File.Exists(PasswordsFilePath))
            {
                return [];
            }

            var json = File.ReadAllText(PasswordsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<PasswordEntry>>(json) ?? [];
        }

        private sealed class PasswordEntry
        {
            public string FolderName { get; set; } = string.Empty;

            public string DirectoryLocation { get; set; } = string.Empty;

            public string Password { get; set; } = string.Empty;
        }
    }
}
