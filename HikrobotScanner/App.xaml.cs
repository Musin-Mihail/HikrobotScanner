using HikrobotScanner.Interfaces;
using HikrobotScanner.Services;
using HikrobotScanner.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace HikrobotScanner
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; }

        public App()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IAppLogger, AppLogger>();
            services.AddSingleton<IDispatcherService, WpfDispatcherService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IBarcodeService, BarcodeService>();
            services.AddSingleton<IDataService, DataService>();
            services.AddSingleton<ICameraService, CameraService>();
            services.AddSingleton<IServerService, ServerService>();

            services.AddTransient<MainViewModel>();
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var mainWindow = new MainWindow
            {
                DataContext = ServiceProvider.GetRequiredService<MainViewModel>()
            };
            mainWindow.Show();
        }
    }
}
