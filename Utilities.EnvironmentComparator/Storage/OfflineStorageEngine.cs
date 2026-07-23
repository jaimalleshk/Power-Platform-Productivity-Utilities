using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Utilities.EnvironmentComparator.Engine;
using Utilities.EnvironmentComparator.Models;

namespace Utilities.EnvironmentComparator.Storage
{
    public class SnapshotInfo
    {
        public int SnapshotId { get; set; }
        public string EnvironmentName { get; set; } = string.Empty;
        public DateTime CrawledAt { get; set; }
        public int AdminItemCount { get; set; }
        public int MetadataItemCount { get; set; }
    }

    public class OfflineStorageEngine
    {
        private static string GetConnectionString(string sqlitePath) =>
            $"Data Source={sqlitePath}";

        public void InitializeDatabase(string sqlitePath)
        {
            if (string.IsNullOrWhiteSpace(sqlitePath)) throw new ArgumentNullException(nameof(sqlitePath));

            string? dir = Path.GetDirectoryName(sqlitePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var conn = new SqliteConnection(GetConnectionString(sqlitePath));
            conn.Open();

            string sql = @"
                CREATE TABLE IF NOT EXISTS EnvironmentSnapshots (
                    SnapshotId INTEGER PRIMARY KEY AUTOINCREMENT,
                    EnvironmentName TEXT NOT NULL UNIQUE,
                    CrawledAt TEXT NOT NULL,
                    AdminItemCount INTEGER DEFAULT 0,
                    MetadataItemCount INTEGER DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS AdminSettingsCache (
                    SettingId INTEGER PRIMARY KEY AUTOINCREMENT,
                    SnapshotId INTEGER NOT NULL,
                    SettingKey TEXT NOT NULL,
                    PropertiesJson TEXT NOT NULL,
                    FOREIGN KEY(SnapshotId) REFERENCES EnvironmentSnapshots(SnapshotId) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS MetadataItemsCache (
                    ItemId INTEGER PRIMARY KEY AUTOINCREMENT,
                    SnapshotId INTEGER NOT NULL,
                    ItemKey TEXT NOT NULL,
                    PropertiesJson TEXT NOT NULL,
                    FOREIGN KEY(SnapshotId) REFERENCES EnvironmentSnapshots(SnapshotId) ON DELETE CASCADE
                );
            ";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        public void SaveSnapshot(string sqlitePath, RawEnvData rawData)
        {
            InitializeDatabase(sqlitePath);

            using var conn = new SqliteConnection(GetConnectionString(sqlitePath));
            conn.Open();

            using var tx = conn.BeginTransaction();

            // Insert or Replace Snapshot Record
            string insertSnapshotSql = @"
                INSERT INTO EnvironmentSnapshots (EnvironmentName, CrawledAt, AdminItemCount, MetadataItemCount)
                VALUES (@envName, @crawledAt, @adminCount, @metaCount)
                ON CONFLICT(EnvironmentName) DO UPDATE SET
                    CrawledAt = excluded.CrawledAt,
                    AdminItemCount = excluded.AdminItemCount,
                    MetadataItemCount = excluded.MetadataItemCount;
            ";

            using (var cmd = new SqliteCommand(insertSnapshotSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@envName", rawData.EnvironmentName);
                cmd.Parameters.AddWithValue("@crawledAt", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@adminCount", rawData.AdminSettings.Count);
                cmd.Parameters.AddWithValue("@metaCount", rawData.MetadataItems.Count);
                cmd.ExecuteNonQuery();
            }

            // Fetch SnapshotId
            int snapshotId = 0;
            using (var cmd = new SqliteCommand("SELECT SnapshotId FROM EnvironmentSnapshots WHERE EnvironmentName = @envName;", conn, tx))
            {
                cmd.Parameters.AddWithValue("@envName", rawData.EnvironmentName);
                var result = cmd.ExecuteScalar();
                if (result != null) snapshotId = Convert.ToInt32(result);
            }

            // Clear old cache for this snapshot
            using (var cmd = new SqliteCommand("DELETE FROM AdminSettingsCache WHERE SnapshotId = @id; DELETE FROM MetadataItemsCache WHERE SnapshotId = @id;", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", snapshotId);
                cmd.ExecuteNonQuery();
            }

            // Insert Admin Settings
            foreach (var kvp in rawData.AdminSettings)
            {
                using var cmd = new SqliteCommand("INSERT INTO AdminSettingsCache (SnapshotId, SettingKey, PropertiesJson) VALUES (@id, @key, @json);", conn, tx);
                cmd.Parameters.AddWithValue("@id", snapshotId);
                cmd.Parameters.AddWithValue("@key", kvp.Key);
                cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(kvp.Value));
                cmd.ExecuteNonQuery();
            }

            // Insert Metadata Items
            foreach (var kvp in rawData.MetadataItems)
            {
                using var cmd = new SqliteCommand("INSERT INTO MetadataItemsCache (SnapshotId, ItemKey, PropertiesJson) VALUES (@id, @key, @json);", conn, tx);
                cmd.Parameters.AddWithValue("@id", snapshotId);
                cmd.Parameters.AddWithValue("@key", kvp.Key);
                cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(kvp.Value));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public List<SnapshotInfo> GetSnapshots(string sqlitePath)
        {
            var list = new List<SnapshotInfo>();
            if (!File.Exists(sqlitePath)) return list;

            using var conn = new SqliteConnection(GetConnectionString(sqlitePath));
            conn.Open();

            string sql = "SELECT SnapshotId, EnvironmentName, CrawledAt, AdminItemCount, MetadataItemCount FROM EnvironmentSnapshots ORDER BY EnvironmentName;";
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add(new SnapshotInfo
                {
                    SnapshotId = reader.GetInt32(0),
                    EnvironmentName = reader.GetString(1),
                    CrawledAt = DateTime.TryParse(reader.GetString(2), out var dt) ? dt : DateTime.UtcNow,
                    AdminItemCount = reader.GetInt32(3),
                    MetadataItemCount = reader.GetInt32(4)
                });
            }

            return list;
        }

        public RawEnvData LoadSnapshot(string sqlitePath, string environmentName)
        {
            var rawData = new RawEnvData { EnvironmentName = environmentName };
            if (!File.Exists(sqlitePath)) return rawData;

            using var conn = new SqliteConnection(GetConnectionString(sqlitePath));
            conn.Open();

            int snapshotId = 0;
            using (var cmd = new SqliteCommand("SELECT SnapshotId FROM EnvironmentSnapshots WHERE EnvironmentName = @envName;", conn))
            {
                cmd.Parameters.AddWithValue("@envName", environmentName);
                var result = cmd.ExecuteScalar();
                if (result != null) snapshotId = Convert.ToInt32(result);
            }

            if (snapshotId == 0) return rawData;

            // Load Admin Settings
            using (var cmd = new SqliteCommand("SELECT SettingKey, PropertiesJson FROM AdminSettingsCache WHERE SnapshotId = @id;", conn))
            {
                cmd.Parameters.AddWithValue("@id", snapshotId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string key = reader.GetString(0);
                    string json = reader.GetString(1);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null) rawData.AdminSettings[key] = dict;
                }
            }

            // Load Metadata Items
            using (var cmd = new SqliteCommand("SELECT ItemKey, PropertiesJson FROM MetadataItemsCache WHERE SnapshotId = @id;", conn))
            {
                cmd.Parameters.AddWithValue("@id", snapshotId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string key = reader.GetString(0);
                    string json = reader.GetString(1);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null) rawData.MetadataItems[key] = dict;
                }
            }

            return rawData;
        }

        public ComparisonResult CompareSnapshotsOffline(string sqlitePath, List<string> selectedEnvNames, ComparisonScope scope)
        {
            var envDataList = new List<RawEnvData>();
            foreach (var envName in selectedEnvNames)
            {
                var data = LoadSnapshot(sqlitePath, envName);
                envDataList.Add(data);
            }

            var comparer = new NWayComparer();
            return comparer.CompareEnvironments(envDataList, scope);
        }
    }
}
