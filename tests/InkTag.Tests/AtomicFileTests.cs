using System;
using System.IO;
using System.Linq;
using System.Text;
using InkTag.Core.Backup;
using InkTag.Core.Configuration;
using InkTag.Core.IO;
using InkTag.Core.Logging;
using InkTag.Core.Scrapers;
using Xunit;

namespace InkTag.Tests;

[Collection("AppLogger")]
public class AtomicFileTests : IDisposable
{
    private readonly string _testDir;

    public AtomicFileTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "InkTagAtomicFileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    [Fact]
    public void AtomicFile_WriteAllText_CreatesAndOverwritesAtomicallyWithoutLeavingTempFiles()
    {
        string filePath = Path.Combine(_testDir, "test.json");

        // 1. Initial write
        AtomicFile.WriteAllText(filePath, "{\"version\": 1}");
        Assert.True(File.Exists(filePath));
        Assert.Equal("{\"version\": 1}", File.ReadAllText(filePath));

        // 2. Overwrite
        AtomicFile.WriteAllText(filePath, "{\"version\": 2}");
        Assert.Equal("{\"version\": 2}", File.ReadAllText(filePath));

        // 3. Confirm no leftover temporary files in directory
        var remainingFiles = Directory.GetFiles(_testDir, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToList();

        Assert.Single(remainingFiles);
        Assert.Equal("test.json", remainingFiles[0]);
    }

    [Fact]
    public void AtomicFile_WriteAllBytes_CreatesAndOverwritesAtomically()
    {
        string filePath = Path.Combine(_testDir, "binary.dat");
        byte[] data1 = [0xDE, 0xAD, 0xBE, 0xEF];
        byte[] data2 = [0xCA, 0xFE, 0xBA, 0xBE];

        AtomicFile.WriteAllBytes(filePath, data1);
        Assert.True(File.Exists(filePath));
        Assert.Equal(data1, File.ReadAllBytes(filePath));

        AtomicFile.WriteAllBytes(filePath, data2);
        Assert.Equal(data2, File.ReadAllBytes(filePath));

        var remainingFiles = Directory.GetFiles(_testDir, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToList();

        Assert.Single(remainingFiles);
        Assert.Equal("binary.dat", remainingFiles[0]);
    }

    [Fact]
    public void AtomicFile_WriteAllText_CreatesNestedParentDirectories()
    {
        string deepPath = Path.Combine(_testDir, "sub1", "sub2", "sub3", "nested.txt");

        AtomicFile.WriteAllText(deepPath, "nested content");

        Assert.True(File.Exists(deepPath));
        Assert.Equal("nested content", File.ReadAllText(deepPath));
    }

    [Fact]
    public void MetadataBackupService_CorruptManifest_LogsWarningAndReturnsEmpty()
    {
        string backupDir = Path.Combine(_testDir, "backups");
        Directory.CreateDirectory(backupDir);
        string manifestPath = Path.Combine(backupDir, "backups_manifest.json");
        File.WriteAllText(manifestPath, "THIS IS CORRUPTED JSON {{{");

        string logPath = Path.Combine(_testDir, "app.log");
        AppLogger.Initialize(logPath);

        var service = new MetadataBackupService(backupDir);
        var backups = service.ListBackups("nonexistent");

        Assert.Empty(backups);
        Assert.True(File.Exists(logPath));
        string logContent = File.ReadAllText(logPath);
        Assert.Contains("[WARN]", logContent);
        Assert.Contains("[MetadataBackupService] Failed to load manifest", logContent);
    }

    [Fact]
    public void ScraperCacheService_CorruptCache_LogsWarningAndInitializesEmpty()
    {
        string cacheFile = Path.Combine(_testDir, "corrupt_cache.json");
        File.WriteAllText(cacheFile, "INVALID JSON DATA NOT AN ARRAY");

        string logPath = Path.Combine(_testDir, "app_cache.log");
        AppLogger.Initialize(logPath);

        using var cacheService = new ScraperCacheService(cacheFile);

        Assert.Null(cacheService.Get("any_key", TimeSpan.FromDays(1)));
        Assert.True(File.Exists(logPath));
        string logContent = File.ReadAllText(logPath);
        Assert.Contains("[WARN]", logContent);
        Assert.Contains("[ScraperCacheService] Failed to load cache", logContent);
    }
}
