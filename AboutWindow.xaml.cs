using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace MetricBot
{
    public partial class AboutWindow : Window
    {
        private const string Functionality = "TeaLover, Azgeda, Eteria";
        private const string Code          = "Claude, ChatGPT";
        private const string Ui            = "TeaLover";
        private const string CgbsWebsite   = "https://astra-cgbs.ru";
        private const string Support       = "itcgbs@gmail.com";

        public AboutWindow()
        {
            InitializeComponent();

            TxtFunctionality.Text = Functionality;
            TxtCode.Text          = Code;
            TxtUi.Text            = Ui;
            TxtSupport.Text       = Support;
        }

        private void BtnClose_Click(object s, RoutedEventArgs e) => Close();


        private void Cgbs_Click(object s, RoutedEventArgs e) => OpenUrl(CgbsWebsite);

        private void TxtSupport_Click(object s, MouseButtonEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(Support))
                OpenUrl($"mailto:{Support}");
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
