using System;
using System.IO;
using System.Text;

namespace InkTag.Core.Logging;

/// <summary>
/// Thread-safe cross-platform logger writing to standard OS local application data path and console/debug outputs.
/// </summary>
public static class AppLogger
{
    private static readonly object LogLock = new();
    private static string? _logFilePath;
    private static StreamWriter? _logWriter;
    private static string? _writerFilePath;
    private static long _currentFileSizeBytes;

    static AppLogger()
    {
        try
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => CloseWriter();
        }
        catch
        {
            // Ignore environment hooks failure
        }
    }

    /// <summary>
    /// Gets the current log file path.
    /// </summary>
    public static string LogFilePath => _logFilePath ??= GetDefaultLogFilePath();

    /// <summary>
    /// Initializes the logger. Can be called with a custom path (e.g. for unit tests).
    /// </summary>
    public static void Initialize(string? customLogPath = null)
    {
        lock (LogLock)
        {
            CloseWriterInternal();
            _logFilePath = customLogPath ?? GetDefaultLogFilePath();

            var dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        LogInfo("=========================================");
        LogInfo($"AppLogger initialized. OS: {Environment.OSVersion}, Runtime: {Environment.Version}");
        LogInfo($"Log File Path: {LogFilePath}");
    }

    /// <summary>
    /// Resolves the default cross-platform log file path.
    /// Linux: ~/.local/share/InkTag/logs/InkTag.log
    /// Windows: %LocalAppData%\InkTag\logs\InkTag.log
    /// macOS: ~/Library/Application Support/InkTag/logs/InkTag.log
    /// </summary>
    public static string GetDefaultLogFilePath()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }
        return Path.Combine(baseDir, "InkTag", "logs", "InkTag.log");
    }

    /// <summary>
    /// Gets or sets whether verbose / debug logging is active.
    /// </summary>
    public static bool IsDebugEnabled { get; set; } = false;

    public static void LogInfo(string message) => Log("INFO", message);
    public static void LogWarning(string message) => Log("WARN", message);
    public static void LogDebug(string message)
    {
        if (IsDebugEnabled)
        {
            Log("DEBUG", message);
        }
    }
    
    public static void LogError(string message, Exception? ex = null)
    {
        string fullMessage = ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
        Log("ERROR", fullMessage);
    }

    public const long MaxLogFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private static void RotateLogIfNeeded(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length >= MaxLogFileSizeBytes)
                {
                    CloseWriterInternal();
                    string backupPath = path + ".bak";
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                    File.Move(path, backupPath);
                }
            }
        }
        catch
        {
            // Ignore rotation errors
        }
    }

    private static void EnsureWriterOpen(string path)
    {
        if (_logWriter != null && string.Equals(_writerFilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CloseWriterInternal();

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        RotateLogIfNeeded(path);

        var fileStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _logWriter = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = true };
        _writerFilePath = path;
        _currentFileSizeBytes = fileStream.Length;
    }

    /// <summary>
    /// Flushes and closes the active log writer if open.
    /// </summary>
    public static void CloseWriter()
    {
        lock (LogLock)
        {
            CloseWriterInternal();
        }
    }

    private static void CloseWriterInternal()
    {
        if (_logWriter != null)
        {
            try
            {
                _logWriter.Flush();
                _logWriter.Dispose();
            }
            catch
            {
                // Suppress disposal exceptions
            }
            finally
            {
                _logWriter = null;
                _writerFilePath = null;
                _currentFileSizeBytes = 0;
            }
        }
    }

    private static void Log(string level, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string formattedLine = $"[{timestamp}] [{level}] {message}";

        // Diagnostics go to stderr, never stdout: the MCP server speaks JSON-RPC over stdout
        // and the CLI reserves stdout for its own machine-readable output.
        Console.Error.WriteLine(formattedLine);
        System.Diagnostics.Debug.WriteLine(formattedLine);

        try
        {
            lock (LogLock)
            {
                string path = LogFilePath;
                EnsureWriterOpen(path);

                if (_logWriter != null)
                {
                    _logWriter.WriteLine(formattedLine);
                    _currentFileSizeBytes += Encoding.UTF8.GetByteCount(formattedLine) + Environment.NewLine.Length;

                    if (_currentFileSizeBytes >= MaxLogFileSizeBytes)
                    {
                        CloseWriterInternal();
                        RotateLogIfNeeded(path);
                    }
                }
            }
        }
        catch
        {
            // Fallback gracefully if logging fails (e.g. read-only filesystem)
        }
    }

    /// <summary>
    /// Opens the log folder in the default OS file manager cross-platform (Linux: xdg-open, Windows: explorer, macOS: open).
    /// </summary>
    public static void OpenLogFolder()
    {
        string logFile = LogFilePath;
        string dir = Path.GetDirectoryName(logFile) ?? logFile;

        try
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (OperatingSystem.IsWindows())
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = false
                };
                if (File.Exists(logFile))
                {
                    psi.ArgumentList.Add($"/select,{logFile}");
                }
                else
                {
                    psi.ArgumentList.Add(dir);
                }
                System.Diagnostics.Process.Start(psi);
            }
            else if (OperatingSystem.IsMacOS())
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    UseShellExecute = false
                };
                if (File.Exists(logFile))
                {
                    psi.ArgumentList.Add("-R");
                    psi.ArgumentList.Add(logFile);
                }
                else
                {
                    psi.ArgumentList.Add(dir);
                }
                System.Diagnostics.Process.Start(psi);
            }
            else // Linux & Unix
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false
                };
                psi.ArgumentList.Add(dir);
                System.Diagnostics.Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            LogError("Failed to open log folder in system file manager.", ex);
        }
    }
}
