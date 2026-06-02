using System.Windows;
using System.Windows.Input;

namespace WinFolderLock
{
    public partial class PasswordInputWindow : Window
    {
        public PasswordInputWindow() : this(WindowMode.LockFolder) { }

        public PasswordInputWindow(WindowMode mode)
        {
            InitializeComponent();
            SetMode(mode);
        }

        public string Password { get; private set; } = string.Empty;


        private void SetMode(WindowMode mode)
        {
            Title = mode switch
            {
                WindowMode.LockFolder => "Lock Folder",
                WindowMode.UnlockFolder => "Unlock Folder",
                WindowMode.PermanentlyUnlockFolder => "Permanently Unlock Folder",
                _ => "WinFolderLock"
            };
        }

        private void ShowPasswordButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            SwitchPasswordVisibility(reveal: true);
        }

        private void ShowPasswordButton_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            SwitchPasswordVisibility(reveal: false);
        }

        private void ShowPasswordButton_MouseLeave(object sender, MouseEventArgs e)
        {
            SwitchPasswordVisibility(reveal: false);
        }

        private void SwitchPasswordVisibility(bool reveal)
        {
            if (reveal)
            {
                UserTextBox.Text = UserPasswordBox.Password;
                UserTextBox.Visibility = Visibility.Visible;
                UserPasswordBox.Visibility = Visibility.Collapsed;
            }
            else if (UserTextBox.Visibility == Visibility.Visible)
            {
                UserPasswordBox.Password = UserTextBox.Text;
                UserPasswordBox.Visibility = Visibility.Visible;
                UserTextBox.Visibility = Visibility.Collapsed;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Password = UserPasswordBox.Password;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}

