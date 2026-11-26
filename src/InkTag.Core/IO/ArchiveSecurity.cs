using System;
using System.Collections.Generic;
using System.IO;
using InkTag.Core.Exceptions;
using SharpCompress.Archives;

namespace InkTag.Core.IO;

/// <summary>
/// Provides security validation routines for archive extraction and zip-slip prevention.
/// </summary>
public static class ArchiveSecurity
{
    public const int MaxArchiveEntries = 10_000;
    public const long MaxArchiveUncompressedBytes = 2L * 1024 * 1024 * 1024; // 2 GiB
    public const long MaxSingleEntryBytes = 256L * 1024 * 1024; // 256 MiB
    public const long MaxMetadataEntryBytes = 16L * 1024 * 1024; // 16 MiB

    /// <summary>
    /// Resolves symlinks/reparse points along the existing portion of a path and returns the canonical full path.
    /// </summary>
    public static string ResolveRealPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        var trailingSegments = new Stack<string>();
        string current = fullPath;

        while (!string.IsNullOrEmpty(current) && !File.Exists(current) && !Directory.Exists(current))
        {
            string? parent = Path.GetDirectoryName(current);
            string name = Path.GetFileName(current);
            if (string.IsNullOrEmpty(parent) || parent == current)
            {
                break;
            }
            if (!string.IsNullOrEmpty(name))
            {
                trailingSegments.Push(name);
            }
            current = parent;
        }

        string resolvedBase = current;
        try
        {
            if (File.Exists(current))
            {
                var target = File.ResolveLinkTarget(current, returnFinalTarget: true);
                if (target != null) resolvedBase = target.FullName;
            }
            else if (Directory.Exists(current))
            {
                var target = Directory.ResolveLinkTarget(current, returnFinalTarget: true);
                if (target != null) resolvedBase = target.FullName;
            }
        }
        catch
        {
            // Fall back to Path.GetFullPath if link resolution is unsupported or inaccessible.
        }

        string result = resolvedBase;
        while (trailingSegments.Count > 0)
        {
            result = Path.Combine(result, trailingSegments.Pop());
        }

        return Path.GetFullPath(result);
    }

    /// <summary>
    /// Returns true if an archive entry is a symlink or hardlink that could escape the extraction directory.
    /// </summary>
    public static bool IsSymlinkEntry(IArchiveEntry entry)
    {
        return !string.IsNullOrEmpty(entry.LinkTarget);
    }

    /// <summary>
    /// Copies from <paramref name="source"/> to <paramref name="destination"/> while enforcing a strict byte limit.
    /// </summary>
    public static long CopyStreamWithLimit(Stream source, Stream destination, long maxBytes, string entryKey, string archiveFilePath)
    {
        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            totalRead += read;
            if (totalRead > maxBytes)
            {
                throw new InvalidDataException(
                    $"Archive entry '{entryKey}' in '{archiveFilePath}' exceeds the maximum allowed uncompressed size of {maxBytes} bytes.");
            }
            destination.Write(buffer, 0, read);
        }
        return totalRead;
    }

    /// <summary>
    /// Safely extracts an archive to <paramref name="destinationDir"/> while enforcing Zip-Slip containment,
    /// symlink rejection, entry count limits, and uncompressed byte limits.
    /// </summary>
    public static void SafeExtractArchive(string archiveFilePath, string destinationDir, System.Threading.CancellationToken cancellationToken = default)
    {
        string canonicalDest = ResolveRealPath(destinationDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        int entryCount = 0;
        long totalBytes = 0;

        using (Stream stream = File.OpenRead(archiveFilePath))
        using (var archive = ArchiveFactory.OpenArchive(stream))
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entryCount++;
                if (entryCount > MaxArchiveEntries)
                {
                    throw new InvalidDataException($"Archive '{archiveFilePath}' exceeds the maximum allowed entry count of {MaxArchiveEntries}.");
                }

                if (IsSymlinkEntry(entry))
                {
                    var unsafeEx = new InvalidDataException($"Archive entry '{entry.Key}' is a symlink/link, which is not permitted.");
                    throw new UnsafeArchiveEntryException(unsafeEx.Message, entry.Key ?? string.Empty, archiveFilePath, unsafeEx);
                }

                if (entry.Key != null && IsUnsafeEntryKey(entry.Key, destinationDir))
                {
                    var unsafeEx = new InvalidDataException($"Archive entry '{entry.Key}' contains invalid path traversal characters.");
                    throw new UnsafeArchiveEntryException(unsafeEx.Message, entry.Key, archiveFilePath, unsafeEx);
                }

                if (!entry.IsDirectory && !string.IsNullOrWhiteSpace(entry.Key))
                {
                    string relativePath = entry.Key.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    string targetPath = Path.GetFullPath(Path.Combine(canonicalDest, relativePath));
                    if (!targetPath.StartsWith(canonicalDest, StringComparison.OrdinalIgnoreCase))
                    {
                        var unsafeEx = new InvalidDataException($"Archive entry '{entry.Key}' escapes extraction target directory.");
                        throw new UnsafeArchiveEntryException(unsafeEx.Message, entry.Key, archiveFilePath, unsafeEx);
                    }

                    string? parentDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    long remainingBudget = MaxArchiveUncompressedBytes - totalBytes;
                    if (remainingBudget <= 0)
                    {
                        throw new InvalidDataException($"Archive '{archiveFilePath}' exceeds the maximum allowed uncompressed size of {MaxArchiveUncompressedBytes} bytes.");
                    }

                    using Stream entryStream = entry.OpenEntryStream();
                    using FileStream outStream = File.Create(targetPath);
                    totalBytes += CopyStreamWithLimit(
                        entryStream,
                        outStream,
                        Math.Min(MaxSingleEntryBytes, remainingBudget),
                        entry.Key,
                        archiveFilePath);
                }
            }
        }

        VerifyDirectoryContainment(destinationDir, archiveFilePath);
    }
    /// <summary>
    /// Checks whether an archive entry key is rooted, contains parent path traversal segments (".."),
    /// or resolves to a location outside the destination directory.
    /// </summary>
    public static bool IsUnsafeEntryKey(string? entryKey, string destinationDir)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
        {
            return false;
        }

        string normalized = entryKey.Replace('\\', '/');

        // Reject rooted paths on any OS (e.g., /evil.txt, \evil.txt, C:\evil.txt)
        if (normalized.StartsWith('/') ||
            Path.IsPathRooted(entryKey) ||
            Path.IsPathRooted(normalized) ||
            (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
        {
            return true;
        }

        // Reject path traversal segments ("..")
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            if (part == "..")
            {
                return true;
            }
        }

        // Verify destination containment via path resolution
        try
        {
            string canonicalDest = Path.GetFullPath(destinationDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string targetPath = Path.GetFullPath(Path.Combine(canonicalDest, entryKey));
            if (!targetPath.StartsWith(canonicalDest, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Post-extraction verification that all extracted files remain strictly inside destinationDir
    /// and that no symlinks/reparse points were created.
    /// </summary>
    public static void VerifyDirectoryContainment(string destinationDir, string archiveFilePath)
    {
        string canonicalDest = ResolveRealPath(destinationDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (string entryPath in Directory.EnumerateFileSystemEntries(destinationDir, "*", SearchOption.AllDirectories))
        {
            if (File.GetAttributes(entryPath).HasFlag(FileAttributes.ReparsePoint))
            {
                var unsafeEx = new InvalidDataException($"Archive entry '{entryPath}' is a symlink or reparse point, which is not permitted.");
                throw new UnsafeArchiveEntryException(unsafeEx.Message, entryPath, archiveFilePath, unsafeEx);
            }

            string canonicalEntry = ResolveRealPath(entryPath);
            if (!canonicalEntry.StartsWith(canonicalDest, StringComparison.OrdinalIgnoreCase))
            {
                var unsafeEx = new InvalidDataException($"Archive entry '{entryPath}' escapes extraction target directory.");
                throw new UnsafeArchiveEntryException(unsafeEx.Message, entryPath, archiveFilePath, unsafeEx);
            }
        }
    }
}
