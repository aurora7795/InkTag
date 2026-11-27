using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using InkTag.Core.Backup;
using InkTag.Core.Logging;
using Xunit;

namespace InkTag.Tests;

[Collection("ProcessStateTests")]
public class MetadataBackupJournalTests : IDisposable
{
    private readonly string _testBackupDir;

    public MetadataBackupJournalTests()
    {
        _testBackupDir = Path.Combine(Path.GetTempPath(), "InkTagJournalTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBackupDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBackupDir))
        {
            try { Directory.Delete(_testBackupDir, true); } catch { }
        }
    }

    [Fact]
    public void CreateBackup_AppendsToJournalWithoutRewritingManifestEachTime()
    {
        var service = new MetadataBackupService(_testBackupDir);

        Assert.False(File.Exists(service.ManifestPath));
        Assert.False(File.Exists(service.JournalPath));

        var entry1 = service.CreateBackup(
            archivePath: "/comics/Action Comics #001.cbz",
            originalXml: "<ComicInfo><Title>Issue 1</Title></ComicInfo>",
            operationType: "Tagging",
            batchJobId: "batch_1");

        Assert.NotNull(entry1);
        Assert.True(File.Exists(service.JournalPath), "Journal file should exist after CreateBackup");
        Assert.False(File.Exists(service.ManifestPath), "Manifest file should not be created before threshold or flush");

        string[] lines = File.ReadAllLines(service.JournalPath);
        Assert.Single(lines);
        Assert.Contains(entry1.Id, lines[0]);

        var entry2 = service.CreateBackup(
            archivePath: "/comics/Action Comics #002.cbz",
            originalXml: "<ComicInfo><Title>Issue 2</Title></ComicInfo>",
            operationType: "Tagging",
            batchJobId: "batch_1");

        Assert.NotNull(entry2);
        Assert.False(File.Exists(service.ManifestPath), "Manifest file should still not exist");

        lines = File.ReadAllLines(service.JournalPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains(entry2.Id, lines[1]);
    }

    [Fact]
    public void Flush_CompactsJournalIntoManifestAndDeletesJournal()
    {
        var service = new MetadataBackupService(_testBackupDir);

        var entry1 = service.CreateBackup(
            archivePath: "/comics/Batman #001.cbz",
            originalXml: "<ComicInfo><Title>Batman 1</Title></ComicInfo>",
            operationType: "Scrape");
        var entry2 = service.CreateBackup(
            archivePath: "/comics/Batman #002.cbz",
            originalXml: "<ComicInfo><Title>Batman 2</Title></ComicInfo>",
            operationType: "Scrape");

        Assert.True(File.Exists(service.JournalPath));
        Assert.False(File.Exists(service.ManifestPath));

        service.Flush();

        Assert.False(File.Exists(service.JournalPath), "Journal should be deleted after Flush");
        Assert.True(File.Exists(service.ManifestPath), "Manifest should be written after Flush");

        string json = File.ReadAllText(service.ManifestPath);
        var entries = JsonSerializer.Deserialize<List<MetadataBackupEntry>>(json);
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);
        // Newest entry (entry2) should be at index 0
        Assert.Equal(entry2!.Id, entries[0].Id);
        Assert.Equal(entry1!.Id, entries[1].Id);
    }

    [Fact]
    public void ReadOperations_AutomaticallyReconcileAndCompactJournal()
    {
        var service = new MetadataBackupService(_testBackupDir);

        var entry = service.CreateBackup(
            archivePath: "/comics/Spider-Man #001.cbz",
            originalXml: "<ComicInfo><Title>Spidey 1</Title></ComicInfo>",
            operationType: "Edit",
            changeReason: "Corrected series name",
            coverDHash: "DEADBEEF01234567");

        Assert.True(File.Exists(service.JournalPath));
        Assert.False(File.Exists(service.ManifestPath));

        // Calling ListBackups should trigger reconciliation and compaction
        var backups = service.ListBackups();

        Assert.Single(backups);
        Assert.Equal(entry!.Id, backups[0].Id);
        Assert.Equal("Corrected series name", backups[0].ChangeReason);
        Assert.Equal("DEADBEEF01234567", backups[0].CoverDHash);

        Assert.False(File.Exists(service.JournalPath), "Journal should be compacted after query");
        Assert.True(File.Exists(service.ManifestPath), "Manifest should now exist");

        // Verify GetBackupEntry also finds the record
        var found = service.GetBackupEntry(entry.Id);
        Assert.NotNull(found);
        Assert.Equal(entry.Id, found.Id);

        // Verify GetBackupXml finds snapshot
        string? xml = service.GetBackupXml(entry.Id);
        Assert.Equal("<ComicInfo><Title>Spidey 1</Title></ComicInfo>", xml);
    }

    [Fact]
    public void AutomaticCompaction_TriggersWhenThresholdReached()
    {
        var service = new MetadataBackupService(_testBackupDir)
        {
            JournalCompactionThreshold = 3
        };

        service.CreateBackup("/comics/X-Men #001.cbz", "<ComicInfo/>", "Scrape");
        Assert.True(File.Exists(service.JournalPath));
        Assert.False(File.Exists(service.ManifestPath));

        service.CreateBackup("/comics/X-Men #002.cbz", "<ComicInfo/>", "Scrape");
        Assert.True(File.Exists(service.JournalPath));
        Assert.False(File.Exists(service.ManifestPath));

        // 3rd backup hits threshold -> automatic compaction
        service.CreateBackup("/comics/X-Men #003.cbz", "<ComicInfo/>", "Scrape");
        Assert.False(File.Exists(service.JournalPath), "Journal should be compacted upon hitting threshold");
        Assert.True(File.Exists(service.ManifestPath), "Manifest should be written upon hitting threshold");

        var backups = service.ListBackups();
        Assert.Equal(3, backups.Count);
    }

    [Fact]
    public void CorruptJournalLine_SkippedWithWarningAndValidEntriesPreserved()
    {
        string logPath = Path.Combine(_testBackupDir, "test.log");
        AppLogger.Initialize(logPath);

        var service = new MetadataBackupService(_testBackupDir);

        var entry1 = new MetadataBackupEntry(
            Id: "valid001",
            ArchivePath: "/comics/Valid1.cbz",
            Timestamp: DateTime.UtcNow.ToString("o"),
            OperationType: "Tag",
            OriginalFileName: "Valid1.cbz",
            BackupXmlFileName: "hash_valid001.xml"
        );
        var entry2 = new MetadataBackupEntry(
            Id: "valid002",
            ArchivePath: "/comics/Valid2.cbz",
            Timestamp: DateTime.UtcNow.ToString("o"),
            OperationType: "Tag",
            OriginalFileName: "Valid2.cbz",
            BackupXmlFileName: "hash_valid002.xml"
        );

        string line1 = JsonSerializer.Serialize(entry1);
        string lineCorrupt = "{\"Id\": \"corrupt\", \"ArchivePath\": UNPARSED_GARBAGE {{{";
        string line2 = JsonSerializer.Serialize(entry2);

        File.WriteAllLines(service.JournalPath, new[] { line1, lineCorrupt, line2 }, Encoding.UTF8);

        // ListBackups triggers compaction
        var backups = service.ListBackups();

        Assert.Equal(2, backups.Count);
        Assert.Contains(backups, b => b.Id == "valid001");
        Assert.Contains(backups, b => b.Id == "valid002");

        string logs = ReadSharedLog(logPath);
        Assert.Contains("[MetadataBackupService] Failed to parse journal entry", logs);
    }

    private static string ReadSharedLog(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    [Fact]
    public void Compaction_Enforces1000ItemCapAndDeletesExcessXmls()
    {
        var service = new MetadataBackupService(_testBackupDir);

        // Seed manifest with 998 existing entries
        var seeded = new List<MetadataBackupEntry>();
        for (int i = 0; i < 998; i++)
        {
            string xmlName = $"seed_{i}.xml";
            string xmlPath = Path.Combine(_testBackupDir, xmlName);
            File.WriteAllText(xmlPath, $"<ComicInfo><Title>Seed {i}</Title></ComicInfo>");

            seeded.Add(new MetadataBackupEntry(
                Id: $"seed_{i:D4}",
                ArchivePath: $"/comics/Seed_{i}.cbz",
                Timestamp: DateTime.UtcNow.AddMinutes(-1000 + i).ToString("o"),
                OperationType: "Seed",
                OriginalFileName: $"Seed_{i}.cbz",
                BackupXmlFileName: xmlName
            ));
        }

        // Save manifest directly
        string seedJson = JsonSerializer.Serialize(seeded, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(service.ManifestPath, seedJson);

        // Add 5 new backups through CreateBackup (total 998 + 5 = 1003)
        for (int i = 0; i < 5; i++)
        {
            service.CreateBackup($"/comics/New_{i}.cbz", $"<ComicInfo><Title>New {i}</Title></ComicInfo>", "NewTag");
        }

        // Compact
        service.Flush();

        var backups = service.ListBackups(limit: 2000);
        Assert.Equal(1000, backups.Count);

        // The 3 oldest seeded backups (seed_997, seed_996, seed_995 in reversed order, or seed_0000, seed_0001, seed_0002)
        // Since seed_0000 was oldest in timestamp (AddMinutes(-1000)), seeded entries were at the end of seeded list.
        // The excess 3 at the tail should be deleted.
        int deletedXmlCount = 0;
        for (int i = 0; i < 998; i++)
        {
            string xmlPath = Path.Combine(_testBackupDir, $"seed_{i}.xml");
            if (!File.Exists(xmlPath))
            {
                deletedXmlCount++;
            }
        }

        Assert.Equal(3, deletedXmlCount);
    }

    [Fact]
    public void Deduplication_PreservesNewestWithoutDuplicatesIfJournalAndManifestOverlap()
    {
        var service = new MetadataBackupService(_testBackupDir);

        var entry = service.CreateBackup("/comics/Overlap.cbz", "<ComicInfo/>", "OverlapTest");
        Assert.NotNull(entry);

        // Manually flush to manifest
        service.Flush();
        Assert.True(File.Exists(service.ManifestPath));

        // Simulate crash recovery where journal still had the entry appended
        File.WriteAllText(service.JournalPath, JsonSerializer.Serialize(entry) + Environment.NewLine);

        // ListBackups reconciles
        var backups = service.ListBackups();
        Assert.Single(backups);
        Assert.Equal(entry.Id, backups[0].Id);
    }
}
