using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using InkTag.Core;
using Xunit;

namespace InkTag.Tests;

public class CoreBugFixTests
{
    [Fact]
    public void ApplyJsonPatch_PreservesDecimalCommunityRating()
    {
        var info = new ComicInfo { Title = "Test" };

        MetadataEditor.ApplyJsonPatch(info, "{\"CommunityRating\": 4.5}");

        Assert.Equal(4.5m, info.CommunityRating);
    }

    [Fact]
    public void SanitizeComicInfoXml_PreservesSurrogatePairCharacters()
    {
        string xml = "<ComicInfo><Title>Hero \U0001F680 Launch</Title></ComicInfo>";
        string sanitized = ComicInfoXmlSanitizer.SanitizeComicInfoXml(xml);
        Assert.Contains("\U0001F680", sanitized);
    }

    [Fact]
    public void GetImageEntries_SortsByNormalizedRelativePath_MatchingRemoveArchivePages()
    {
        string cbzPath = Path.Combine(Path.GetTempPath(), $"sort_{Guid.NewGuid():N}.cbz");
        try
        {
            using (var zip = ZipFile.Open(cbzPath, ZipArchiveMode.Create))
            {
                zip.CreateEntry("ch2/001.jpg");
                zip.CreateEntry("ch1/002.jpg");
                zip.CreateEntry("ch1/001.jpg");
            }

            var editor = new MetadataEditor();
            var entries = editor.GetImageEntries(cbzPath);

            Assert.Equal(new[] { "ch1/001.jpg", "ch1/002.jpg", "ch2/001.jpg" }, entries);
        }
        finally
        {
            if (File.Exists(cbzPath)) File.Delete(cbzPath);
        }
    }

    [Fact]
    public void EditMetadata_CbrConversion_ThrowsIfTargetCbzAlreadyExists()
    {
        string basePath = Path.Combine(Path.GetTempPath(), $"collision_{Guid.NewGuid():N}");
        string cbrPath = basePath + ".cbr";
        string cbzPath = basePath + ".cbz";
        try
        {
            File.WriteAllBytes(cbrPath, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(cbzPath, new byte[] { 4, 5, 6 });

            var editor = new MetadataEditor();
            Assert.Throws<IOException>(() => editor.EditMetadata(cbrPath, info => info.Title = "Updated"));
            // Pre-existing CBZ must remain untouched
            Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(cbzPath));
        }
        finally
        {
            if (File.Exists(cbrPath)) File.Delete(cbrPath);
            if (File.Exists(cbzPath)) File.Delete(cbzPath);
        }
    }
}
