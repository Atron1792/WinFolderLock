using Microsoft.Win32;
using System.IO;
using System.Security.Principal;

namespace WinFolderLock;

internal static partial class AdminUtils
{
    private const string ApplicationFolderName = "WinFolderLock";
    private const string ResourcesFolderName = "Resources";
    private const string ContextMenuKeyPath = @"Software\Classes\Directory\shell\WinFolderLock";
    private const string ContextMenuCommandKeyPath = @"Software\Classes\Directory\shell\WinFolderLock\command";
    private static readonly string[] InstalledResourceFileNames =
    [
        "Install.ico",
        "Locked.ico",
        "LockedWin10.ico",
        "LockedWIn11.ico",
        "Unlocked.ico"
    ];

    // P/Invoke to notify the shell about association/context menu changes so Explorer refreshes
    [System.Runtime.InteropServices.LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    private static void NotifyShell()
    {
        try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); } catch (Exception ex) { Helper.LogError(ex); }
    }

    internal static void NotifyShellOfChange(string path)
    {
        try
        {
            // Notify about the path itself
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

            // Also notify about the parent directory to refresh the folder view
            string? parentDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                System.Threading.Thread.Sleep(100); // Give the system a moment to complete extraction
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch (Exception ex) { Helper.LogError(ex); }
    }

    internal static bool IsRunningAsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static void AddLockFolderContextMenu()
    {
        string executablePath = GetInstalledExecutablePath();
        string iconPath = GetContextMenuIconPath();

        Registry.CurrentUser.DeleteSubKeyTree(ContextMenuKeyPath, throwOnMissingSubKey: false);

        using RegistryKey menuKey = Registry.CurrentUser.CreateSubKey(ContextMenuKeyPath);
        ArgumentNullException.ThrowIfNull(menuKey);

        menuKey.SetValue(string.Empty, "Lock Folder", RegistryValueKind.String);
        menuKey.SetValue("Icon", iconPath, RegistryValueKind.String);

        using RegistryKey commandKey = Registry.CurrentUser.CreateSubKey(ContextMenuCommandKeyPath);
        ArgumentNullException.ThrowIfNull(commandKey);

        commandKey.SetValue(string.Empty, $"\"{executablePath}\" \"%1\"", RegistryValueKind.String);
        // Notify shell so the new context menu appears without requiring logout/explorer restart
        NotifyShell();
    }

    internal static void RemoveLockFolderContextMenu()
    {
        Registry.CurrentUser.DeleteSubKeyTree(ContextMenuKeyPath, throwOnMissingSubKey: false);
        NotifyShell();
    }

    internal static void AddUnlockFolderContextMenu()
    {
        string executablePath = GetInstalledExecutablePath();
        string wflckIconPath = GetInstalledResourcePath("LockedWIn11.ico");

        // Register .wflck extension under HKCU so it is visible to the current user without requiring admin.
        const string extKey = @"Software\Classes\.wflck";
        const string progId = "WinFolderLock.File";
        const string progIdKey = @"Software\Classes\WinFolderLock.File";

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(extKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(progIdKey, throwOnMissingSubKey: false);
        }
        catch (Exception ex) { Helper.LogError(ex); }

        using (RegistryKey ext = Registry.CurrentUser.CreateSubKey(extKey))
        {
            ArgumentNullException.ThrowIfNull(ext);
            ext.SetValue(string.Empty, progId, RegistryValueKind.String);
        }

        using (RegistryKey p = Registry.CurrentUser.CreateSubKey(progIdKey))
        {
            ArgumentNullException.ThrowIfNull(p);
            p.SetValue(string.Empty, "WFL Locked Folder", RegistryValueKind.String);
        }

        // Default icon for the ProgID
        using (RegistryKey defIcon = Registry.CurrentUser.CreateSubKey(progIdKey + "\\DefaultIcon"))
        {
            ArgumentNullException.ThrowIfNull(defIcon);
            defIcon.SetValue(string.Empty, wflckIconPath, RegistryValueKind.String);
        }

        // Shell open command for ProgID (default action on double-click) - label it "Unlock Folder"
        using (RegistryKey shellOpen = Registry.CurrentUser.CreateSubKey(progIdKey + "\\shell\\open"))
        {
            ArgumentNullException.ThrowIfNull(shellOpen);
            shellOpen.SetValue(string.Empty, "Unlock Folder", RegistryValueKind.String);
        }

        using (RegistryKey openCmd = Registry.CurrentUser.CreateSubKey(progIdKey + "\\shell\\open\\command"))
        {
            ArgumentNullException.ThrowIfNull(openCmd);
            openCmd.SetValue(string.Empty, $"\"{executablePath}\" \"%1\"", RegistryValueKind.String);
        }

        // Permanent unlock as a context menu entry
        const string extMenuKey = @"Software\Classes\.wflck\shell\WinFolderLockPermUnlock";
        const string extCommandKeyPath = @"Software\Classes\.wflck\shell\WinFolderLockPermUnlock\command";

        Registry.CurrentUser.DeleteSubKeyTree(extMenuKey, throwOnMissingSubKey: false);

        using RegistryKey menuKey = Registry.CurrentUser.CreateSubKey(extMenuKey);
        ArgumentNullException.ThrowIfNull(menuKey);

        menuKey.SetValue(string.Empty, "Permanently Unlock Folder", RegistryValueKind.String);
        menuKey.SetValue("Icon", wflckIconPath, RegistryValueKind.String);

        using RegistryKey commandKey = Registry.CurrentUser.CreateSubKey(extCommandKeyPath);
        ArgumentNullException.ThrowIfNull(commandKey);

        // Command includes a /permunlock switch to indicate permanent unlock behavior
        commandKey.SetValue(string.Empty, $"\"{executablePath}\" /permunlock \"%1\"", RegistryValueKind.String);
        // Refresh Explorer to show the new context menu entry immediately
        NotifyShell();
    }

    internal static void RemoveUnlockFolderContextMenu()
    {
        const string extKey = @"Software\Classes\.wflck";
        const string progIdKey = @"Software\Classes\WinFolderLock.File";
        Registry.CurrentUser.DeleteSubKeyTree(extKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(progIdKey, throwOnMissingSubKey: false);
        NotifyShell();
    }

    internal static void AddPermanentUnlockFolderContextMenu()
    {
        string executablePath = GetInstalledExecutablePath();
        string unlockedIconPath = GetInstalledResourcePath("Unlocked.ico");

        // Register permanent unlock as a context menu entry for the ProgID (not extension)
        const string progIdKey = @"Software\Classes\WinFolderLock.File";
        const string extMenuKey = progIdKey + @"\shell\WinFolderLockPermUnlock";
        const string extCommandKeyPath = extMenuKey + @"\command";

        Registry.CurrentUser.DeleteSubKeyTree(extMenuKey, throwOnMissingSubKey: false);

        using RegistryKey menuKey = Registry.CurrentUser.CreateSubKey(extMenuKey);
        ArgumentNullException.ThrowIfNull(menuKey);

        menuKey.SetValue(string.Empty, "Permanently Unlock Folder", RegistryValueKind.String);
        menuKey.SetValue("Icon", unlockedIconPath, RegistryValueKind.String);

        using RegistryKey commandKey = Registry.CurrentUser.CreateSubKey(extCommandKeyPath);
        ArgumentNullException.ThrowIfNull(commandKey);

        // Command includes a /permunlock switch to indicate permanent unlock behavior
        commandKey.SetValue(string.Empty, $"\"{executablePath}\" /permunlock \"%1\"", RegistryValueKind.String);
        // Refresh Explorer to show the new context menu entry immediately
        NotifyShell();
    }

    internal static void RemovePermanentUnlockFolderContextMenu()
    {
        const string progIdKey = @"Software\Classes\WinFolderLock.File";
        const string extKeyPath = progIdKey + @"\shell\WinFolderLockPermUnlock";
        Registry.CurrentUser.DeleteSubKeyTree(extKeyPath, throwOnMissingSubKey: false);
        NotifyShell();
    }

    internal static void InstallApplicationFiles()
    {
        string installDirectoryPath = GetInstallDirectoryPath();
        if (Directory.Exists(installDirectoryPath))
        {
            Directory.Delete(installDirectoryPath, recursive: true);
        }

        _ = Directory.CreateDirectory(installDirectoryPath);

        string sourceDirectoryPath = GetCurrentExecutableDirectoryPath();
        CopyDirectoryContents(sourceDirectoryPath, installDirectoryPath);
    }

    internal static void RemoveInstalledApplicationFiles()
    {
        string installDirectoryPath = GetInstallDirectoryPath();
        if (Directory.Exists(installDirectoryPath))
        {
            Directory.Delete(installDirectoryPath, recursive: true);
        }
    }

    private static string GetInstallDirectoryPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ApplicationFolderName);
    }

    private static string GetInstalledResourcesDirectoryPath()
    {
        return Path.Combine(GetInstallDirectoryPath(), ResourcesFolderName);
    }

    internal static string GetInstalledExecutablePath()
    {
        string installedExecutablePath = Path.Combine(GetInstallDirectoryPath(), GetCurrentExecutableFileName());
        return File.Exists(installedExecutablePath) ? installedExecutablePath : GetCurrentExecutablePath();
    }

    private static string GetInstalledResourcePath(string resourceFileName)
    {
        string resourcePath = Path.Combine(GetInstalledResourcesDirectoryPath(), resourceFileName);
        if (File.Exists(resourcePath))
        {
            return resourcePath;
        }

        string execDir = GetCurrentExecutableDirectoryPath();

        // First check Resources subfolder next to the executable (common layout)
        string candidateInResources = Path.Combine(execDir, ResourcesFolderName, resourceFileName);
        if (File.Exists(candidateInResources))
        {
            return candidateInResources;
        }

        // Next check for the file copied directly into the output directory
        string candidateInOutput = Path.Combine(execDir, resourceFileName);
        if (File.Exists(candidateInOutput))
        {
            return candidateInOutput;
        }

        // Fallback to the Resources subfolder path (keeps previous behavior when files are installed)
        return candidateInResources;
    }

    private static string GetCurrentExecutablePath()
    {
        string? executablePath = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(executablePath)
            ? throw new InvalidOperationException("Could not resolve executable path for context menu registration.")
            : executablePath;
    }

    private static string GetCurrentExecutableDirectoryPath()
    {
        string? executableDirectoryPath = Path.GetDirectoryName(GetCurrentExecutablePath());
        return string.IsNullOrWhiteSpace(executableDirectoryPath)
            ? throw new InvalidOperationException("Could not resolve executable directory for installation.")
            : executableDirectoryPath;
    }

    private static string GetCurrentExecutableFileName()
    {
        return Path.GetFileName(GetCurrentExecutablePath());
    }

    private static void CopyDirectoryContents(string sourceDirectoryPath, string destinationDirectoryPath)
    {
        foreach (string filePath in Directory.EnumerateFiles(sourceDirectoryPath))
        {
            if (Path.GetExtension(filePath).Equals(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destinationPath = Path.Combine(destinationDirectoryPath, Path.GetFileName(filePath));
            File.Copy(filePath, destinationPath, overwrite: true);
        }

        foreach (string directoryPath in Directory.EnumerateDirectories(sourceDirectoryPath))
        {
            string destinationPath = Path.Combine(destinationDirectoryPath, Path.GetFileName(directoryPath));
            _ = Directory.CreateDirectory(destinationPath);
            CopyDirectoryContents(directoryPath, destinationPath);
        }
    }

    private static string GetContextMenuIconPath()
    {
        return GetInstalledResourcePath("Locked.ico");
    }
}
