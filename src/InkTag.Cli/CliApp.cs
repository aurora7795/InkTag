using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core;
using InkTag.Core.Configuration;
using InkTag.Core.Renaming;
using InkTag.Core.Scrapers;

namespace InkTag.Cli;

public static class CliApp
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "read", "update", "scan", "cover", "strip-intro", "remove-page",
        "schema", "scrape", "rename", "help", "--help", "-h"
    };

    /// <summary>
    /// Executes the InkTag CLI with the specified arguments, editor, and output streams.
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        MetadataEditor? editor = null,
        TextWriter? stdout = null,
        TextWriter? stderr = null,
        CancellationToken ct = default)
    {
        editor ??= new MetadataEditor();
        var outWriter = stdout ?? Console.Out;
        var errWriter = stderr ?? Console.Error;

        bool isJson = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));
        bool isVerbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase));

        // Legacy / fallback argument rewriting:
        // If the first non-option token is a file or directory on disk and not a known command,
        // rewrite to 'read <file>' or 'scan <dir>'.
        string[] effectiveArgs = PreprocessArgs(args);

        // Explicit help check:
        if (effectiveArgs.Length == 0 ||
            (effectiveArgs.Length <= 2 &&
             (effectiveArgs[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
              effectiveArgs[0].Equals("-h", StringComparison.OrdinalIgnoreCase) ||
              effectiveArgs[0].Equals("help", StringComparison.OrdinalIgnoreCase))))
        {
            if (isJson || effectiveArgs.Length == 0)
            {
                PrintHelp(isJson, outWriter);
                return 0;
            }
        }

        var (rootCommand, jsonOpt, dryRunOpt, verboseOpt) = BuildCommandTree(editor, outWriter, errWriter);

        try
        {
            var parseResult = rootCommand.Parse(effectiveArgs);

            if (parseResult.Errors.Count > 0)
            {
                string errMsg = string.Join("; ", parseResult.Errors.Select(e => e.Message));
                if (isJson)
                {
                    object errObj = isVerbose
                        ? new { success = false, error = errMsg, stackTrace = (string?)null }
                        : (object)new { success = false, error = errMsg };
                    outWriter.WriteLine(JsonSerializer.Serialize(errObj, SerializerOptions));
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    errWriter.WriteLine($"Error: {errMsg}");
                    Console.ResetColor();
                }
                return 1;
            }

            return await parseResult.InvokeAsync(null, ct);
        }
        catch (Exception ex)
        {
            HandleError(ex, isJson, isVerbose, outWriter, errWriter);
            return 1;
        }
    }

    private static string[] PreprocessArgs(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("-")) return args;

        string firstPositional = args[0];
        if (!KnownCommands.Contains(firstPositional))
        {
            if (File.Exists(firstPositional))
            {
                var list = new List<string> { "read" };
                list.AddRange(args);
                return list.ToArray();
            }
            if (Directory.Exists(firstPositional))
            {
                var list = new List<string> { "scan" };
                list.AddRange(args);
                return list.ToArray();
            }
        }

        return args;
    }

    private static Func<ParseResult, int> SafeAction(
        Option<bool> jsonOpt,
        Option<bool> verboseOpt,
        TextWriter outWriter,
        TextWriter errWriter,
        Func<ParseResult, int> action)
    {
        return parseResult =>
        {
            bool isJson = parseResult.GetValue(jsonOpt);
            bool isVerbose = parseResult.GetValue(verboseOpt);
            try
            {
                return action(parseResult);
            }
            catch (Exception ex)
            {
                HandleError(ex, isJson, isVerbose, outWriter, errWriter);
                return 1;
            }
        };
    }

    private static Func<ParseResult, CancellationToken, Task<int>> SafeActionAsync(
        Option<bool> jsonOpt,
        Option<bool> verboseOpt,
        TextWriter outWriter,
        TextWriter errWriter,
        Func<ParseResult, CancellationToken, Task<int>> action)
    {
        return async (parseResult, ct) =>
        {
            bool isJson = parseResult.GetValue(jsonOpt);
            bool isVerbose = parseResult.GetValue(verboseOpt);
            try
            {
                return await action(parseResult, ct);
            }
            catch (Exception ex)
            {
                HandleError(ex, isJson, isVerbose, outWriter, errWriter);
                return 1;
            }
        };
    }

    private static (RootCommand Root, Option<bool> Json, Option<bool> DryRun, Option<bool> Verbose) BuildCommandTree(
        MetadataEditor editor,
        TextWriter outWriter,
        TextWriter errWriter)
    {
        var rootCommand = new RootCommand("AI-Agent friendly CLI for editing ComicInfo.xml metadata in CBZ/CBR archives.");

        // Global options with Recursive = true so all subcommands inherit them
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Output structured machine-parseable JSON.",
            Recursive = true
        };
        var dryRunOpt = new Option<bool>("--dry-run")
        {
            Description = "Preview changes without writing files to disk.",
            Recursive = true
        };
        var verboseOpt = new Option<bool>("--verbose")
        {
            Description = "Include detailed stack traces in error outputs.",
            Recursive = true
        };

        rootCommand.Add(jsonOpt);
        rootCommand.Add(dryRunOpt);
        rootCommand.Add(verboseOpt);

        // 1. read
        var readCmd = new Command("read", "Reads and displays ComicInfo metadata as JSON or text.");
        var readFileArg = new Argument<string>("file") { Description = "Path to comic archive (.cbz/.cbr)" };
        readCmd.Add(readFileArg);
        readCmd.SetAction(SafeAction(jsonOpt, verboseOpt, outWriter, errWriter, (parseResult) =>
        {
            string filePath = parseResult.GetValue(readFileArg)!;
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Comic archive not found: {filePath}");
            }

            var metadata = editor.ReadMetadata(filePath);
            bool isJson = parseResult.GetValue(jsonOpt);

            if (isJson)
            {
                var result = new { success = true, path = filePath, metadata = metadata };
                outWriter.WriteLine(JsonSerializer.Serialize(result, SerializerOptions));
            }
            else
            {
                outWriter.WriteLine($"--- Metadata for {Path.GetFileName(filePath)} ---");
                outWriter.WriteLine(JsonSerializer.Serialize(metadata, SerializerOptions));
            }
            return 0;
        }));
        rootCommand.Add(readCmd);

        // 2. update
        var updateCmd = new Command("update", "Applies JSON property edits to one or all comic archives.");
        var updateTargetArg = new Argument<string>("target") { Description = "Path to comic file or directory" };
        var patchOpt = new Option<string>("--patch") { Description = "JSON patch containing properties to update", Required = true };
        var updateRecursiveOpt = new Option<bool>("--recursive", "-r") { Description = "Recursively search subdirectories" };

        updateCmd.Add(updateTargetArg);
        updateCmd.Add(patchOpt);
        updateCmd.Add(updateRecursiveOpt);

        updateCmd.SetAction(SafeAction(jsonOpt, verboseOpt, outWriter, errWriter, (parseResult) =>
        {
            string targetPath = parseResult.GetValue(updateTargetArg)!;
            string? patchJson = parseResult.GetValue(patchOpt);
            bool isRecursive = parseResult.GetValue(updateRecursiveOpt);
            bool isDryRun = parseResult.GetValue(dryRunOpt);
            bool isJson = parseResult.GetValue(jsonOpt);

            if (string.IsNullOrWhiteSpace(patchJson))
            {
                throw new ArgumentException("Missing required option --patch '<json>'");
            }

            var result = AgentOperations.UpdatePath(editor, targetPath, patchJson, isDryRun, isRecursive);

            if (!result.IsDirectory)
            {
                if (isJson)
                {
                    var jsonRes = (result.Warnings != null && result.Warnings.Count > 0)
                        ? (object)new { success = true, path = result.TargetPath, dryRun = result.DryRun, diffs = result.Diffs, warnings = result.Warnings }
                        : (object)new { success = true, path = result.TargetPath, dryRun = result.DryRun, diffs = result.Diffs };
                    outWriter.WriteLine(JsonSerializer.Serialize(jsonRes, SerializerOptions));
                }
                else
                {
                    outWriter.WriteLine($"{(isDryRun ? "[DRY RUN] " : "")}Updated {Path.GetFileName(result.TargetPath)} successfully.");
                    outWriter.WriteLine($"Modifications ({result.Diffs?.Count ?? 0} fields):");
                    if (result.Diffs != null)
                    {
                        foreach (var d in result.Diffs)
                        {
                            outWriter.WriteLine($"  - {d.PropertyName}: '{d.OldValue}' => '{d.NewValue}'");
                        }
                    }
                }
            }
            else
            {
                if (isDryRun)
                {
                    var fileDiffsForJson = result.FileDiffs?.Select(fd => new
                    {
                        path = fd.Path,
                        diffs = fd.Diffs
                    }).ToList();

                    if (isJson)
                    {
                        var jsonRes = new { success = true, directory = result.TargetPath, dryRun = true, files = fileDiffsForJson };
                        outWriter.WriteLine(JsonSerializer.Serialize(jsonRes, SerializerOptions));
                    }
                    else
                    {
                        outWriter.WriteLine($"[DRY RUN] Would update {result.FileDiffs?.Count ?? 0} files in {result.TargetPath}.");
                    }
                }
                else
                {
                    if (isJson)
                    {
                        var jsonRes = new { success = result.Report?.Failures.Count == 0, report = result.Report };
                        outWriter.WriteLine(JsonSerializer.Serialize(jsonRes, SerializerOptions));
                    }
                    else
                    {
                        outWriter.WriteLine($"Bulk edit complete: {result.Report?.Successes.Count ?? 0} succeeded, {result.Report?.Failures.Count ?? 0} failed.");
                    }
                }
            }
            return 0;
        }));
        rootCommand.Add(updateCmd);

        // 3. scan
        var scanCmd = new Command("scan", "Scans a directory for comic files, untagged comics, and missing metadata.");
        var scanDirArg = new Argument<string?>("directory")
        {
            Description = "Directory to scan (defaults to current working directory)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var missingOpt = new Option<string?>("--missing") { Description = "Comma-separated list of required fields" };
        var untaggedOpt = new Option<bool>("--untagged", "--no-metadata") { Description = "Only list untagged comic files" };
        var scanRecursiveOpt = new Option<bool>("--recursive", "-r") { Description = "Recursively search subdirectories" };

        scanCmd.Add(scanDirArg);
        scanCmd.Add(missingOpt);
        scanCmd.Add(untaggedOpt);
        scanCmd.Add(scanRecursiveOpt);

        scanCmd.SetAction(SafeAction(jsonOpt, verboseOpt, outWriter, errWriter, (parseResult) =>
        {
            string targetDir = parseResult.GetValue(scanDirArg) ?? Directory.GetCurrentDirectory();
            string? missingFilterStr = parseResult.GetValue(missingOpt);
            var missingFields = missingFilterStr?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
            bool isRecursive = parseResult.GetValue(scanRecursiveOpt);
            bool isUntaggedOnly = parseResult.GetValue(untaggedOpt);
            bool isJson = parseResult.GetValue(jsonOpt);

            var scanResult = AgentOperations.ScanDirectory(editor, targetDir, missingFields, isRecursive, isUntaggedOnly);

            if (isJson)
            {
                var itemsForJson = scanResult.Items.Select(item => new
                {
                    path = item.Path,
                    title = item.Title,
                    series = item.Series,
                    number = item.Number,
                    year = item.Year,
                    hasEmbeddedXml = item.HasEmbeddedXml,
                    isUntagged = item.IsUntagged,
                    missingFields = item.MissingFields
                }).ToList();

                var payload = new
                {
                    success = true,
                    directory = scanResult.Directory,
                    totalFound = scanResult.TotalFound,
                    untaggedCount = scanResult.UntaggedCount,
                    onlyUntagged = scanResult.OnlyUntagged,
                    items = itemsForJson
                };
                outWriter.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
            }
            else
            {
                if (scanResult.OnlyUntagged)
                {
                    outWriter.WriteLine($"Found {scanResult.Items.Count} untagged comic archive(s) in {scanResult.Directory} (out of {scanResult.TotalFound} total):");
                }
                else
                {
                    outWriter.WriteLine($"Found {scanResult.TotalFound} comic archives in {scanResult.Directory} ({scanResult.UntaggedCount} untagged):");
                }

                foreach (var item in scanResult.Items)
                {
                    string tagStatus = item.IsUntagged ? "[UNTAGGED]" : "[TAGGED]";
                    outWriter.WriteLine($"  {tagStatus} [{item.Number ?? "?"}] {item.Series ?? "Unknown"} - {item.Title ?? Path.GetFileName(item.Path)}");
                }
            }
            return 0;
        }));
        rootCommand.Add(scanCmd);

        // 4. cover
        var coverCmd = new Command("cover", "Extracts front cover or specific page image from archive.");
        var coverFileArg = new Argument<string>("file") { Description = "Path to comic archive" };
        var coverPageOpt = new Option<int?>("--page") { Description = "0-based page index to extract" };
        var coverOutputOpt = new Option<string?>("--output") { Description = "Destination path for extracted image file" };

        coverCmd.Add(coverFileArg);
        coverCmd.Add(coverPageOpt);
        coverCmd.Add(coverOutputOpt);

        coverCmd.SetAction(SafeAction(jsonOpt, verboseOpt, outWriter, errWriter, (parseResult) =>
        {
            string comicPath = parseResult.GetValue(coverFileArg)!;
            string? outputPath = parseResult.GetValue(coverOutputOpt);
            int pageIndex = parseResult.GetValue(coverPageOpt) ?? 0;
            bool isJson = parseResult.GetValue(jsonOpt);

            if (string.IsNullOrEmpty(outputPath))
            {
                string dir = Path.GetDirectoryName(comicPath) ?? ".";
                string nameNoExt = Path.GetFileNameWithoutExtension(comicPath);
                string suffix = pageIndex > 0 ? $"_page{pageIndex + 1}.jpg" : "_cover.jpg";
                outputPath = Path.Combine(dir, $"{nameNoExt}{suffix}");
            }

            string? extractedPath = editor.ExtractCoverImage(comicPath, outputPath, pageIndex);

            if (extractedPath != null && File.Exists(extractedPath))
            {
                if (isJson)
                {
                    var res = new { success = true, comicPath = comicPath, coverPath = extractedPath, pageIndex = pageIndex };
                    outWriter.WriteLine(JsonSerializer.Serialize(res, SerializerOptions));
                }
                else
                {
                    outWriter.WriteLine($"Extracted page {pageIndex + 1} image to: {extractedPath}");
                }
            }
            else
            {
                throw new InvalidOperationException($"No image at page index {pageIndex} could be extracted from: {comicPath}");
            }
            return 0;
        }));
        rootCommand.Add(coverCmd);

        // 5. strip-intro
        var stripIntroCmd = new Command("strip-intro", "Strips the first page (provider/scanner title page) from archive(s).");
        var stripIntroTargetArg = new Argument<string>("target") { Description = "Path to comic file or directory" };
        var stripIntroRecursiveOpt = new Option<bool>("--recursive", "-r") { Description = "Recursively search subdirectories" };

        stripIntroCmd.Add(stripIntroTargetArg);
        stripIntroCmd.Add(stripIntroRecursiveOpt);

        stripIntroCmd.SetAction(SafeAction(jsonOpt, verboseOpt, outWriter, errWriter, (parseResult) =>
        {
            string targetPath = parseResult.GetValue(stripIntroTargetArg)!;
            bool isRecursive = parseResult.GetValue(stripIntroRecursiveOpt);
            bool isDryRun = parseResult.GetValue(dryRunOpt);
            bool isJson = parseResult.GetValue(jsonOpt);

            if (File.Exists(targetPath))
            {
                if (isDryRun)
                {
                    var entries = editor.GetImageEntries(targetPath);
                    string? firstPage = entries.Count > 0 ? entries[0] : null;
                    if (isJson)
                    {
                        var res = new { success = true, dryRun = true, file = targetPath, firstPage = firstPage, totalPages = entries.Count };
                        outWriter.WriteLine(JsonSerializer.Serialize(res, SerializerOptions));
                    }
                    else
                    {
                        outWriter.WriteLine($"[DRY RUN] Would strip first page '{firstPage}' from '{Path.GetFileName(targetPath)}' ({entries.Count} -> {Math.Max(0, entries.Count - 1)} pages).");
                    }
                    return 0;
                }

                var result = editor.StripFirstPage(targetPath);
                if (isJson)
                {
                    outWriter.WriteLine(JsonSerializer.Serialize(result, SerializerOptions));
                }
                else
                {
                    if (result.Success)
                    {
                        outWriter.WriteLine($"Successfully stripped first page ({string.Join(", ", result.RemovedEntries)}) from '{Path.GetFileName(result.FilePath)}' ({result.OriginalPageCount} -> {result.FinalPageCount} pages).");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        outWriter.WriteLine($"Failed to strip page: {result.ErrorMessage}");
                        Console.ResetColor();
                    }
                }
            }
            else if (Directory.Exists(targetPath))
            {
                var searchOption = isRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(targetPath, "*.*", searchOption).Where(MetadataEditor.IsSupportedComicFile).ToList();

                var results = new List<PageRemovalResult>();
                int strippedCount = 0;

                foreach (var file in files)
                {
                    if (isDryRun)
                    {
                        var entries = editor.GetImageEntries(file);
                        results.Add(new PageRemovalResult
                        {
                            Success = true,
                            FilePath = file,
                            OriginalPageCount = entries.Count,
                            FinalPageCount = Math.Max(0, entries.Count - 1),
                            RemovedCount = 1,
                            RemovedEntries = entries.Take(1).ToList()
                        });
                    }
                    else
                    {
                        var res = editor.StripFirstPage(file);
                        results.Add(res);
                        if (res.Success) strippedCount++;
                    }
                }

                if (isJson)
                {
                    var summary = new
                    {
                        success = true,
                        dryRun = isDryRun,
                        totalFiles = files.Count,
                        strippedCount = isDryRun ? files.Count : strippedCount,
                        items = results
                    };
                    outWriter.WriteLine(JsonSerializer.Serialize(summary, SerializerOptions));
                }
                else
                {
                    outWriter.WriteLine($"{(isDryRun ? "[DRY RUN] Would strip" : "Stripped")} first page across {results.Count(r => r.Success)} of {files.Count} files.");
                }
            }
            else
            {
                throw new FileNotFoundException($"Path not found: '{targetPath}'");
            }
            return 0;
        }));
        rootCommand.Add(stripIntroCmd);

        // 6. remove-page
        var removePageCmd = new Command("remove-page", "Removes a specific page by 0-based index from archive.");
        var removePageFileArg = new Argument<string>("file") { Description = "Path to comic file" };
        var removePageIndexOpt = new Option<int>("--index") { Description = "0-based index of page to remove", Required = true };

        removePageCmd.Add(removePageFileArg);
        removePageCmd.Add(removePageIndexOpt);

        removePageCmd.SetAction(SafeAction(jsonOpt, verboseOpt, outWriter, errWriter, (parseResult) =>
        {
            string filePath = parseResult.GetValue(removePageFileArg)!;
            int pageIndex = parseResult.GetValue(removePageIndexOpt);
            bool isDryRun = parseResult.GetValue(dryRunOpt);
            bool isJson = parseResult.GetValue(jsonOpt);

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Comic file not found: {filePath}");
            }

            if (isDryRun)
            {
                var entries = editor.GetImageEntries(filePath);
                string? targetEntry = pageIndex >= 0 && pageIndex < entries.Count ? entries[pageIndex] : null;
                if (isJson)
                {
                    var res = new { success = true, dryRun = true, file = filePath, pageIndex = pageIndex, targetEntry = targetEntry, totalPages = entries.Count };
                    outWriter.WriteLine(JsonSerializer.Serialize(res, SerializerOptions));
                }
                else
                {
                    outWriter.WriteLine($"[DRY RUN] Would remove page index {pageIndex} ('{targetEntry}') from '{Path.GetFileName(filePath)}' ({entries.Count} -> {Math.Max(0, entries.Count - 1)} pages).");
                }
                return 0;
            }

            var result = editor.RemoveArchivePages(filePath, new[] { pageIndex });
            if (isJson)
            {
                outWriter.WriteLine(JsonSerializer.Serialize(result, SerializerOptions));
            }
            else
            {
                if (result.Success)
                {
                    outWriter.WriteLine($"Successfully removed page {pageIndex} ({string.Join(", ", result.RemovedEntries)}) from '{Path.GetFileName(result.FilePath)}' ({result.OriginalPageCount} -> {result.FinalPageCount} pages).");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    outWriter.WriteLine($"Failed to remove page: {result.ErrorMessage}");
                    Console.ResetColor();
                }
            }
            return 0;
        }));
        rootCommand.Add(removePageCmd);

        // 7. schema
        var schemaCmd = new Command("schema", "Prints JSON Schema for ComicInfo metadata objects.");
        schemaCmd.SetAction(SafeAction(jsonOpt, verboseOpt, outWriter, errWriter, (parseResult) =>
        {
            bool isJson = parseResult.GetValue(jsonOpt);
            string schemaJson = MetadataEditor.ExportJsonSchema();
            if (isJson)
            {
                outWriter.WriteLine(schemaJson);
            }
            else
            {
                outWriter.WriteLine("--- ComicInfo Metadata JSON Schema ---");
                outWriter.WriteLine(schemaJson);
            }
            return 0;
        }));
        rootCommand.Add(schemaCmd);

        // 8. scrape
        var scrapeCmd = new Command("scrape", "Auto-scrapes metadata from ComicVine online database with intro page detection.");
        var scrapeTargetArg = new Argument<string>("target") { Description = "Path to comic file or directory" };
        var apiKeyOpt = new Option<string?>("--api-key") { Description = "ComicVine API key override" };
        var modeOpt = new Option<string?>("--mode") { Description = "Merge mode: fill-missing (default) or overwrite" };
        var scrapeCoverPageOpt = new Option<int?>("--cover-page") { Description = "Target cover page index" };
        var stripIntroPageOpt = new Option<bool>("--strip-intro-page", "--strip-intro") { Description = "Strip intro page if detected" };
        var noIntroFallbackOpt = new Option<bool>("--no-intro-fallback", "--no-intro-detection") { Description = "Disable intro page fallback detection" };
        var scrapeRecursiveOpt = new Option<bool>("--recursive", "-r") { Description = "Recursively search subdirectories" };

        scrapeCmd.Add(scrapeTargetArg);
        scrapeCmd.Add(apiKeyOpt);
        scrapeCmd.Add(modeOpt);
        scrapeCmd.Add(scrapeCoverPageOpt);
        scrapeCmd.Add(stripIntroPageOpt);
        scrapeCmd.Add(noIntroFallbackOpt);
        scrapeCmd.Add(scrapeRecursiveOpt);

        scrapeCmd.SetAction(SafeActionAsync(jsonOpt, verboseOpt, outWriter, errWriter, async (parseResult, ct) =>
        {
            string targetPath = parseResult.GetValue(scrapeTargetArg)!;
            string? apiKey = parseResult.GetValue(apiKeyOpt);
            string? modeStr = parseResult.GetValue(modeOpt);
            int? coverPageIndex = parseResult.GetValue(scrapeCoverPageOpt);
            bool stripIntroPage = parseResult.GetValue(stripIntroPageOpt);
            bool noIntroFallback = parseResult.GetValue(noIntroFallbackOpt);
            bool isRecursive = parseResult.GetValue(scrapeRecursiveOpt);
            bool isDryRun = parseResult.GetValue(dryRunOpt);
            bool isJson = parseResult.GetValue(jsonOpt);

            ScrapeMergeMode mergeMode = ScrapeMergeMode.FillMissingOnly;
            if (string.Equals(modeStr, "overwrite", StringComparison.OrdinalIgnoreCase))
            {
                mergeMode = ScrapeMergeMode.OverwriteAll;
            }

            var settingsService = new AppSettingsService();
            if (!string.IsNullOrEmpty(apiKey))
            {
                settingsService.Settings.ComicVineApiKey = apiKey;
            }

            using var scraperService = new MetadataScraperService(settingsService);

            if (File.Exists(targetPath))
            {
                var comic = editor.ReadMetadata(targetPath);
                ulong coverHash = editor.GetCoverHash(targetPath, coverPageIndex ?? 0);
                var result = await scraperService.AutoScrapeComicAsync(
                    comic,
                    coverHash != 0 ? coverHash : null,
                    targetPath,
                    enableIntroPageFallback: !noIntroFallback,
                    targetCoverPageIndex: coverPageIndex,
                    ct: ct);

                var reportComic = comic;
                var fetched = result.FetchedMetadata;

                if (result.Success && fetched != null)
                {
                    if (!isDryRun)
                    {
                        if (stripIntroPage && result.DetectedIntroPage)
                        {
                            var stripResult = editor.StripFirstPage(targetPath);
                            if (stripResult.Success)
                            {
                                targetPath = stripResult.FilePath;
                            }
                        }

                        editor.EditMetadata(
                            targetPath,
                            existing => scraperService.ApplyMetadata(existing, fetched, mergeMode),
                            changeReason: "Scraped ComicVine metadata",
                            coverDHash: coverHash != 0 ? coverHash.ToString("X16") : null,
                            matchedThumbnailUrl: result.SelectedCandidate?.SmallCoverUrl ?? result.SelectedCandidate?.CoverUrl,
                            matchConfidence: result.SelectedCandidate?.MatchConfidence,
                            visualSimilarity: result.SelectedCandidate?.VisualSimilarity);

                        reportComic = editor.ReadMetadata(targetPath);
                    }
                    else
                    {
                        reportComic = comic.Clone();
                        scraperService.ApplyMetadata(reportComic, fetched, mergeMode);
                    }
                }

                if (isJson)
                {
                    var resObj = new
                    {
                        success = result.Success,
                        message = result.Message,
                        dryRun = isDryRun,
                        file = targetPath,
                        series = reportComic.Series,
                        number = reportComic.Number,
                        title = reportComic.Title,
                        writer = reportComic.Writer,
                        detectedIntroPage = result.DetectedIntroPage,
                        trueCoverPageIndex = result.TrueCoverPageIndex
                    };
                    outWriter.WriteLine(JsonSerializer.Serialize(resObj, SerializerOptions));
                }
                else
                {
                    outWriter.WriteLine(result.Message);
                }
            }
            else if (Directory.Exists(targetPath))
            {
                var searchOption = isRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(targetPath, "*.*", searchOption)
                                     .Where(MetadataEditor.IsSupportedComicFile)
                                     .ToList();

                var queueService = new BulkScrapeQueueService(scraperService, editor, settingsService);
                var queue = queueService.CreateQueue(files);
                var options = new BulkScrapeOptions
                {
                    MergeMode = mergeMode,
                    ConfidenceThreshold = settingsService.Settings.AutoMatchConfidenceThreshold,
                    EnableSmartSeriesGrouping = true,
                    EnableIntroPageFallback = !noIntroFallback,
                    StripDetectedIntroPages = stripIntroPage
                };

                var progress = new Progress<BulkScrapeProgressReport>(report =>
                {
                    if (!isJson)
                    {
                        outWriter.WriteLine($"[{report.ProcessedItems}/{report.TotalItems}] {report.StatusMessage}");
                    }
                });

                var summaryReport = await queueService.ProcessQueueAsync(queue, options, progress, ct);

                if (!isDryRun)
                {
                    await queueService.ApplyMatchedMetadataAsync(queue, mergeMode, stripDetectedIntroPages: stripIntroPage, ct: ct);
                }

                if (isJson)
                {
                    var summary = new
                    {
                        success = true,
                        dryRun = isDryRun,
                        totalFiles = summaryReport.Total,
                        scrapedCount = summaryReport.Matched,
                        reviewNeeded = summaryReport.LowConfidence,
                        failedCount = summaryReport.Unmatched + summaryReport.Failed,
                        details = summaryReport.Items.Select(i => new
                        {
                            file = i.FilePath,
                            status = i.Status.ToString(),
                            matchedIssue = i.MatchedCandidate != null ? $"{i.MatchedCandidate.SeriesTitle} #{i.MatchedCandidate.IssueNumber}" : null,
                            visualSimilarity = i.MatchedCandidate?.VisualSimilarity,
                            matchConfidence = i.MatchedCandidate?.MatchConfidence,
                            detectedIntroPage = i.DetectedIntroPage,
                            message = i.StatusMessage
                        })
                    };
                    outWriter.WriteLine(JsonSerializer.Serialize(summary, SerializerOptions));
                }
                else
                {
                    outWriter.WriteLine($"\n--- Bulk Scrape Complete ---");
                    outWriter.WriteLine($"Total: {summaryReport.Total} | Matched: {summaryReport.Matched} | Review Needed: {summaryReport.LowConfidence} | Unmatched: {summaryReport.Unmatched + summaryReport.Failed}");
                    if (isDryRun)
                    {
                        outWriter.WriteLine("(Dry run: no changes written to files)");
                    }
                }
            }
            else
            {
                throw new FileNotFoundException($"Target path not found: '{targetPath}'");
            }
            return 0;
        }));
        rootCommand.Add(scrapeCmd);

        // 9. rename
        var renameCmd = new Command("rename", "Bulk renames files based on metadata template pattern.");
        var renameTargetArg = new Argument<string>("target") { Description = "Path to comic file or directory" };
        var templateOpt = new Option<string?>("--template") { Description = "Renaming template pattern" };
        var stripScanInfoOpt = new Option<bool>("--strip-scan-info") { Description = "Strip scanner information from filenames" };
        var renameRecursiveOpt = new Option<bool>("--recursive", "-r") { Description = "Recursively search subdirectories" };

        renameCmd.Add(renameTargetArg);
        renameCmd.Add(templateOpt);
        renameCmd.Add(stripScanInfoOpt);
        renameCmd.Add(renameRecursiveOpt);

        renameCmd.SetAction(SafeAction(jsonOpt, verboseOpt, outWriter, errWriter, (parseResult) =>
        {
            string targetPath = parseResult.GetValue(renameTargetArg)!;
            string template = parseResult.GetValue(templateOpt) ?? ComicFileRenamer.DefaultTemplate;
            bool preserveScanInfo = !parseResult.GetValue(stripScanInfoOpt);
            bool isRecursive = parseResult.GetValue(renameRecursiveOpt);
            bool isDryRun = parseResult.GetValue(dryRunOpt);
            bool isJson = parseResult.GetValue(jsonOpt);

            var filesToProcess = new List<string>();
            if (File.Exists(targetPath))
            {
                filesToProcess.Add(targetPath);
            }
            else if (Directory.Exists(targetPath))
            {
                var searchOpt = isRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cbz", ".cbr", ".cb7", ".zip", ".rar" };
                filesToProcess.AddRange(Directory.EnumerateFiles(targetPath, "*.*", searchOpt).Where(f => exts.Contains(Path.GetExtension(f))));
            }
            else
            {
                throw new FileNotFoundException("Target file or directory not found.", targetPath);
            }

            var items = filesToProcess.Select(f =>
            {
                var comic = editor.ReadMetadata(f);
                return (FilePath: f, Comic: comic);
            }).ToList();

            var previews = ComicFileRenamer.PreviewBatchRename(items, template, preserveScanInfo);

            if (isDryRun)
            {
                if (isJson)
                {
                    var jsonOut = new
                    {
                        success = true,
                        dryRun = true,
                        total = previews.Count,
                        items = previews.Select(p => new
                        {
                            original = p.OriginalFilePath,
                            proposed = p.ProposedFilePath,
                            hasChange = p.HasChange,
                            hasCollision = p.HasCollision,
                            error = p.ErrorMessage
                        })
                    };
                    outWriter.WriteLine(JsonSerializer.Serialize(jsonOut, SerializerOptions));
                }
                else
                {
                    outWriter.WriteLine($"[DRY RUN] Evaluated {previews.Count} file(s) for renaming:\n");
                    foreach (var p in previews)
                    {
                        string status = p.HasCollision ? "[COLLISION]" : (p.HasChange ? "[RENAME]" : "[SKIP]");
                        outWriter.WriteLine($"  {status} {Path.GetFileName(p.OriginalFilePath)} -> {Path.GetFileName(p.ProposedFilePath)}");
                        if (!string.IsNullOrEmpty(p.ErrorMessage))
                        {
                            outWriter.WriteLine($"     Error: {p.ErrorMessage}");
                        }
                    }
                }
                return 0;
            }

            var result = ComicFileRenamer.ExecuteBatchRename(previews);

            if (isJson)
            {
                var jsonOut = new
                {
                    success = true,
                    total = result.Total,
                    renamed = result.Renamed,
                    skipped = result.Skipped,
                    failed = result.Failed,
                    items = result.Items.Select(p => new
                    {
                        original = p.OriginalFilePath,
                        proposed = p.ProposedFilePath,
                        renamed = p.HasChange && string.IsNullOrEmpty(p.ErrorMessage),
                        error = p.ErrorMessage
                    })
                };
                outWriter.WriteLine(JsonSerializer.Serialize(jsonOut, SerializerOptions));
            }
            else
            {
                outWriter.WriteLine($"Renamed {result.Renamed} of {result.Total} file(s) ({result.Skipped} unchanged, {result.Failed} failed).");
            }
            return 0;
        }));
        rootCommand.Add(renameCmd);

        // 10. help
        var helpCmd = new Command("help", "Displays help information.");
        helpCmd.SetAction(SafeAction(jsonOpt, verboseOpt, outWriter, errWriter, (parseResult) =>
        {
            bool isJson = parseResult.GetValue(jsonOpt);
            PrintHelp(isJson, outWriter);
            return 0;
        }));
        rootCommand.Add(helpCmd);

        return (rootCommand, jsonOpt, dryRunOpt, verboseOpt);
    }

    public static void PrintHelp(bool isJson, TextWriter outWriter)
    {
        var helpObj = new
        {
            name = "InkTag.Cli",
            description = "AI-Agent friendly CLI for editing ComicInfo.xml metadata in CBZ/CBR archives.",
            subcommands = new[]
            {
                new { name = "read <file>", description = "Reads and displays ComicInfo metadata as JSON or text." },
                new { name = "update <file|dir> --patch '<json>' [--dry-run] [--recursive]", description = "Applies JSON property edits to one or all comic archives." },
                new { name = "scan <directory> [--untagged] [--missing Field1,Field2] [--recursive]", description = "Scans a directory for comic files, untagged comics, and missing metadata." },
                new { name = "scrape <file|dir> [--api-key KEY] [--mode fill-missing|overwrite] [--cover-page <n>] [--strip-intro-page]", description = "Auto-scrapes metadata from ComicVine online database with intro page detection." },
                new { name = "rename <file|dir> [--template <pattern>] [--dry-run]", description = "Bulk renames files based on metadata template pattern." },
                new { name = "cover <file> [--page <n>] [--output <image-path>]", description = "Extracts front cover or specific page image from archive." },
                new { name = "strip-intro <file|dir> [--dry-run]", description = "Strips the first page (provider/scanner title page) from archive(s)." },
                new { name = "remove-page <file> --index <n> [--dry-run]", description = "Removes a specific page by 0-based index from archive." },
                new { name = "schema", description = "Prints JSON Schema for ComicInfo metadata objects." }
            },
            globalFlags = new[]
            {
                new { flag = "--json", description = "Output structured machine-parseable JSON." },
                new { flag = "--dry-run", description = "Preview changes without writing files to disk." },
                new { flag = "--recursive, -r", description = "Recursively search subdirectories when scanning or updating directories." },
                new { flag = "--verbose", description = "Include detailed stack traces in error outputs." }
            }
        };

        if (isJson)
        {
            outWriter.WriteLine(JsonSerializer.Serialize(helpObj, SerializerOptions));
        }
        else
        {
            outWriter.WriteLine("InkTag.Cli - AI-Agent Friendly CLI\n");
            outWriter.WriteLine("Usage: dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- <command> [options]\n");
            outWriter.WriteLine("Commands:");
            foreach (var cmd in helpObj.subcommands)
            {
                outWriter.WriteLine($"  {cmd.name,-45} {cmd.description}");
            }
            outWriter.WriteLine("\nGlobal Flags:");
            foreach (var flag in helpObj.globalFlags)
            {
                outWriter.WriteLine($"  {flag.flag,-45} {flag.description}");
            }
        }
    }

    private static void HandleError(Exception ex, bool isJson, bool isVerbose, TextWriter outWriter, TextWriter errWriter)
    {
        if (isJson)
        {
            object errObj = isVerbose
                ? new { success = false, error = ex.Message, stackTrace = ex.StackTrace }
                : (object)new { success = false, error = ex.Message };
            outWriter.WriteLine(JsonSerializer.Serialize(errObj, SerializerOptions));
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            errWriter.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
        }
    }
}
