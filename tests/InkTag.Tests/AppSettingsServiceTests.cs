using System;
using System.IO;
using InkTag.Core.Configuration;
using Xunit;

namespace InkTag.Tests;

[Collection("AppLogger")]
public class AppSettingsServiceTests
{
    [Fact]
    public void SaveSettings_PersistsAndLoadsSettingsCorrectly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"inktag_settings_test_{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(tempDir, "settings.json");

        try
        {
            var service = new AppSettingsService(settingsPath);
            var customSettings = new AppSettings
            {
                ComicVineApiKey = "test_api_key_12345",
                KomgaServerUrl = "https://komga.example.com",
                KomgaApiKey = "komga_secret_token",
                ThemeMode = AppThemeMode.Dark,
                AutoMatchConfidenceThreshold = 0.92
            };

            service.SaveSettings(customSettings);

            Assert.True(File.Exists(settingsPath));

            // Reload via fresh service instance
            var reloadedService = new AppSettingsService(settingsPath);
            Assert.Equal("test_api_key_12345", reloadedService.Settings.ComicVineApiKey);
            Assert.Equal("https://komga.example.com", reloadedService.Settings.KomgaServerUrl);
            Assert.Equal("komga_secret_token", reloadedService.Settings.KomgaApiKey);
            Assert.Equal(AppThemeMode.Dark, reloadedService.Settings.ThemeMode);
            Assert.Equal(0.92, reloadedService.Settings.AutoMatchConfidenceThreshold);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void SaveSettings_OnUnixPlatforms_EnforcesRestrictivePermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), $"inktag_perms_test_{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(tempDir, "settings.json");

        try
        {
            var service = new AppSettingsService(settingsPath);
            var settings = new AppSettings
            {
                ComicVineApiKey = "secret_cv_key",
                KomgaPassword = "secret_komga_password"
            };

            service.SaveSettings(settings);

            Assert.True(File.Exists(settingsPath));

            // Verify file permissions: 0600 (UserRead | UserWrite, no Group or Other access)
            UnixFileMode fileMode = File.GetUnixFileMode(settingsPath);
            Assert.True((fileMode & UnixFileMode.UserRead) != 0, "UserRead should be enabled on settings file");
            Assert.True((fileMode & UnixFileMode.UserWrite) != 0, "UserWrite should be enabled on settings file");
            Assert.False((fileMode & UnixFileMode.GroupRead) != 0, "GroupRead must be disabled on settings file");
            Assert.False((fileMode & UnixFileMode.GroupWrite) != 0, "GroupWrite must be disabled on settings file");
            Assert.False((fileMode & UnixFileMode.OtherRead) != 0, "OtherRead must be disabled on settings file");
            Assert.False((fileMode & UnixFileMode.OtherWrite) != 0, "OtherWrite must be disabled on settings file");

            // Verify directory permissions: 0700 (UserRead | UserWrite | UserExecute, no Group or Other access)
            UnixFileMode dirMode = File.GetUnixFileMode(tempDir);
            Assert.True((dirMode & UnixFileMode.UserRead) != 0, "UserRead should be enabled on config dir");
            Assert.True((dirMode & UnixFileMode.UserWrite) != 0, "UserWrite should be enabled on config dir");
            Assert.True((dirMode & UnixFileMode.UserExecute) != 0, "UserExecute should be enabled on config dir");
            Assert.False((dirMode & UnixFileMode.GroupRead) != 0, "GroupRead must be disabled on config dir");
            Assert.False((dirMode & UnixFileMode.GroupWrite) != 0, "GroupWrite must be disabled on config dir");
            Assert.False((dirMode & UnixFileMode.OtherRead) != 0, "OtherRead must be disabled on config dir");
            Assert.False((dirMode & UnixFileMode.OtherWrite) != 0, "OtherWrite must be disabled on config dir");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
