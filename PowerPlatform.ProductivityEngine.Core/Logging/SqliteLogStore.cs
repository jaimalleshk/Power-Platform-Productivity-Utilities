using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace PowerPlatform.ProductivityEngine.Core.Logging
{
    public static class SqliteLogStore
    {
        private static readonly string DbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerPlatformProductivityEngine",
            "execution_logs.sqlite");

        private static readonly ConcurrentQueue<LogEntry> PendingLogsQueue = new();
        private static readonly CancellationTokenSource Cts = new();

        static SqliteLogStore()
        {
            InitializeDatabase();
            Task.Run(ProcessLogQueueLoopAsync);
        }

        private static void InitializeDatabase()
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
                    );
                    DELETE FROM ExecutionLogs WHERE Id NOT IN (SELECT Id FROM ExecutionLogs ORDER BY Id DESC LIMIT 10000);";
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public static void SaveLog(LogEntry entry)
        {
            if (entry == null) return;
            PendingLogsQueue.Enqueue(entry);
        }

        private static async Task ProcessLogQueueLoopAsync()
        {
            while (!Cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (!PendingLogsQueue.IsEmpty)
                    {
                        var batch = new List<LogEntry>();
                        while (batch.Count < 100 && PendingLogsQueue.TryDequeue(out var entry))
                        {
                            batch.Add(entry);
                        }

                        if (batch.Count > 0)
                        {
                            using var conn = new SqliteConnection($"Data Source={DbPath}");
                            await conn.OpenAsync().ConfigureAwait(false);
                            using var transaction = conn.BeginTransaction();

                            foreach (var log in batch)
                            {
                                using var cmd = conn.CreateCommand();
                                cmd.Transaction = transaction;
                                cmd.CommandText = @"
                                    INSERT INTO ExecutionLogs (Timestamp, Level, Category, Message, ExceptionText)
                                    VALUES (@ts, @lvl, @cat, @msg, @ex);";

                                cmd.Parameters.AddWithValue("@ts", log.Timestamp.ToString("o"));
                                cmd.Parameters.AddWithValue("@lvl", log.Level.ToString());
                                cmd.Parameters.AddWithValue("@cat", log.Category ?? "System");
                                cmd.Parameters.AddWithValue("@msg", log.Message ?? "");
                                cmd.Parameters.AddWithValue("@ex", log.Exception?.ToString() ?? "");

                                cmd.ExecuteNonQuery();
                            }

                            transaction.Commit();
                        }
                    }
                }
                catch { }

                await Task.Delay(200).ConfigureAwait(false);
            }
        }

        public static List<LogEntry> GetLogs(string? levelFilter = null)
        {
            var list = new List<LogEntry>();
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
                    list.Add(new LogEntry
                    {
                        Timestamp = DateTimeOffset.TryParse(reader.GetString(0), out var dt) ? dt : DateTimeOffset.Now,
                        Level = Enum.TryParse<LogLevel>(reader.GetString(1), true, out var lvl) ? lvl : LogLevel.Info,
                        Category = reader.IsDBNull(2) ? "System" : reader.GetString(2),
                        Message = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        Exception = reader.IsDBNull(4) || string.IsNullOrEmpty(reader.GetString(4)) ? null : new Exception(reader.GetString(4))
                    });
                }
            }
            catch { }
            return list;
        }
    }
}
