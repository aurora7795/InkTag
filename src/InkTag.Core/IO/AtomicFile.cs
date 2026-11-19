using System;
using System.IO;
using System.Text;

namespace InkTag.Core.IO;

/// <summary>
/// Provides atomic file write operations by staging content to a temporary sibling file
/// before atomically replacing the destination file.
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// Writes text content to a destination file atomically.
    /// The content is first written to a temporary sibling file (<c>path + ".tmp-" + Guid</c>)
    /// and then swapped into place using <see cref="File.Move(string, string, bool)"/> with overwrite enabled.
    /// </summary>
    /// <param name="path">The destination file path.</param>
    /// <param name="content">The text content to write.</param>
    /// <param name="encoding">The character encoding to use (defaults to UTF-8 if null).</param>
    public static void WriteAllText(string path, string content, Encoding? encoding = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{path}.tmp-{Guid.NewGuid():N}";
        encoding ??= Encoding.UTF8;

        try
        {
            File.WriteAllText(tempPath, content, encoding);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Suppress secondary cleanup exceptions to preserve primary fault
            }

            throw;
        }
    }

    /// <summary>
    /// Writes binary bytes to a destination file atomically.
    /// </summary>
    /// <param name="path">The destination file path.</param>
    /// <param name="bytes">The byte array to write.</param>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }
        ArgumentNullException.ThrowIfNull(bytes);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{path}.tmp-{Guid.NewGuid():N}";

        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Suppress secondary cleanup exceptions
            }

            throw;
        }
    }
}
