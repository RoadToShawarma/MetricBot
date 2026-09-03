using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace MetricBot
{
    public partial class AboutWindow : Window
    {
#if NET10_0_OR_GREATER
        private const string Edition       = "Modern";
#else
        private const string Edition       = "Legacy";
#endif
        private const string Functionality = "RoadToShawarma, Azgeda";
        private const string Code          = "Claude, Codex";
        private const string Ui            = "RoadToShawarma";
        private const string ProjectUrl    = "https://github.com/RoadToShawarma/MetricBot";

        public AboutWindow()
        {
            InitializeComponent();

            var architecture = Environment.Is64BitProcess ? "x64" : "x86";
            TxtEdition.Text       = $"{Edition} {architecture}";
            TxtVersion.Text       = "v1.0.1";
            TxtFunctionality.Text = Functionality;
            TxtCode.Text          = Code;
            TxtUi.Text            = Ui;
            TxtSupport.Text       = "GitHub";
        }

        private void TxtSupport_Click(object s, MouseButtonEventArgs e)
        {
            OpenUrl(ProjectUrl);
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
