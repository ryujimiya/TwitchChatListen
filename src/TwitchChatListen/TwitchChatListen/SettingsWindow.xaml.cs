using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TwitchChatListen
{
    /// <summary>
    /// SettingsWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public string AccountName { get; set; }
        public string Password { get; set; }
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SetDataToGui();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            GetDataFromGui();
        }

        private void SetDataToGui()
        {
            AccountTextBox.Text = AccountName;
            PasswordBox.Password = Password;
        }

        private void GetDataFromGui()
        {
            AccountName = AccountTextBox.Text;
            Password = PasswordBox.Password;
        }

        private void MakePasswordBtn_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://twitchapps.com/tmi/";
            // ブラウザで開く
            System.Diagnostics.Process.Start(url);
        }

    }
}
