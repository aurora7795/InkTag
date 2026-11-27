using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using InkTag.Core;
using InkTag.Core.Backup;
using InkTag.Mcp;
using Xunit;

namespace InkTag.Tests;

[Collection("ProcessStateTests")]
public class McpSecurityAndBackupTests : IDisposable
{
    private readonly string _testBackupDir;

    public McpSecurityAndBackupTests()
    {
        _testBackupDir = Path.Combine(Path.GetTempPath(), "InkTagBackupTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBackupDir);
        MetadataBackupService.SetGlobalCustomBackupDirectory(_testBackupDir);
        ComicTools.ReadOnlyOverride = null;
        ComicTools.AllowedRootsOverride = null;
    }

    public void Dispose()
    {
        ComicTools.ReadOnlyOverride = null;
        ComicTools.AllowedRootsOverride = null;
        MetadataBackupService.SetGlobalCustomBackupDirectory(null);
        if (Directory.Exists(_testBackupDir))
        {
            try { Directory.Delete(_testBackupDir, true); } catch { }
        }
    }

    private string CreateTestCbz(string comicInfoXml = "<ComicInfo><Title>Initial Title</Title><Series>Initial Series</Series></ComicInfo>")
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        string imagePath = Path.Combine(tempDir, "001.jpg");
        File.WriteAllBytes(imagePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 });

        string xmlPath = Path.Combine(tempDir, "ComicInfo.xml");
        File.WriteAllText(xmlPath, comicInfoXml);

        string cbzPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cbz");
        ZipFile.CreateFromDirectory(tempDir, cbzPath);
        Directory.Delete(tempDir, true);

        return cbzPath;
    }

    [Fact]
    public void ReadOnlyMode_ThrowsUnauthorizedAccessException_OnMutatingWrites()
    {
        string cbz = CreateTestCbz();
        try
        {
            ComicTools.ReadOnlyOverride = true;

            using var doc = JsonDocument.Parse("{\"Writer\": \"Hacker\"}");
            
            // Should throw when dryRun is false
            var ex1 = Assert.Throws<UnauthorizedAccessException>(() =>
                ComicTools.UpdateComicMetadata(cbz, doc.RootElement, dryRun: false));
            Assert.Contains("strict READ-ONLY mode", ex1.Message);

            var ex2 = Assert.Throws<UnauthorizedAccessException>(() =>
                ComicTools.RenameComicFiles(cbz, dryRun: false));
            Assert.Contains("strict READ-ONLY mode", ex2.Message);

            var ex3 = Assert.Throws<UnauthorizedAccessException>(() =>
                ComicTools.RestoreComicBackup(cbz));
            Assert.Contains("strict READ-ONLY mode", ex3.Message);

            // But dry-run should still be allowed
            string preview = ComicTools.UpdateComicMetadata(cbz, doc.RootElement, dryRun: true);
            Assert.Contains("Writer", preview);
        }
        finally
        {
            ComicTools.ReadOnlyOverride = null;
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public void RemoveComicPage_ReadOnlyMode_ThrowsUnauthorizedAccessException_WhenCommitting()
    {
        string cbz = CreateTestCbz();
        try
        {
            ComicTools.ReadOnlyOverride = true;

            var ex = Assert.Throws<UnauthorizedAccessException>(() =>
                ComicTools.RemoveComicPage(cbz, pageIndex: 0, dryRun: false));
            Assert.Contains("strict READ-ONLY mode", ex.Message);

            // Dry-run preview must still be allowed in read-only mode.
            string preview = ComicTools.RemoveComicPage(cbz, pageIndex: 0, dryRun: true);
            Assert.Contains("\"dryRun\": true", preview);
        }
        finally
        {
            ComicTools.ReadOnlyOverride = null;
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public void RemoveComicPage_DefaultsToDryRun_WithoutModifyingArchive()
    {
        string cbz = CreateTestCbz();
        try
        {
            var editor = new MetadataEditor();
            int originalPageCount = editor.GetImageEntries(cbz).Count;

            // Invoke without specifying dryRun (should default to dryRun: true).
            string result = ComicTools.RemoveComicPage(cbz, pageIndex: 0);
            Assert.Contains("\"dryRun\": true", result);

            Assert.Equal(originalPageCount, editor.GetImageEntries(cbz).Count);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public void UpdateComicMetadata_DefaultsToDryRun_WithoutModifyingFile()
    {
        string cbz = CreateTestCbz("<ComicInfo><Title>Untouched Title</Title></ComicInfo>");
        try
        {
            using var doc = JsonDocument.Parse("{\"Title\": \"Changed Title\"}");
            
            // Invoke without specifying dryRun parameter (should default to dryRun: true)
            string result = ComicTools.UpdateComicMetadata(cbz, doc.RootElement);
            Assert.Contains("Changed Title", result);

            // Verify file was NOT modified
            var editor = new MetadataEditor();
            var metadata = editor.ReadMetadata(cbz);
            Assert.Equal("Untouched Title", metadata.Title);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public void PreWriteBackup_CreatesSnapshotAndAllowsRestore()
    {
        string cbz = CreateTestCbz("<ComicInfo><Title>Original Title</Title><Writer>Original Writer</Writer></ComicInfo>");
        try
        {
            // 1. Perform a write edit
            var editor = new MetadataEditor();
            editor.EditMetadata(cbz, info =>
            {
                info.Title = "Overwritten Title";
                info.Writer = "Overwritten Writer";
            });

            // Verify edit applied
            var edited = editor.ReadMetadata(cbz);
            Assert.Equal("Overwritten Title", edited.Title);

            // 2. Query backup service
            var backupService = new MetadataBackupService(_testBackupDir);
            var backups = backupService.ListBackups(cbz);
            Assert.NotEmpty(backups);

            // 3. Restore backup
            bool restored = backupService.RestoreBackup(cbz, backups[0].Id);
            Assert.True(restored);

            // 4. Verify original metadata restored
            var reverted = editor.ReadMetadata(cbz);
            Assert.Equal("Original Title", reverted.Title);
            Assert.Equal("Original Writer", reverted.Writer);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public void RestoreComicBackup_McpTool_RestoresMetadataSuccessfully()
    {
        string cbz = CreateTestCbz("<ComicInfo><Title>Initial Title</Title><Series>Initial Series</Series></ComicInfo>");
        try
        {
            // Edit via MCP tool with explicit dryRun: false
            using var doc = JsonDocument.Parse("{\"Title\": \"Malicious Injected Title\"}");
            ComicTools.UpdateComicMetadata(cbz, doc.RootElement, dryRun: false);

            var editor = new MetadataEditor();
            var modified = editor.ReadMetadata(cbz);
            Assert.Equal("Malicious Injected Title", modified.Title);

            // List backups via MCP tool
            string listJson = ComicTools.ListMetadataBackups(cbz);
            Assert.Contains("backupCount", listJson);

            // Restore via MCP tool
            string restoreJson = ComicTools.RestoreComicBackup(cbz);
            Assert.Contains("\"success\": true", restoreJson);

            // Verify restored
            var restored = editor.ReadMetadata(cbz);
            Assert.Equal("Initial Title", restored.Title);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
<<<<<<< HEAD
    public void ValidatePathAccess_RejectsEmptyOrWhitespace_AndDoesNotLeakAllowedRoots()
    {
        Assert.Throws<ArgumentException>(() => ComicTools.ValidatePathAccess(""));
        Assert.Throws<ArgumentException>(() => ComicTools.ValidatePathAccess("   "));

        string secretRoot = Path.Combine(Path.GetTempPath(), "SecretAllowedRoot_" + Guid.NewGuid().ToString("N"));
        ComicTools.AllowedRootsOverride = new List<string> { secretRoot };

        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
            ComicTools.ValidatePathAccess(Path.Combine(Path.GetTempPath(), "other.cbz")));
        Assert.DoesNotContain(secretRoot, ex.Message);
    }

    [Fact]
    public void ValidatePathAccess_BlocksSymlinkEscapingAllowedRoot()
    {
        string allowedDir = Path.Combine(Path.GetTempPath(), "InkTagAllowed_" + Guid.NewGuid().ToString("N"));
        string outsideFile = Path.Combine(Path.GetTempPath(), "InkTagOutside_" + Guid.NewGuid().ToString("N") + ".txt");
        Directory.CreateDirectory(allowedDir);
        File.WriteAllText(outsideFile, "secret");

        string symlinkPath = Path.Combine(allowedDir, "escape.txt");
        try
        {
            File.CreateSymbolicLink(symlinkPath, outsideFile);
            ComicTools.AllowedRootsOverride = new List<string> { allowedDir };

            Assert.Throws<UnauthorizedAccessException>(() => ComicTools.ValidatePathAccess(symlinkPath));
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            // Symlink creation on Windows without Developer Mode requires elevation; skip gracefully.
        }
        finally
        {
            if (File.Exists(symlinkPath)) File.Delete(symlinkPath);
            if (Directory.Exists(allowedDir)) Directory.Delete(allowedDir, true);
            if (File.Exists(outsideFile)) File.Delete(outsideFile);
        }
    }

    [Fact]
    public void ExtractCoverImage_ReadOnlyMode_BlocksDiskWrite_AndAllowsBase64InMemory()
    {
        string cbz = CreateTestCbz();
        string outImg = Path.Combine(Path.GetTempPath(), $"out_{Guid.NewGuid():N}.jpg");
        try
        {
            ComicTools.ReadOnlyOverride = true;

            Assert.Throws<UnauthorizedAccessException>(() =>
                ComicTools.ExtractCoverImage(cbz, pageIndex: 0, outputPath: outImg, returnBase64: false));
            Assert.Throws<UnauthorizedAccessException>(() =>
                ComicTools.ExtractCoverImage(cbz, pageIndex: 0, outputPath: null, returnBase64: false));

            // In-memory vision inspection (no outputPath, returnBase64: true) must still work in read-only mode
            var result = ComicTools.ExtractCoverImage(cbz, pageIndex: 0, outputPath: null, returnBase64: true);
            Assert.NotNull(result);
            Assert.False(File.Exists(outImg));
        }
        finally
        {
            ComicTools.ReadOnlyOverride = null;
            if (File.Exists(cbz)) File.Delete(cbz);
            if (File.Exists(outImg)) File.Delete(outImg);
        }
    }

    [Fact]
    public void RestoreBatchJob_ThrowsUnauthorizedAccessException_WhenAffectedFileOutsideAllowedRoots()
    {
        string allowedDir = Path.Combine(Path.GetTempPath(), "InkTagAllowedBatch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(allowedDir);
        string cbzOutside = CreateTestCbz();
        try
        {
            var backupService = new MetadataBackupService(_testBackupDir);
            string batchId = "batch_sec_test_" + Guid.NewGuid().ToString("N");
            backupService.CreateBackup(cbzOutside, "<ComicInfo/>", "Test", batchJobId: batchId);

            ComicTools.AllowedRootsOverride = new List<string> { allowedDir };

            // ListBatchJobs must not leak the outside file
            string listJson = ComicTools.ListBatchJobs();
            Assert.DoesNotContain(Path.GetFileName(cbzOutside), listJson);

            // RestoreBatchJob must reject restoring files outside allowed roots
            Assert.Throws<UnauthorizedAccessException>(() => ComicTools.RestoreBatchJob(batchId));
        }
        finally
        {
            if (Directory.Exists(allowedDir)) Directory.Delete(allowedDir, true);
            if (File.Exists(cbzOutside)) File.Delete(cbzOutside);
        }
    }

    [Fact]
    public void ListMetadataBackups_FiltersOutBackupsOutsideAllowedRoots()
    {
        string allowedDir = Path.Combine(Path.GetTempPath(), "InkTagAllowedList_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(allowedDir);
        string cbzOutside = CreateTestCbz();
        try
        {
            var backupService = new MetadataBackupService(_testBackupDir);
            backupService.CreateBackup(cbzOutside, "<ComicInfo/>", "Test");

            ComicTools.AllowedRootsOverride = new List<string> { allowedDir };
            string json = ComicTools.ListMetadataBackups();
            Assert.DoesNotContain(Path.GetFileName(cbzOutside), json);
        }
        finally
        {
            if (Directory.Exists(allowedDir)) Directory.Delete(allowedDir, true);
            if (File.Exists(cbzOutside)) File.Delete(cbzOutside);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckKomgaServer_RejectsMetadataIpAndDisallowedScheme()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ComicTools.CheckKomgaServer(serverUrl: "http://169.254.169.254/latest/meta-data/"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ComicTools.CheckKomgaServer(serverUrl: "file:///etc/passwd"));
    }

    [Fact]
    public void ComicFileRenamer_RenameFile_RejectsPathTraversal()
    {
        string cbz = CreateTestCbz();
        try
        {
            Assert.Throws<ArgumentException>(() =>
                InkTag.Core.Renaming.ComicFileRenamer.RenameFile(cbz, "../escaped.cbz"));
            Assert.Throws<ArgumentException>(() =>
                InkTag.Core.Renaming.ComicFileRenamer.RenameFile(cbz, "sub/escaped.cbz"));
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public void ArchiveSecurity_CopyStreamWithLimit_ThrowsWhenExceeded()
    {
        using var src = new MemoryStream(new byte[1024]);
        using var dst = new MemoryStream();
        Assert.Throws<InvalidDataException>(() =>
            InkTag.Core.IO.ArchiveSecurity.CopyStreamWithLimit(src, dst, 512, "page1.jpg", "test.cbz"));
    }

    [Fact]
    public void RemoveComicPage_DryRun_OutOfBoundsPageIndex_ReturnsSuccessFalse()
    {
        string cbz = CreateTestCbz();
        try
        {
            string json = ComicTools.RemoveComicPage(cbz, pageIndex: 99, dryRun: true);
            using var doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("finalPageCount").GetInt32());
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ScrapeComicMetadata_InvalidMode_ThrowsArgumentException()
    {
        string cbz = CreateTestCbz();
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                ComicTools.ScrapeComicMetadata(cbz, mode: "invalid-mode"));
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }
}
