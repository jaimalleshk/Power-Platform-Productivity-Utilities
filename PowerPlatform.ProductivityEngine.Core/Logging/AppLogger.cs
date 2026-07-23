using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace PowerPlatform.ProductivityEngine.Core.Logging
{
    public enum LogLevel
    {
        Debug,
        Info,
        Success,
        Warning,
        Error,
        Progress
    }

    public class LogEntry
    {
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
        public LogLevel Level { get; set; } = LogLevel.Info;
        public string Category { get; set; } = "System";
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
        public int ProgressCurrent { get; set; }
        public int ProgressTotal { get; set; }

        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");

        public string DisplayText => Exception != null
            ? $"[{FormattedTimestamp}] [{Level.ToString().ToUpper()}] [{Category}] {Message} (Error: {Exception.Message})"
            : $"[{FormattedTimestamp}] [{Level.ToString().ToUpper()}] [{Category}] {Message}";
    }

    public static class AppLogger
    {
        private static readonly ConcurrentQueue<LogEntry> LogBuffer = new();
        private const int MaxBufferSize = 5000;

        public static event EventHandler<LogEntry>? OnLogReceived;

        public static void Log(LogLevel level, string category, string message, Exception? ex = null, int current = 0, int total = 0)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTimeOffset.Now,
                Level = level,
                Category = string.IsNullOrWhiteSpace(category) ? "System" : category,
                Message = message,
                Exception = ex,
                ProgressCurrent = current,
                ProgressTotal = total
            };

            LogBuffer.Enqueue(entry);
            while (LogBuffer.Count > MaxBufferSize)
            {
                LogBuffer.TryDequeue(out _);
            }

            try
            {
                OnLogReceived?.Invoke(null, entry);
            }
            catch { }
        }

        public static void LogInfo(string category, string message) => Log(LogLevel.Info, category, message);
        public static void LogSuccess(string category, string message) => Log(LogLevel.Success, category, message);
        public static void LogWarning(string category, string message) => Log(LogLevel.Warning, category, message);
        public static void LogError(string category, string message, Exception? ex = null) => Log(LogLevel.Error, category, message, ex);
        public static void LogProgress(string category, int current, int total, string message) => Log(LogLevel.Progress, category, message, null, current, total);
        public static void LogDebug(string category, string message) => Log(LogLevel.Debug, category, message);

        public static List<LogEntry> GetRecentLogs() => LogBuffer.ToList();

        public static void Clear()
        {
            while (LogBuffer.TryDequeue(out _)) { }
        }
    }
}
