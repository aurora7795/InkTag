using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using InkTag.Core.Configuration;

namespace InkTag.Gui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        var settingsService = new AppSettingsService();
        ApplyTheme(settingsService.Settings.ThemeMode);
    }

    public static void ApplyTheme(AppThemeMode mode)
    {
        if (Current != null)
        {
            Current.RequestedThemeVariant = mode switch
            {
                AppThemeMode.Light => ThemeVariant.Light,
                AppThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Views.MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void About_Click(object? sender, System.EventArgs e)
    {
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var aboutWindow = new Views.AboutWindow();
                await aboutWindow.ShowDialog(desktop.MainWindow);
            }
        }
        catch (System.Exception ex)
        {
            Core.Logging.AppLogger.LogError("Failed to open About window.", ex);
        }
    }

    private async void Settings_Click(object? sender, System.EventArgs e)
    {
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var settingsWindow = new Views.SettingsWindow();
                await settingsWindow.ShowDialog(desktop.MainWindow);
            }
        }
        catch (System.Exception ex)
        {
            Core.Logging.AppLogger.LogError("Failed to open Settings window.", ex);
        }
    }
}
