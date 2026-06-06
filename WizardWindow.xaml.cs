using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace WinFolderLock
{
    public partial class WizardWindow : Window
    {
        public WizardWindow()
        {
            InitializeComponent();
        }

        private void OnInstallClicked(object sender, RoutedEventArgs e)
        {
            RunElevatedMaintenanceAction("/install");
        }

        private void OnUninstallClicked(object sender, RoutedEventArgs e)
        {
            RunElevatedMaintenanceAction("/uninstall");
        }

        private void RunElevatedMaintenanceAction(string argument)
        {
            try
            {
                string? executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    throw new InvalidOperationException("Could not determine the application path.");
                }

                Process process = Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = argument,
                    UseShellExecute = true,
                    Verb = "runas"
                }) ?? throw new InvalidOperationException("The administrator prompt could not be started.");

                // Wait for the elevated process to complete
                process.WaitForExit();

                // Check exit code to determine success
                bool isInstall = argument.Equals("/install", StringComparison.OrdinalIgnoreCase);
                ConfirmationText.Foreground = Brushes.White;

                ConfirmationText.Text = process.ExitCode switch
                {
                    0 => isInstall ? "WinFolderLock installed successfully." : "WinFolderLock uninstalled successfully.",
                    1 => isInstall ? "Installation cancelled." : "Uninstallation cancelled.",
                    _ => "An error occurred during the operation."
                };

                ConfirmationText.Foreground = process.ExitCode switch
                {
                    0 => Brushes.White,
                    1 => Brushes.Orange,
                    _ => Brushes.Red
                };
            }
            catch (Exception ex)
            {
                Helper.ExceptionHandler(ex);
                ConfirmationText.Foreground = Brushes.Red;
                ConfirmationText.Text = ex.Message;
            }
        }
    }
}
