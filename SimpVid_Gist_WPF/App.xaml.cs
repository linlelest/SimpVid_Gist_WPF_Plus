using System.Windows;

namespace SimpVid_Gist_WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Logger.Separate();
            Logger.Log("Application starting...");

            DispatcherUnhandledException += (s, args) =>
            {
                Logger.LogError("DispatcherUnhandledException", args.Exception);
                args.Handled = true;
                MessageBox.Show(args.Exception.ToString(), "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Logger.LogError("AppDomain.UnhandledException", ex);
            };

            base.OnStartup(e);
        }
    }
}
