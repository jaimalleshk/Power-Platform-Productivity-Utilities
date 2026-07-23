using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PowerPlatform.ProductivityEngine.Core.Storage
{
    public class EnvironmentDetailsModel
    {
        public string EnvironmentUrl { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;
        public string SystemUserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string BusinessUnit { get; set; } = string.Empty;
        public string AccessMode { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public List<string> AssignedRoles { get; set; } = new List<string>();
        public Dictionary<string, string> OrgMetadata { get; set; } = new Dictionary<string, string>();
        public DateTimeOffset LastRefreshed { get; set; } = DateTimeOffset.UtcNow;
    }

    public static class EnvironmentDetailsCache
    {
        private static readonly string DbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerPlatformProductivityEngine",
            "env_details_cache.sqlite");

        private static readonly object FileLock = new object();

        static EnvironmentDetailsCache()
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
                        CREATE TABLE IF NOT EXISTS EnvironmentDetails (
                            EnvironmentUrl TEXT PRIMARY KEY,
                            EnvironmentName TEXT,
                            SystemUserId TEXT,
                            FullName TEXT,
                            Email TEXT,
                            BusinessUnit TEXT,
                            AccessMode TEXT,
                            IsAdmin INTEGER,
                            AssignedRolesJson TEXT,
                            OrgMetadataJson TEXT,
                            LastRefreshed TEXT
                        );";
                    cmd.ExecuteNonQuery();
                }
                catch
                {
                    // Non-fatal initialization error
                }
            }
        }

        public static EnvironmentDetailsModel? GetCachedDetails(string environmentUrl)
        {
            if (string.IsNullOrWhiteSpace(environmentUrl)) return null;
            environmentUrl = environmentUrl.TrimEnd('/').ToLowerInvariant();

            lock (FileLock)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={DbPath}");
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT EnvironmentUrl, EnvironmentName, SystemUserId, FullName, Email, BusinessUnit, AccessMode, IsAdmin, AssignedRolesJson, OrgMetadataJson, LastRefreshed FROM EnvironmentDetails WHERE LOWER(EnvironmentUrl) = @url;";
                    cmd.Parameters.AddWithValue("@url", environmentUrl);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        var model = new EnvironmentDetailsModel
                        {
                            EnvironmentUrl = reader.GetString(0),
                            EnvironmentName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            SystemUserId = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            FullName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            Email = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            BusinessUnit = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            AccessMode = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            IsAdmin = reader.GetInt32(7) == 1,
                            AssignedRoles = JsonSerializer.Deserialize<List<string>>(reader.GetString(8)) ?? new List<string>(),
                            OrgMetadata = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(9)) ?? new Dictionary<string, string>(),
                            LastRefreshed = DateTimeOffset.TryParse(reader.GetString(10), out var dt) ? dt : DateTimeOffset.UtcNow
                        };
                        return model;
                    }
                }
                catch
                {
                    // Read error fallback
                }
            }

            return null;
        }

        public static void SaveDetails(EnvironmentDetailsModel model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.EnvironmentUrl)) return;
            string url = model.EnvironmentUrl.TrimEnd('/').ToLowerInvariant();

            lock (FileLock)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={DbPath}");
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO EnvironmentDetails (EnvironmentUrl, EnvironmentName, SystemUserId, FullName, Email, BusinessUnit, AccessMode, IsAdmin, AssignedRolesJson, OrgMetadataJson, LastRefreshed)
                        VALUES (@url, @name, @userId, @fullName, @email, @bu, @accessMode, @isAdmin, @rolesJson, @orgJson, @lastRefreshed)
                        ON CONFLICT(EnvironmentUrl) DO UPDATE SET
                            EnvironmentName = excluded.EnvironmentName,
                            SystemUserId = excluded.SystemUserId,
                            FullName = excluded.FullName,
                            Email = excluded.Email,
                            BusinessUnit = excluded.BusinessUnit,
                            AccessMode = excluded.AccessMode,
                            IsAdmin = excluded.IsAdmin,
                            AssignedRolesJson = excluded.AssignedRolesJson,
                            OrgMetadataJson = excluded.OrgMetadataJson,
                            LastRefreshed = excluded.LastRefreshed;";

                    cmd.Parameters.AddWithValue("@url", url);
                    cmd.Parameters.AddWithValue("@name", model.EnvironmentName);
                    cmd.Parameters.AddWithValue("@userId", model.SystemUserId);
                    cmd.Parameters.AddWithValue("@fullName", model.FullName);
                    cmd.Parameters.AddWithValue("@email", model.Email);
                    cmd.Parameters.AddWithValue("@bu", model.BusinessUnit);
                    cmd.Parameters.AddWithValue("@accessMode", model.AccessMode);
                    cmd.Parameters.AddWithValue("@isAdmin", model.IsAdmin ? 1 : 0);
                    cmd.Parameters.AddWithValue("@rolesJson", JsonSerializer.Serialize(model.AssignedRoles ?? new List<string>()));
                    cmd.Parameters.AddWithValue("@orgJson", JsonSerializer.Serialize(model.OrgMetadata ?? new Dictionary<string, string>()));
                    cmd.Parameters.AddWithValue("@lastRefreshed", DateTimeOffset.UtcNow.ToString("o"));

                    cmd.ExecuteNonQuery();
                }
                catch
                {
                    // Non-fatal write error
                }
            }
        }

        public static void ClearAllCache()
        {
            lock (FileLock)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={DbPath}");
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "DELETE FROM EnvironmentDetails;";
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }
    }
}
