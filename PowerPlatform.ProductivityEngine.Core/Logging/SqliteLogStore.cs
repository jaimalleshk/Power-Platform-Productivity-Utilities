using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace PowerPlatform.ProductivityEngine.Core.Logging
{
    public static class SqliteLogStore
    {
        private static readonly string DbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerPlatformProductivityEngine",
            "execution_logs.sqlite");

        private static readonly object FileLock = new object();

        static SqliteLogStore()
        {
            InitializeDatabase();
        }

        private static void InitializeDatabase()
        {
            lock (FileLock)
            {
                try
                {
                    string folder = Path.GetDirectoryName(DbPath) ?? "";
                    if (!Directory.Exists(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }

                    using var conn = new SqliteConnection($"Data Source={DbPath}");
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS ExecutionLogs (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Timestamp TEXT,
                            Level TEXT,
                            Category TEXT,
                            Message TEXT,
                            ExceptionText TEXT
                        );";
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        public static void SaveLog(LogEntry entry)
        {
            if (entry == null) return;

            lock (FileLock)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={DbPath}");
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO ExecutionLogs (Timestamp, Level, Category, Message, ExceptionText)
                        VALUES (@ts, @lvl, @cat, @msg, @ex);";

                    cmd.Parameters.AddWithValue("@ts", entry.Timestamp.ToString("o"));
                    cmd.Parameters.AddWithValue("@lvl", entry.Level.ToString());
                    cmd.Parameters.AddWithValue("@cat", entry.Category ?? "System");
                    cmd.Parameters.AddWithValue("@msg", entry.Message ?? "");
                    cmd.Parameters.AddWithValue("@ex", entry.Exception?.ToString() ?? "");

                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        public static List<LogEntry> GetLogs(string? levelFilter = null)
        {
            var list = new List<LogEntry>();
            lock (FileLock)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={DbPath}");
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    if (!string.IsNullOrWhiteSpace(levelFilter) && !levelFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
                    {
                        cmd.CommandText = "SELECT Timestamp, Level, Category, Message, ExceptionText FROM ExecutionLogs WHERE Level = @lvl ORDER BY Id DESC LIMIT 1000;";
                        cmd.Parameters.AddWithValue("@lvl", levelFilter);
                    }
                    else
                    {
                        cmd.CommandText = "SELECT Timestamp, Level, Category, Message, ExceptionText FROM ExecutionLogs ORDER BY Id DESC LIMIT 2000;";
                    }

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string tsStr = reader.GetString(0);
                        string lvlStr = reader.GetString(1);
                        string cat = reader.GetString(2);
                        string msg = reader.GetString(3);
                        string exText = reader.IsDBNull(4) ? "" : reader.GetString(4);

                        Enum.TryParse<LogLevel>(lvlStr, true, out var lvl);
                        DateTimeOffset.TryParse(tsStr, out var ts);

                        list.Add(new LogEntry
                        {
                            Timestamp = ts,
                            Level = lvl,
                            Category = cat,
                            Message = msg,
                            Exception = string.IsNullOrEmpty(exText) ? null : new Exception(exText)
                        });
                    }
                }
                catch { }
            }
            return list;
        }

        public static void ClearLogs()
        {
            lock (FileLock)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={DbPath}");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "DELETE FROM ExecutionLogs;";
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }
    }
}
