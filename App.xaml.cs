using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using GameHelper.Services;
using GameHelper.ViewModels;

namespace GameHelper;

public partial class App : System.Windows.Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        SessionLogger.Initialize();
        BuildServiceProvider().GetRequiredService<MainWindow>().Show();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        SessionLogger.Shutdown();
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ISessionLogger, SessionLoggerAdapter>();
        services.AddSingleton<IProjectPaths, ProjectPathsAdapter>();
        services.AddSingleton<IAffixLibrary, AffixLibraryAdapter>();
        services.AddSingleton<OmenActivationService>();
        services.AddSingleton<ChaosCraftService>();
        services.AddSingleton<AugAnnulCraftService>();
        services.AddSingleton<ExaltationCraftServiceFracturedSide>();
        services.AddSingleton<SharpenService>();
        services.AddSingleton<FracturingOrbService>();
        services.AddSingleton<DivineCraftService>();
        services.AddSingleton<ReforgeService>();
        services.AddSingleton<AutoReforgeService>();
        services.AddSingleton<RepricingService>();
        services.AddSingleton<ChancingService>();
        services.AddSingleton<CraftOrchestrator>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }
}
