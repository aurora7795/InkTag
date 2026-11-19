using System;
using System.IO;
using InkTag.Core.Exceptions;

namespace InkTag.Core.IO;

/// <summary>
/// Provides security validation routines for archive extraction and zip-slip prevention.
/// </summary>
public static class ArchiveSecurity
{
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
    /// Post-extraction verification that all extracted files remain strictly inside destinationDir.
    /// </summary>
    public static void VerifyDirectoryContainment(string destinationDir, string archiveFilePath)
    {
        string canonicalDest = Path.GetFullPath(destinationDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (string extractedFile in Directory.GetFiles(destinationDir, "*", SearchOption.AllDirectories))
        {
            string canonicalFile = Path.GetFullPath(extractedFile);
            if (!canonicalFile.StartsWith(canonicalDest, StringComparison.OrdinalIgnoreCase))
            {
                var unsafeEx = new InvalidDataException($"Archive entry '{extractedFile}' escapes extraction target directory.");
                throw new UnsafeArchiveEntryException(unsafeEx.Message, extractedFile, archiveFilePath, unsafeEx);
            }
        }
    }
}
