using System;
using System.Windows;
using System.Windows.Threading;
using AntiBlockManager.Application.Services;
using Velopack;

namespace AntiBlockManager.UI;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Velopack ДОЛЖЕН быть первым — иначе updater не работает
        VelopackApp.Build().Run();

        base.OnStartup(e);

        DispatcherUnhandledException += (s, ex) =>
        {
            MessageBox.Show(
                $"Необработанная ошибка:\n{ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        try
        {
            var settings = new SettingsService();
            LoadTheme(settings.Current.IsLightTheme);

            var loadingScreen = new LoadingScreen();
            loadingScreen.ShowDialog();

            if (settings.Current.IsFirstLaunch)
            {
                settings.Current.Language = "ru";
                settings.Current.IsFirstLaunch = false;
                settings.Save();
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка запуска:\n{ex.Message}\n\n{ex.StackTrace}",
                "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public static void LoadTheme(bool isLight)
    {
        var dict = new ResourceDictionary();
        dict.Source = new Uri(isLight ? "Themes/LightTheme.xaml" : "Themes/DarkTheme.xaml", UriKind.Relative);
        Current.Resources.MergedDictionaries.Clear();
        Current.Resources.MergedDictionaries.Add(dict);
    }
}