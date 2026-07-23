using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerPlatform.ProductivityEngine.Core.Authentication
{
    public class UserSettingsModel
    {
        public string LastUsedUsername { get; set; } = string.Empty;
        public List<string> SavedUsernames { get; set; } = new List<string>();
        public bool AutoConnectOnStartup { get; set; } = true;
    }

    public static class UserSettingsManager
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerPlatformProductivityEngine");

        private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "user_settings.json");

        public static UserSettingsModel LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<UserSettingsModel>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch
            {
                // Fallback to defaults on error
            }

            return new UserSettingsModel();
        }

        public static void SaveUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return;

            username = username.Trim();
            var settings = LoadSettings();
            settings.LastUsedUsername = username;

            if (!settings.SavedUsernames.Any(u => u.Equals(username, StringComparison.OrdinalIgnoreCase)))
            {
                settings.SavedUsernames.Insert(0, username);
            }
            else
            {
                // Move to top of history
                settings.SavedUsernames.RemoveAll(u => u.Equals(username, StringComparison.OrdinalIgnoreCase));
                settings.SavedUsernames.Insert(0, username);
            }

            // Keep top 10 most recent
            if (settings.SavedUsernames.Count > 10)
            {
                settings.SavedUsernames = settings.SavedUsernames.Take(10).ToList();
            }

            SaveSettings(settings);
        }

        public static void SaveSettings(UserSettingsModel settings)
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Non-fatal write error
            }
        }
    }
}
