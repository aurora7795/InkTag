using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using InkTag.Cli;
using InkTag.Core;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using SkiaSharp;
using Xunit;

namespace InkTag.Tests;

public class CliAppTests
{
    private string CreateTestCbz(string filename, string? title = "Test Title", string? series = "Test Series", string? number = "1")
    {
        string cbzPath = Path.Combine(Path.GetTempPath(), filename);
        string imgPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");

        try
        {
            using (var bitmap = new SKBitmap(64, 96))
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);
                using var paint = new SKPaint { Color = SKColors.Black };
                canvas.DrawRect(32, 0, 32, 96, paint);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
                File.WriteAllBytes(imgPath, data.ToArray());
            }

            using (var fs = File.OpenWrite(cbzPath))
            using (var writer = new ZipWriter(fs, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("001_cover.jpg", imgPath);
                writer.Write("002_page.jpg", imgPath);

                if (title != null || series != null || number != null)
                {
                    var comicInfo = new ComicInfo { Title = title, Series = series, Number = number };
                    string xmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");
                    using (var xmlFs = File.OpenWrite(xmlPath))
                    {
                        new System.Xml.Serialization.XmlSerializer(typeof(ComicInfo)).Serialize(xmlFs, comicInfo);
                    }
                    writer.Write("ComicInfo.xml", xmlPath);
                    File.Delete(xmlPath);
                }
            }

            return cbzPath;
        }
        finally
        {
            if (File.Exists(imgPath)) File.Delete(imgPath);
        }
    }

    [Fact]
    public async Task RunAsync_HelpJson_ReturnsStructuredJsonHelp()
    {
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();

        int exitCode = await CliApp.RunAsync(new[] { "help", "--json" }, null, swOut, swErr);

        Assert.Equal(0, exitCode);
        string output = swOut.ToString();
        Assert.Contains("\"name\": \"InkTag.Cli\"", output);
        Assert.Contains("\"subcommands\":", output);
        Assert.Contains("\"globalFlags\":", output);

        using var doc = JsonDocument.Parse(output);
        Assert.Equal("InkTag.Cli", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task RunAsync_SchemaJson_ReturnsComicInfoSchema()
    {
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();

        int exitCode = await CliApp.RunAsync(new[] { "schema", "--json" }, null, swOut, swErr);

        Assert.Equal(0, exitCode);
        string output = swOut.ToString();
        using var doc = JsonDocument.Parse(output);
        Assert.Equal("ComicInfo", doc.RootElement.GetProperty("title").GetString());
        Assert.True(doc.RootElement.GetProperty("properties").TryGetProperty("Series", out _));
    }

    [Fact]
    public async Task RunAsync_ReadCommand_WithValidCbz_OutputsMetadataJson()
    {
        string cbz = CreateTestCbz($"cli_read_{Guid.NewGuid():N}.cbz", title: "Read Test", series: "CLI Series", number: "5");

        try
        {
            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            int exitCode = await CliApp.RunAsync(new[] { "read", cbz, "--json" }, null, swOut, swErr);

            Assert.Equal(0, exitCode);
            string output = swOut.ToString();
            using var doc = JsonDocument.Parse(output);
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(cbz, doc.RootElement.GetProperty("path").GetString());
            var meta = doc.RootElement.GetProperty("metadata");
            Assert.Equal("Read Test", meta.GetProperty("Title").GetString());
            Assert.Equal("CLI Series", meta.GetProperty("Series").GetString());
            Assert.Equal("5", meta.GetProperty("Number").GetString());
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public async Task RunAsync_ReadCommand_NonExistentFile_ReturnsJsonError()
    {
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();

        int exitCode = await CliApp.RunAsync(new[] { "read", "missing_comic.cbz", "--json" }, null, swOut, swErr);

        Assert.Equal(1, exitCode);
        string output = swOut.ToString();
        using var doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("not found", doc.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_UpdateCommand_WithOptEqualsSyntax_ExecutesDryRun()
    {
        string cbz = CreateTestCbz($"cli_update_{Guid.NewGuid():N}.cbz");

        try
        {
            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            // Using --patch=<json> and --dry-run
            int exitCode = await CliApp.RunAsync(new[] { "update", cbz, "--patch={\"Writer\":\"Neil Gaiman\"}", "--dry-run", "--json" }, null, swOut, swErr);

            Assert.Equal(0, exitCode);
            string output = swOut.ToString();
            using var doc = JsonDocument.Parse(output);
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("dryRun").GetBoolean());
            var diffs = doc.RootElement.GetProperty("diffs");
            Assert.True(diffs.GetArrayLength() > 0);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public async Task RunAsync_ScanCommand_WithOptEqualsMissingFilter_ScansDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"cli_scan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string cbz = Path.Combine(tempDir, "Issue 1.cbz");

        try
        {
            string created = CreateTestCbz(Path.GetFileName(cbz), series: "Scan Series", number: "1");
            File.Move(created, cbz, overwrite: true);

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            // Testing scan with --missing=<fields> option=value syntax
            int exitCode = await CliApp.RunAsync(new[] { "scan", tempDir, "--missing=Writer,Genre", "--json" }, null, swOut, swErr);

            Assert.Equal(0, exitCode);
            string output = swOut.ToString();
            using var doc = JsonDocument.Parse(output);
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("totalFound").GetInt32());
            var items = doc.RootElement.GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RunAsync_CoverCommand_WithOptEqualsPage_ExtractsPage()
    {
        string cbz = CreateTestCbz($"cli_cover_{Guid.NewGuid():N}.cbz");
        string outImg = Path.Combine(Path.GetTempPath(), $"cover_out_{Guid.NewGuid():N}.jpg");

        try
        {
            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            // Testing cover with --page=0 and --output=<path>
            int exitCode = await CliApp.RunAsync(new[] { "cover", cbz, "--page=0", $"--output={outImg}", "--json" }, null, swOut, swErr);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outImg));
            string output = swOut.ToString();
            using var doc = JsonDocument.Parse(output);
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(outImg, doc.RootElement.GetProperty("coverPath").GetString());
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
            if (File.Exists(outImg)) File.Delete(outImg);
        }
    }

    [Fact]
    public async Task RunAsync_StripIntroCommand_DryRun_ReturnsPreview()
    {
        string cbz = CreateTestCbz($"cli_strip_{Guid.NewGuid():N}.cbz");

        try
        {
            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            int exitCode = await CliApp.RunAsync(new[] { "strip-intro", cbz, "--dry-run", "--json" }, null, swOut, swErr);

            Assert.Equal(0, exitCode);
            string output = swOut.ToString();
            using var doc = JsonDocument.Parse(output);
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("dryRun").GetBoolean());
            Assert.Equal(2, doc.RootElement.GetProperty("totalPages").GetInt32());
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public async Task RunAsync_RemovePageCommand_WithOptEqualsIndex_DryRunReturnsPreview()
    {
        string cbz = CreateTestCbz($"cli_remove_page_{Guid.NewGuid():N}.cbz");

        try
        {
            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            int exitCode = await CliApp.RunAsync(new[] { "remove-page", cbz, "--index=1", "--dry-run", "--json" }, null, swOut, swErr);

            Assert.Equal(0, exitCode);
            string output = swOut.ToString();
            using var doc = JsonDocument.Parse(output);
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("dryRun").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("pageIndex").GetInt32());
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public async Task RunAsync_RenameCommand_WithOptEqualsTemplate_DryRunPreviews()
    {
        string cbz = CreateTestCbz($"cli_rename_{Guid.NewGuid():N}.cbz", series: "Batman", number: "1");

        try
        {
            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            int exitCode = await CliApp.RunAsync(new[] { "rename", cbz, "--template={Series} #{Number}", "--dry-run", "--json" }, null, swOut, swErr);

            Assert.Equal(0, exitCode);
            string output = swOut.ToString();
            using var doc = JsonDocument.Parse(output);
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("dryRun").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("total").GetInt32());
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public async Task RunAsync_LegacyFallbackDirectPath_ReroutesToReadOrScan()
    {
        string cbz = CreateTestCbz($"cli_fallback_{Guid.NewGuid():N}.cbz", title: "Fallback Comic");

        try
        {
            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            // Calling `inktag <file> --json` directly without "read" subcommand
            int exitCode = await CliApp.RunAsync(new[] { cbz, "--json" }, null, swOut, swErr);

            Assert.Equal(0, exitCode);
            string output = swOut.ToString();
            using var doc = JsonDocument.Parse(output);
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("Fallback Comic", doc.RootElement.GetProperty("metadata").GetProperty("Title").GetString());
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public async Task RunAsync_MissingRequiredPatch_ReturnsErrorJson()
    {
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();

        int exitCode = await CliApp.RunAsync(new[] { "update", "some_file.cbz", "--json" }, null, swOut, swErr);

        Assert.Equal(1, exitCode);
        string output = swOut.ToString();
        using var doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("patch", doc.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_RemovePage_OutOfBounds_ReturnsNonZeroExitCode_AndWritesErrorToErrWriter()
    {
        string cbz = CreateTestCbz($"cli_rm_oob_{Guid.NewGuid():N}.cbz");
        try
        {
            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            int exitCode = await CliApp.RunAsync(new[] { "remove-page", cbz, "--index", "99", "--dry-run" }, null, swOut, swErr);

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, swOut.ToString());
            Assert.Contains("out of bounds", swErr.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public async Task RunAsync_RemovePage_OutOfBoundsJson_ReturnsNonZeroExitCode_AndSuccessFalse()
    {
        string cbz = CreateTestCbz($"cli_rm_oob_json_{Guid.NewGuid():N}.cbz");
        try
        {
            using var swOut = new StringWriter();
            using var swErr = new StringWriter();

            int exitCode = await CliApp.RunAsync(new[] { "remove-page", cbz, "--index", "99", "--dry-run", "--json" }, null, swOut, swErr);

            Assert.Equal(1, exitCode);
            using var doc = JsonDocument.Parse(swOut.ToString());
            Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }
}
