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

                if (process.ExitCode == 0)
                {
                    ConfirmationText.Text = isInstall
                        ? "WinFolderLock installed successfully."
                        : "WinFolderLock uninstalled successfully.";
                }
                else if (process.ExitCode == 1)
                {
                    ConfirmationText.Foreground = Brushes.Orange;
                    ConfirmationText.Text = isInstall
                        ? "Installation cancelled."
                        : "Uninstallation cancelled.";
                }
                else
                {
                    ConfirmationText.Foreground = Brushes.Red;
                    ConfirmationText.Text = "An error occurred during the operation.";
                }
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
