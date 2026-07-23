using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Utilities.EnvironmentComparator.Storage
{
    public static class SqliteComparisonStore
    {
        private static readonly string DbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerPlatformProductivityEngine",
            "env_comparison_cache.sqlite");

        private static readonly object FileLock = new object();

        static SqliteComparisonStore()
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
                        CREATE TABLE IF NOT EXISTS ComponentChunkCache (
                            EnvironmentUrl TEXT,
                            Category TEXT,
                            ComponentKey TEXT,
                            DataJson TEXT,
                            LastRefreshed TEXT,
                            PRIMARY KEY (EnvironmentUrl, Category, ComponentKey)
                        );";
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        public static void SaveChunk(string envUrl, string category, string componentKey, Dictionary<string, string> data)
        {
            if (string.IsNullOrWhiteSpace(envUrl) || string.IsNullOrWhiteSpace(componentKey)) return;
            envUrl = envUrl.TrimEnd('/').ToLowerInvariant();

            lock (FileLock)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={DbPath}");
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO ComponentChunkCache (EnvironmentUrl, Category, ComponentKey, DataJson, LastRefreshed)
                        VALUES (@url, @cat, @key, @json, @ts)
                        ON CONFLICT(EnvironmentUrl, Category, ComponentKey) DO UPDATE SET
                            DataJson = excluded.DataJson,
                            LastRefreshed = excluded.LastRefreshed;";

                    cmd.Parameters.AddWithValue("@url", envUrl);
                    cmd.Parameters.AddWithValue("@cat", category ?? "Default");
                    cmd.Parameters.AddWithValue("@key", componentKey);
                    cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(data ?? new Dictionary<string, string>()));
                    cmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToString("o"));

                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        public static Dictionary<string, string>? GetCachedComponent(string envUrl, string category, string componentKey)
        {
            if (string.IsNullOrWhiteSpace(envUrl) || string.IsNullOrWhiteSpace(componentKey)) return null;
            envUrl = envUrl.TrimEnd('/').ToLowerInvariant();

            lock (FileLock)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={DbPath}");
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT DataJson FROM ComponentChunkCache WHERE LOWER(EnvironmentUrl) = @url AND Category = @cat AND ComponentKey = @key;";
                    cmd.Parameters.AddWithValue("@url", envUrl);
                    cmd.Parameters.AddWithValue("@cat", category ?? "Default");
                    cmd.Parameters.AddWithValue("@key", componentKey);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read() && !reader.IsDBNull(0))
                    {
                        string json = reader.GetString(0);
                        return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    }
                }
                catch { }
            }

            return null;
        }
    }
}
