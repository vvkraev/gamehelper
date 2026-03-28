using System.Windows;

namespace GameHelper;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        Services.SessionLogger.Initialize();
        new MainWindow().Show();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        Services.SessionLogger.Shutdown();
    }
}
