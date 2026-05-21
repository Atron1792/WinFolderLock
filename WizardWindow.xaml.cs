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
            try
            {
                // Register both folder and .wflck file context menu entries for the current user
                AdminUtils.AddLockFolderContextMenu();
                AdminUtils.AddUnlockFolderContextMenu();
                AdminUtils.AddPermanentUnlockFolderContextMenu();

                ConfirmationText.Foreground = Brushes.White;
                ConfirmationText.Text = "WinFolderLock installed successfully.";
            }
            catch (InvalidOperationException ex)
            {
                ExceptionHandler.Handle(ex);
                ConfirmationText.Foreground = Brushes.Red;
                ConfirmationText.Text = ex.Message;
            }
            catch (Exception ex)
            {
                ExceptionHandler.Handle(ex);
                ConfirmationText.Foreground = Brushes.Red;
                ConfirmationText.Text = "Installation failed. See error details.";
            }
        }

        private void OnUninstallClicked(object sender, RoutedEventArgs e)
        {
            // Remove both registrations
            AdminUtils.RemoveLockFolderContextMenu();
            AdminUtils.RemoveUnlockFolderContextMenu();
            AdminUtils.RemovePermanentUnlockFolderContextMenu();

            ConfirmationText.Foreground = Brushes.White;
    ConfirmationText.Text = "WinFolderLock uninstalled successfully.";
}
    }
}
