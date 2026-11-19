using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using InkTag.Core;
using InkTag.Core.Exceptions;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using Xunit;

namespace InkTag.Tests;

public class ArchiveRepackStructureTests : IDisposable
{
    private readonly string _testDir;

    public ArchiveRepackStructureTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "InkTagNestedArchiveTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    private string CreateNestedTestCbz(string cbzName, Dictionary<string, byte[]> entries)
    {
        string cbzPath = Path.Combine(_testDir, cbzName);
        using var stream = File.OpenWrite(cbzPath);
        using var writer = new SharpCompress.Writers.Zip.ZipWriter(stream, new SharpCompress.Writers.Zip.ZipWriterOptions(SharpCompress.Common.CompressionType.Deflate));

        foreach (var (entryPath, data) in entries)
        {
            string tempFile = Path.Combine(_testDir, Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(tempFile, data);
            writer.Write(entryPath, tempFile);
            File.Delete(tempFile);
        }

        return cbzPath;
    }

    private static byte[] SampleImageBytes => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    [Fact]
    public void EditMetadata_PreservesNestedDirectoryStructure()
    {
        var editor = new MetadataEditor();
        var entries = new Dictionary<string, byte[]>
        {
            ["Chapter 1/01.jpg"] = SampleImageBytes,
            ["Chapter 1/02.jpg"] = SampleImageBytes,
            ["Chapter 2/01.jpg"] = SampleImageBytes,
            ["Chapter 2/02.jpg"] = SampleImageBytes,
            ["ComicInfo.xml"] = Encoding.UTF8.GetBytes("<ComicInfo><Title>Original Title</Title><Series>Epic Saga</Series></ComicInfo>")
        };

        string cbzPath = CreateNestedTestCbz("nested_edit.cbz", entries);

        // Edit metadata
        editor.EditMetadata(cbzPath, comic =>
        {
            comic.Title = "Updated Title";
            comic.Writer = "Neil Gaiman";
        });

        // Verify metadata was updated
        var updated = editor.ReadMetadata(cbzPath);
        Assert.Equal("Updated Title", updated.Title);
        Assert.Equal("Epic Saga", updated.Series);
        Assert.Equal("Neil Gaiman", updated.Writer);

        // Verify all entry paths inside the archive preserved their nested structure
        using var zip = ZipFile.OpenRead(cbzPath);
        var entryKeys = zip.Entries.Select(e => e.FullName).OrderBy(k => k).ToList();

        Assert.Contains("Chapter 1/01.jpg", entryKeys);
        Assert.Contains("Chapter 1/02.jpg", entryKeys);
        Assert.Contains("Chapter 2/01.jpg", entryKeys);
        Assert.Contains("Chapter 2/02.jpg", entryKeys);
        Assert.Contains("ComicInfo.xml", entryKeys);

        // Ensure no flattened files exist in the root
        Assert.DoesNotContain("01.jpg", entryKeys);
        Assert.DoesNotContain("02.jpg", entryKeys);
        Assert.Equal(5, entryKeys.Count);
    }

    [Fact]
    public void UpdateMetadataXml_PreservesNestedDirectoryStructure()
    {
        var editor = new MetadataEditor();
        var entries = new Dictionary<string, byte[]>
        {
            ["Vol1/ChapterA/p01.png"] = SampleImageBytes,
            ["Vol1/ChapterA/p02.png"] = SampleImageBytes,
            ["Vol1/ChapterB/p01.png"] = SampleImageBytes,
            ["ComicInfo.xml"] = Encoding.UTF8.GetBytes("<ComicInfo><Title>XML Edit Test</Title></ComicInfo>")
        };

        string cbzPath = CreateNestedTestCbz("nested_update_xml.cbz", entries);

        string newXml = "<ComicInfo><Title>New XML Title</Title><Series>New Series</Series></ComicInfo>";
        editor.UpdateMetadataXml(cbzPath, newXml);

        var updated = editor.ReadMetadata(cbzPath);
        Assert.Equal("New XML Title", updated.Title);
        Assert.Equal("New Series", updated.Series);

        using var zip = ZipFile.OpenRead(cbzPath);
        var entryKeys = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("Vol1/ChapterA/p01.png", entryKeys);
        Assert.Contains("Vol1/ChapterA/p02.png", entryKeys);
        Assert.Contains("Vol1/ChapterB/p01.png", entryKeys);
        Assert.Contains("ComicInfo.xml", entryKeys);
        Assert.Equal(4, entryKeys.Count);
    }

    [Fact]
    public void RemoveArchivePages_HandlesNestedDirectoriesWithoutFlatteningOrDroppingPages()
    {
        var editor = new MetadataEditor();
        var entries = new Dictionary<string, byte[]>
        {
            ["Part1/page01.jpg"] = SampleImageBytes,
            ["Part1/page02.jpg"] = SampleImageBytes,
            ["Part2/page01.jpg"] = SampleImageBytes,
            ["Part2/page02.jpg"] = SampleImageBytes,
            ["ComicInfo.xml"] = Encoding.UTF8.GetBytes("<ComicInfo><Title>Remove Page Test</Title><PageCount>4</PageCount></ComicInfo>")
        };

        string cbzPath = CreateNestedTestCbz("nested_remove_page.cbz", entries);

        // Remove the first page (index 0 -> Part1/page01.jpg)
        var result = editor.RemoveArchivePages(cbzPath, new[] { 0 });

        Assert.True(result.Success);
        Assert.Equal(4, result.OriginalPageCount);
        Assert.Equal(3, result.FinalPageCount);
        Assert.Single(result.RemovedEntries);
        Assert.Equal("Part1/page01.jpg", result.RemovedEntries[0]);

        // Inspect repacked archive
        using var zip = ZipFile.OpenRead(cbzPath);
        var entryKeys = zip.Entries.Select(e => e.FullName).OrderBy(k => k).ToList();

        // Part1/page01.jpg should be gone
        Assert.DoesNotContain("Part1/page01.jpg", entryKeys);

        // Remaining nested entries must be intact with full relative paths preserved
        Assert.Contains("Part1/page02.jpg", entryKeys);
        Assert.Contains("Part2/page01.jpg", entryKeys);
        Assert.Contains("Part2/page02.jpg", entryKeys);
        Assert.Contains("ComicInfo.xml", entryKeys);
        Assert.Equal(4, entryKeys.Count);

        // Metadata PageCount should be updated
        var updated = editor.ReadMetadata(cbzPath);
        Assert.Equal(3, updated.PageCount);
    }

    [Fact]
    public void RemoveArchivePages_FindsSubfolderComicInfoXmlAndRelocatesToRoot()
    {
        var editor = new MetadataEditor();
        var entries = new Dictionary<string, byte[]>
        {
            ["Subfolder/page01.jpg"] = SampleImageBytes,
            ["Subfolder/page02.jpg"] = SampleImageBytes,
            ["Subfolder/ComicInfo.xml"] = Encoding.UTF8.GetBytes("<ComicInfo><Title>Subfolder ComicInfo</Title><PageCount>2</PageCount></ComicInfo>")
        };

        string cbzPath = CreateNestedTestCbz("subfolder_xml.cbz", entries);

        var result = editor.RemoveArchivePages(cbzPath, new[] { 0 });

        Assert.True(result.Success);
        Assert.Equal(1, result.FinalPageCount);

        using var zip = ZipFile.OpenRead(cbzPath);
        var entryKeys = zip.Entries.Select(e => e.FullName).ToList();

        // ComicInfo.xml must now be at the root
        Assert.Contains("ComicInfo.xml", entryKeys);
        // Subfolder ComicInfo.xml must not duplicate
        Assert.DoesNotContain("Subfolder/ComicInfo.xml", entryKeys);
        Assert.Contains("Subfolder/page02.jpg", entryKeys);

        var updated = editor.ReadMetadata(cbzPath);
        Assert.Equal("Subfolder ComicInfo", updated.Title);
        Assert.Equal(1, updated.PageCount);
    }

    [Theory]
    [InlineData("../evil.txt", true)]
    [InlineData("..\\evil.txt", true)]
    [InlineData("sub/../../evil.txt", true)]
    [InlineData("/absolute/evil.txt", true)]
    [InlineData("\\absolute\\evil.txt", true)]
    [InlineData("C:\\Windows\\evil.txt", true)]
    [InlineData("Chapter 1/01.jpg", false)]
    [InlineData("01.jpg", false)]
    [InlineData("sub/folder/nested/01.png", false)]
    public void ArchiveSecurity_IsUnsafeEntryKey_IdentifiesMaliciousPaths(string key, bool expectedUnsafe)
    {
        bool isUnsafe = InkTag.Core.IO.ArchiveSecurity.IsUnsafeEntryKey(key, _testDir);
        Assert.Equal(expectedUnsafe, isUnsafe);
    }

    [Fact]
    public void ArchiveOperations_RejectZipSlipEntry()
    {
        var editor = new MetadataEditor();
        string tempCbz = Path.Combine(_testDir, "zipslip_" + Guid.NewGuid().ToString("N") + ".cbz");
        string tempContent = Path.Combine(_testDir, "payload.txt");
        File.WriteAllText(tempContent, "malicious content");

        using (var stream = File.OpenWrite(tempCbz))
        using (var writer = new SharpCompress.Writers.Zip.ZipWriter(stream, new SharpCompress.Writers.Zip.ZipWriterOptions(SharpCompress.Common.CompressionType.Deflate)))
        {
            writer.Write("01.jpg", tempContent);
            writer.Write("../evil.txt", tempContent);
        }

        // EditMetadata must throw UnsafeArchiveEntryException
        var ex = Assert.Throws<UnsafeArchiveEntryException>(() =>
        {
            editor.EditMetadata(tempCbz, comic => comic.Title = "Test");
        });
        Assert.Equal("../evil.txt", ex.EntryName);

        // UpdateMetadataXml must throw UnsafeArchiveEntryException
        var exUpdate = Assert.Throws<UnsafeArchiveEntryException>(() =>
        {
            editor.UpdateMetadataXml(tempCbz, "<ComicInfo/>");
        });
        Assert.Equal("../evil.txt", exUpdate.EntryName);

        // RemoveArchivePages must fail safely and report invalid path traversal
        var removeResult = editor.RemoveArchivePages(tempCbz, new[] { 0 });
        Assert.False(removeResult.Success);
        Assert.Contains("invalid path traversal", removeResult.ErrorMessage);
    }
}
