using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InkTag.Core.IO;
using InkTag.Core.Logging;

namespace InkTag.Core.Backup;

public record MetadataBackupEntry(
    string Id,
    string ArchivePath,
    string Timestamp,
    string OperationType,
    string? OriginalFileName,
    string BackupXmlFileName,
    string? BatchJobId = null,
    string? SourceFileHash = null,
    string? CoverDHash = null,
    string? MatchedThumbnailUrl = null,
    double? MatchConfidence = null,
    double? VisualSimilarity = null,
    string? ChangeReason = null,
    List<MetadataDiffItem>? FieldDiffs = null
);

public record BatchJobSummary(
    string BatchJobId,
    string Timestamp,
    string OperationType,
    int TotalBackups,
    List<string> AffectedFiles
);

public record BatchRollbackReport(
    string BatchJobId,
    int Total,
    int Restored,
    int Failed,
    List<string> RestoredFiles,
    List<(string Path, string Error)> Failures,
    string? RedoBatchJobId = null
);

public record BatchRollbackItemPlan(
    string ArchivePath,
    string StagedCbzPath,
    string BackupOriginalPath,
    string? CurrentXml,
    string RestoredXml,
    bool Swapped
);

public record BatchRollbackJournal(
    string BatchJobId,
    string JournalId,
    string Timestamp,
    string State,
    List<BatchRollbackItemPlan> Items
);

/// <summary>
/// Cross-platform automated metadata backup, rich provenance tracker, and rollback manager for InkTag.
/// Saves pre-write snapshots of ComicInfo.xml before any archive write or metadata change.
/// </summary>
public class MetadataBackupService
{
    private static readonly object Lock = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly Dictionary<string, int> UncompactedCounts = new(StringComparer.OrdinalIgnoreCase);
    private static string? _customBackupDir;

    public const int DefaultJournalCompactionThreshold = 50;

    private int _journalCompactionThreshold = DefaultJournalCompactionThreshold;

    /// <summary>
    /// Number of uncompacted journal records before automatic compaction into backups_manifest.json occurs.
    /// Defaults to 50.
    /// </summary>
    public int JournalCompactionThreshold
    {
        get => _journalCompactionThreshold;
        set => _journalCompactionThreshold = Math.Max(1, value);
    }

    public static string DefaultBackupDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InkTag", "backups");

    public string BackupDirectory { get; }
    public string ManifestPath => Path.Combine(BackupDirectory, "backups_manifest.json");
    public string JournalPath => Path.Combine(BackupDirectory, "backups_journal.jsonl");

    public MetadataBackupService(string? customBackupDir = null)
    {
        BackupDirectory = customBackupDir ?? _customBackupDir ?? DefaultBackupDirectory;
        try
        {
            if (!Directory.Exists(BackupDirectory))
            {
                Directory.CreateDirectory(BackupDirectory);
            }
            else
            {
                RecoverPendingBatchJournals();
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Failed to create backup directory '{BackupDirectory}': {ex.Message}");
        }
    }

    /// <summary>
    /// Overrides the global default backup directory (useful for unit testing).
    /// </summary>
    public static void SetGlobalCustomBackupDirectory(string? customDir)
    {
        _customBackupDir = customDir;
    }

    /// <summary>
    /// Creates a pre-write backup of ComicInfo.xml before an edit or overwrite, storing rich provenance metadata.
    /// Appends the entry to an append-oriented journal to avoid O(n) manifest rewrites during batch operations.
    /// </summary>
    public MetadataBackupEntry? CreateBackup(
        string archivePath,
        string? originalXml,
        string operationType,
        string? batchJobId = null,
        string? sourceFileHash = null,
        string? coverDHash = null,
        string? matchedThumbnailUrl = null,
        double? matchConfidence = null,
        double? visualSimilarity = null,
        string? changeReason = null,
        List<MetadataDiffItem>? fieldDiffs = null)
    {
        if (string.IsNullOrEmpty(archivePath)) return null;

        lock (Lock)
        {
            try
            {
                if (!Directory.Exists(BackupDirectory))
                {
                    Directory.CreateDirectory(BackupDirectory);
                }

                string id = Guid.NewGuid().ToString("N")[..12];
                string timestamp = DateTime.UtcNow.ToString("o");
                string fileHash = ComputeShortHash(archivePath);
                string fileName = Path.GetFileName(archivePath);
                string xmlFileName = $"{fileHash}_{id}.xml";
                string fullXmlPath = Path.Combine(BackupDirectory, xmlFileName);

                // Save XML snapshot (or empty if creating fresh metadata) atomically
                AtomicFile.WriteAllText(fullXmlPath, originalXml ?? string.Empty, Encoding.UTF8);

                var entry = new MetadataBackupEntry(
                    Id: id,
                    ArchivePath: Path.GetFullPath(archivePath),
                    Timestamp: timestamp,
                    OperationType: operationType,
                    OriginalFileName: fileName,
                    BackupXmlFileName: xmlFileName,
                    BatchJobId: batchJobId,
                    SourceFileHash: sourceFileHash,
                    CoverDHash: coverDHash,
                    MatchedThumbnailUrl: matchedThumbnailUrl,
                    MatchConfidence: matchConfidence,
                    VisualSimilarity: visualSimilarity,
                    ChangeReason: changeReason,
                    FieldDiffs: fieldDiffs
                );

                AppendJournalEntry(entry);

                int uncompacted = IncrementUncompactedCount();
                if (uncompacted >= JournalCompactionThreshold)
                {
                    CompactJournalInternal();
                }

                AppLogger.LogDebug($"[MetadataBackupService] Created backup '{id}' for '{archivePath}' (Batch: {batchJobId ?? "none"}, Op: {operationType})");
                return entry;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[MetadataBackupService] Failed to create backup for '{archivePath}': {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Forces a compaction of the append journal into backups_manifest.json.
    /// </summary>
    public void Flush()
    {
        CompactJournal();
    }

    /// <summary>
    /// Compacts pending journal records into backups_manifest.json.
    /// </summary>
    public void CompactJournal()
    {
        lock (Lock)
        {
            CompactJournalInternal();
        }
    }

    /// <summary>
    /// Lists all backup records, optionally filtered by comic archive path.
    /// </summary>
    public IReadOnlyList<MetadataBackupEntry> ListBackups(string? archivePath = null, int limit = 50)
    {
        lock (Lock)
        {
            var manifest = LoadManifestInternal();
            if (!string.IsNullOrEmpty(archivePath))
            {
                string fullPath = Path.GetFullPath(archivePath);
                manifest = manifest.Where(m => string.Equals(m.ArchivePath, fullPath, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return manifest.Take(limit).ToList();
        }
    }

    /// <summary>
    /// Retrieves a specific backup entry with full provenance details.
    /// </summary>
    public MetadataBackupEntry? GetBackupEntry(string backupId)
    {
        lock (Lock)
        {
            var manifest = LoadManifestInternal();
            return manifest.FirstOrDefault(m => string.Equals(m.Id, backupId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Gets the raw XML content of a specific backup snapshot.
    /// </summary>
    public string? GetBackupXml(string backupId)
    {
        lock (Lock)
        {
            var manifest = LoadManifestInternal();
            var entry = manifest.FirstOrDefault(m => string.Equals(m.Id, backupId, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;

            string fullXmlPath = Path.Combine(BackupDirectory, entry.BackupXmlFileName);
            if (!File.Exists(fullXmlPath)) return null;

            return File.ReadAllText(fullXmlPath, Encoding.UTF8);
        }
    }

    /// <summary>
    /// Restores a comic archive's ComicInfo.xml from a backup snapshot.
    /// If backupId is null, the most recent backup for the given archive path is restored.
    /// </summary>
    public bool RestoreBackup(string archivePath, string? backupId = null)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"Archive not found: '{archivePath}'");
        }

        string fullPath = Path.GetFullPath(archivePath);
        MetadataBackupEntry? targetEntry;

        lock (Lock)
        {
            var manifest = LoadManifestInternal();
            if (!string.IsNullOrEmpty(backupId))
            {
                targetEntry = manifest.FirstOrDefault(m => string.Equals(m.Id, backupId, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                targetEntry = manifest.FirstOrDefault(m => string.Equals(m.ArchivePath, fullPath, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (targetEntry == null)
        {
            throw new InvalidOperationException($"No backup snapshot found for '{archivePath}'{(backupId != null ? $" with ID '{backupId}'" : "")}.");
        }

        string backupXmlPath = Path.Combine(BackupDirectory, targetEntry.BackupXmlFileName);
        if (!File.Exists(backupXmlPath))
        {
            throw new FileNotFoundException($"Backup snapshot file '{targetEntry.BackupXmlFileName}' was not found on disk.");
        }

        string restoredXml = File.ReadAllText(backupXmlPath, Encoding.UTF8);
        var editor = new MetadataEditor();
        editor.UpdateMetadataXml(archivePath, restoredXml, createBackup: false);
        AppLogger.LogInfo($"[MetadataBackupService] Successfully restored backup '{targetEntry.Id}' onto '{archivePath}'.");
        return true;
    }

    /// <summary>
    /// Lists recent batch jobs grouped by BatchJobId.
    /// </summary>
    public IReadOnlyList<BatchJobSummary> ListBatchJobs(int limit = 20)
    {
        lock (Lock)
        {
            var manifest = LoadManifestInternal();
            var batchGroups = manifest
                .Where(m => !string.IsNullOrEmpty(m.BatchJobId))
                .GroupBy(m => m.BatchJobId!)
                .Select(g => new BatchJobSummary(
                    BatchJobId: g.Key,
                    Timestamp: g.First().Timestamp,
                    OperationType: g.First().OperationType,
                    TotalBackups: g.Count(),
                    AffectedFiles: g.Select(m => m.ArchivePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                ))
                .Take(limit)
                .ToList();

            return batchGroups;
        }
    }

    /// <summary>
    /// Atomically rolls back all files belonging to a specific batch job back to their pre-batch snapshots.
    /// Uses staged swaps and a recovery journal to guarantee that either all archives are restored or none are.
    /// Creates a redo snapshot point so the rollback can itself be reversed.
    /// </summary>
    public BatchRollbackReport RestoreBatchJob(string batchJobId)
    {
        if (string.IsNullOrWhiteSpace(batchJobId))
        {
            throw new ArgumentException("BatchJobId cannot be null or empty.", nameof(batchJobId));
        }

        lock (Lock)
        {
            // First recover any orphaned journals from prior interrupted runs
            RecoverPendingBatchJournals();

            var manifest = LoadManifestInternal();
            var allBatchEntries = manifest
                .Where(m => string.Equals(m.BatchJobId, batchJobId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (allBatchEntries.Count == 0)
            {
                throw new InvalidOperationException($"No batch job found with ID '{batchJobId}'.");
            }

            // Group by distinct archive path and select the oldest entry (the true pre-batch state)
            var targetEntries = allBatchEntries
                .GroupBy(m => m.ArchivePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .ToList();

            var failures = new List<(string Path, string Error)>();
            var targetsToRestore = new List<(MetadataBackupEntry Entry, string RestoredXml, string? CurrentXml)>();

            // Phase 1: Pre-flight validation (read-only)
            foreach (var entry in targetEntries)
            {
                if (!File.Exists(entry.ArchivePath))
                {
                    failures.Add((entry.ArchivePath, "File does not exist on disk."));
                    continue;
                }

                string backupXmlPath = Path.Combine(BackupDirectory, entry.BackupXmlFileName);
                if (!File.Exists(backupXmlPath))
                {
                    failures.Add((entry.ArchivePath, $"Backup snapshot file '{entry.BackupXmlFileName}' is missing."));
                    continue;
                }

                string restoredXml;
                try
                {
                    restoredXml = File.ReadAllText(backupXmlPath, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    failures.Add((entry.ArchivePath, $"Failed to read backup snapshot XML: {ex.Message}"));
                    continue;
                }

                string? currentXml = null;
                try
                {
                    currentXml = ComicArchiveHandler.GetRawMetadataXml(entry.ArchivePath);
                }
                catch (Exception ex)
                {
                    failures.Add((entry.ArchivePath, $"Failed to inspect current archive metadata: {ex.Message}"));
                    continue;
                }

                targetsToRestore.Add((entry, restoredXml, currentXml));
            }

            // If ANY file failed validation, abort atomically! Zero files modified.
            if (failures.Count > 0)
            {
                AppLogger.LogWarning($"[MetadataBackupService] Atomic batch rollback aborted for '{batchJobId}': {failures.Count} file(s) failed pre-flight validation.");
                return new BatchRollbackReport(
                    BatchJobId: batchJobId,
                    Total: targetEntries.Count,
                    Restored: 0,
                    Failed: failures.Count,
                    RestoredFiles: new List<string>(),
                    Failures: failures
                );
            }

            // Phase 2: Staging pass (build replacement CBZs in isolated staging dir)
            string stagingId = Guid.NewGuid().ToString("N")[..12];
            string stagingDir = Path.Combine(BackupDirectory, "staging_" + stagingId);
            Directory.CreateDirectory(stagingDir);

            var planItems = new List<BatchRollbackItemPlan>();

            try
            {
                foreach (var item in targetsToRestore)
                {
                    string stagedCbzPath = Path.Combine(stagingDir, $"{item.Entry.Id}.cbz.staged");
                    try
                    {
                        ArchiveSwapService.StageMetadataXml(item.Entry.ArchivePath, item.RestoredXml, stagedCbzPath);
                    }
                    catch (Exception ex)
                    {
                        failures.Add((item.Entry.ArchivePath, $"Failed to stage replacement archive: {ex.Message}"));
                        break;
                    }

                    string guidSuffix = Guid.NewGuid().ToString("N");
                    string backupOriginalPath = item.Entry.ArchivePath + ".orig.bak." + guidSuffix;

                    planItems.Add(new BatchRollbackItemPlan(
                        ArchivePath: item.Entry.ArchivePath,
                        StagedCbzPath: stagedCbzPath,
                        BackupOriginalPath: backupOriginalPath,
                        CurrentXml: item.CurrentXml,
                        RestoredXml: item.RestoredXml,
                        Swapped: false
                    ));
                }

                if (failures.Count > 0)
                {
                    try { Directory.Delete(stagingDir, true); } catch { }

                    AppLogger.LogWarning($"[MetadataBackupService] Atomic batch rollback aborted for '{batchJobId}': staging failed. Zero target files modified.");
                    return new BatchRollbackReport(
                        BatchJobId: batchJobId,
                        Total: targetEntries.Count,
                        Restored: 0,
                        Failed: failures.Count,
                        RestoredFiles: new List<string>(),
                        Failures: failures
                    );
                }

                // Phase 3: Recovery Journal creation
                string journalPath = Path.Combine(BackupDirectory, $"batch_rollback_{batchJobId}_{stagingId}.journal.json");
                var journal = new BatchRollbackJournal(
                    BatchJobId: batchJobId,
                    JournalId: stagingId,
                    Timestamp: DateTime.UtcNow.ToString("o"),
                    State: "Swapping",
                    Items: planItems
                );

                string journalJson = JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true });
                AtomicFile.WriteAllText(journalPath, journalJson, Encoding.UTF8);

                // Phase 4: Atomic two-step swap loop guarded by in-process rollback
                try
                {
                    for (int i = 0; i < planItems.Count; i++)
                    {
                        var plan = planItems[i];
                        File.Move(plan.ArchivePath, plan.BackupOriginalPath);
                        File.Move(plan.StagedCbzPath, plan.ArchivePath);
                        planItems[i] = plan with { Swapped = true };
                    }
                }
                catch (Exception swapEx)
                {
                    AppLogger.LogError($"[MetadataBackupService] Swap failed during batch rollback for '{batchJobId}': {swapEx.Message}. Executing immediate rollback of swapped files...", swapEx);

                    foreach (var plan in planItems)
                    {
                        if (File.Exists(plan.BackupOriginalPath))
                        {
                            try
                            {
                                if (File.Exists(plan.ArchivePath)) File.Delete(plan.ArchivePath);
                                File.Move(plan.BackupOriginalPath, plan.ArchivePath);
                            }
                            catch (Exception revertEx)
                            {
                                AppLogger.LogError($"Failed to revert '{plan.ArchivePath}' from '{plan.BackupOriginalPath}': {revertEx.Message}", revertEx);
                            }
                        }
                    }

                    try { Directory.Delete(stagingDir, true); } catch { }
                    try { File.Delete(journalPath); } catch { }

                    failures.Add((batchJobId, $"Swap failed and was cleanly reverted: {swapEx.Message}"));
                    return new BatchRollbackReport(
                        BatchJobId: batchJobId,
                        Total: targetEntries.Count,
                        Restored: 0,
                        Failed: failures.Count,
                        RestoredFiles: new List<string>(),
                        Failures: failures
                    );
                }

                // Phase 5: Swaps complete! Create Redo point
                string redoBatchJobId = $"redo_{batchJobId}_{stagingId}";
                foreach (var plan in planItems)
                {
                    try
                    {
                        CreateBackup(
                            archivePath: plan.ArchivePath,
                            originalXml: plan.CurrentXml,
                            operationType: "BatchRollbackRedo",
                            batchJobId: redoBatchJobId,
                            changeReason: $"Redo point prior to rolling back batch '{batchJobId}'"
                        );
                    }
                    catch (Exception redoEx)
                    {
                        AppLogger.LogWarning($"Failed to record redo snapshot for '{plan.ArchivePath}': {redoEx.Message}");
                    }
                }

                // Phase 6: Clean up backup original files, staging directory, and journal
                foreach (var plan in planItems)
                {
                    if (File.Exists(plan.BackupOriginalPath))
                    {
                        try { File.Delete(plan.BackupOriginalPath); } catch { }
                    }
                }

                try { Directory.Delete(stagingDir, true); } catch { }
                try { File.Delete(journalPath); } catch { }

                var restoredPaths = planItems.Select(p => p.ArchivePath).ToList();
                AppLogger.LogInfo($"[MetadataBackupService] Atomic batch rollback completed successfully for '{batchJobId}'. Restored {restoredPaths.Count} file(s). Redo Batch ID: '{redoBatchJobId}'.");

                return new BatchRollbackReport(
                    BatchJobId: batchJobId,
                    Total: targetEntries.Count,
                    Restored: restoredPaths.Count,
                    Failed: 0,
                    RestoredFiles: restoredPaths,
                    Failures: new List<(string, string)>(),
                    RedoBatchJobId: redoBatchJobId
                );
            }
            finally
            {
                if (Directory.Exists(stagingDir))
                {
                    try { Directory.Delete(stagingDir, true); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// Scans the backup directory for any pending recovery journals left from an interrupted batch rollback,
    /// and safely reconciles all files back to their consistent pre-rollback state.
    /// </summary>
    public void RecoverPendingBatchJournals()
    {
        lock (Lock)
        {
            if (!Directory.Exists(BackupDirectory)) return;

            string[] journalFiles;
            try
            {
                journalFiles = Directory.GetFiles(BackupDirectory, "batch_rollback_*.journal.json");
            }
            catch
            {
                return;
            }

            foreach (string journalFile in journalFiles)
            {
                try
                {
                    string json = File.ReadAllText(journalFile, Encoding.UTF8);
                    var journal = JsonSerializer.Deserialize<BatchRollbackJournal>(json);
                    if (journal == null || journal.Items == null)
                    {
                        File.Delete(journalFile);
                        continue;
                    }

                    AppLogger.LogWarning($"[MetadataBackupService] Recovering interrupted batch rollback '{journal.BatchJobId}' from journal '{Path.GetFileName(journalFile)}'...");

                    foreach (var item in journal.Items)
                    {
                        if (File.Exists(item.BackupOriginalPath))
                        {
                            try
                            {
                                if (File.Exists(item.ArchivePath))
                                {
                                    File.Delete(item.ArchivePath);
                                }
                                File.Move(item.BackupOriginalPath, item.ArchivePath);
                                AppLogger.LogInfo($"[MetadataBackupService] Restored '{item.ArchivePath}' from '{item.BackupOriginalPath}'.");
                            }
                            catch (Exception ex)
                            {
                                AppLogger.LogError($"[MetadataBackupService] Recovery failed for '{item.ArchivePath}': {ex.Message}", ex);
                            }
                        }

                        if (File.Exists(item.StagedCbzPath))
                        {
                            try { File.Delete(item.StagedCbzPath); } catch { }
                        }
                    }

                    string stagingDir = Path.Combine(BackupDirectory, "staging_" + journal.JournalId);
                    if (Directory.Exists(stagingDir))
                    {
                        try { Directory.Delete(stagingDir, true); } catch { }
                    }

                    File.Delete(journalFile);
                    AppLogger.LogInfo($"[MetadataBackupService] Successfully recovered and cleaned up journal '{Path.GetFileName(journalFile)}'.");
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"[MetadataBackupService] Failed to process recovery journal '{journalFile}': {ex.Message}");
                }
            }
        }
    }

    private void AppendJournalEntry(MetadataBackupEntry entry)
    {
        string jsonLine = JsonSerializer.Serialize(entry);
        File.AppendAllText(JournalPath, jsonLine + Environment.NewLine, Utf8NoBom);
    }

    private int IncrementUncompactedCount()
    {
        if (!UncompactedCounts.TryGetValue(BackupDirectory, out int count))
        {
            try
            {
                count = File.Exists(JournalPath)
                    ? File.ReadLines(JournalPath).Count(l => !string.IsNullOrWhiteSpace(l))
                    : 1;
            }
            catch
            {
                count = 1;
            }
            UncompactedCounts[BackupDirectory] = count;
            return count;
        }

        count++;
        UncompactedCounts[BackupDirectory] = count;
        return count;
    }

    private void ResetUncompactedCount()
    {
        UncompactedCounts[BackupDirectory] = 0;
    }

    private List<MetadataBackupEntry> LoadManifestInternal()
    {
        if (File.Exists(JournalPath))
        {
            return CompactJournalInternal();
        }

        return LoadManifestFileOnly();
    }

    private List<MetadataBackupEntry> LoadManifestFileOnly()
    {
        if (!File.Exists(ManifestPath)) return new List<MetadataBackupEntry>();

        try
        {
            string json = File.ReadAllText(ManifestPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<MetadataBackupEntry>>(json) ?? new List<MetadataBackupEntry>();
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[MetadataBackupService] Failed to load manifest from '{ManifestPath}': {ex.Message}");
            return new List<MetadataBackupEntry>();
        }
    }

    private List<MetadataBackupEntry> LoadJournalEntriesOnly()
    {
        var entries = new List<MetadataBackupEntry>();
        if (!File.Exists(JournalPath)) return entries;

        try
        {
            foreach (string line in File.ReadLines(JournalPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<MetadataBackupEntry>(line);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"[MetadataBackupService] Failed to parse journal entry: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[MetadataBackupService] Failed to read journal from '{JournalPath}': {ex.Message}");
        }

        return entries;
    }

    private List<MetadataBackupEntry> CompactJournalInternal()
    {
        var manifest = LoadManifestFileOnly();
        var journalEntries = LoadJournalEntriesOnly();

        if (journalEntries.Count == 0)
        {
            if (File.Exists(JournalPath))
            {
                try { File.Delete(JournalPath); } catch { }
            }
            ResetUncompactedCount();
            return manifest;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<MetadataBackupEntry>();

        // Journal records are chronological; reverse so newest is at the front
        for (int i = journalEntries.Count - 1; i >= 0; i--)
        {
            var entry = journalEntries[i];
            if (seenIds.Add(entry.Id))
            {
                merged.Add(entry);
            }
        }

        foreach (var entry in manifest)
        {
            if (seenIds.Add(entry.Id))
            {
                merged.Add(entry);
            }
        }

        // Keep manifest at max 1000 items
        if (merged.Count > 1000)
        {
            var excess = merged.Skip(1000).ToList();
            merged = merged.Take(1000).ToList();

            foreach (var old in excess)
            {
                string oldXml = Path.Combine(BackupDirectory, old.BackupXmlFileName);
                if (File.Exists(oldXml))
                {
                    try { File.Delete(oldXml); } catch { /* Ignore */ }
                }
            }
        }

        bool saved = SaveManifestInternal(merged);
        if (saved && File.Exists(JournalPath))
        {
            try
            {
                File.Delete(JournalPath);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[MetadataBackupService] Failed to delete journal file after compaction: {ex.Message}");
            }
        }

        ResetUncompactedCount();
        return merged;
    }

    private bool SaveManifestInternal(List<MetadataBackupEntry> manifest)
    {
        try
        {
            string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            AtomicFile.WriteAllText(ManifestPath, json, Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[MetadataBackupService] Failed to save manifest: {ex.Message}");
            return false;
        }
    }

    private static string ComputeShortHash(string input)
    {
        using var sha = SHA256.Create();
        byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }
}
