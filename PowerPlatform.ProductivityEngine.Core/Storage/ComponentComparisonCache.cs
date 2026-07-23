using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PowerPlatform.ProductivityEngine.Core.Storage
{
    public class CachedComponentItem
    {
        public string EnvironmentUrl { get; set; } = string.Empty;
        public string ComponentKey { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string PropertiesJson { get; set; } = string.Empty;
        public string RawContent { get; set; } = string.Empty;
        public DateTimeOffset LastSynced { get; set; } = DateTimeOffset.UtcNow;
    }

    public static class ComponentComparisonCache
    {
        private static readonly string DbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerPlatformProductivityEngine",
            "component_comparison_cache.sqlite");

        private static readonly object FileLock = new object();

        static ComponentComparisonCache()
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
                        CREATE TABLE IF NOT EXISTS ComponentCache (
                            EnvironmentUrl TEXT,
                            ComponentKey TEXT,
                            Category TEXT,
                            PropertiesJson TEXT,
                            RawContent TEXT,
                            LastSynced TEXT,
                            PRIMARY KEY (EnvironmentUrl, ComponentKey)
                        );";
                    cmd.ExecuteNonQuery();
                }
                catch
                {
                    // Non-fatal initialization fallback
                }
            }
        }

        public static void SaveChunkInTransaction(IEnumerable<CachedComponentItem> items)
        {
            if (items == null) return;
            lock (FileLock)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={DbPath}");
                    conn.Open();
                    using var transaction = conn.BeginTransaction();

                    foreach (var item in items)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = transaction;
                        cmd.CommandText = @"
                            INSERT OR REPLACE INTO ComponentCache 
                            (EnvironmentUrl, ComponentKey, Category, PropertiesJson, RawContent, LastSynced)
                            VALUES (@env, @key, @cat, @props, @raw, @synced);";

                        cmd.Parameters.AddWithValue("@env", item.EnvironmentUrl);
                        cmd.Parameters.AddWithValue("@key", item.ComponentKey);
                        cmd.Parameters.AddWithValue("@cat", item.Category ?? "");
                        cmd.Parameters.AddWithValue("@props", item.PropertiesJson ?? "{}");
                        cmd.Parameters.AddWithValue("@raw", item.RawContent ?? "");
                        cmd.Parameters.AddWithValue("@synced", item.LastSynced.ToString("o"));

                        cmd.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    Logging.AppLogger.LogError("SQLiteCache", $"Failed to commit component chunk into SQLite: {ex.Message}", ex);
                }
            }
        }

        public static CachedComponentItem? GetCachedComponent(string environmentUrl, string componentKey)
        {
            lock (FileLock)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={DbPath}");
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT EnvironmentUrl, ComponentKey, Category, PropertiesJson, RawContent, LastSynced FROM ComponentCache WHERE EnvironmentUrl = @env AND ComponentKey = @key;";
                    cmd.Parameters.AddWithValue("@env", environmentUrl);
                    cmd.Parameters.AddWithValue("@key", componentKey);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        return new CachedComponentItem
                        {
                            EnvironmentUrl = reader.GetString(0),
                            ComponentKey = reader.GetString(1),
                            Category = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            PropertiesJson = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            RawContent = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            LastSynced = DateTimeOffset.TryParse(reader.IsDBNull(5) ? "" : reader.GetString(5), out var d) ? d : DateTimeOffset.UtcNow
                        };
                    }
                }
                catch { }
                return null;
            }
        }
    }
}
