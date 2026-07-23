using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Identity.Client;

namespace PowerPlatform.ProductivityEngine.Core.Authentication
{
    public static class MsalTokenCacheHelper
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerPlatformProductivityEngine");

        private static readonly string CacheFilePath = Path.Combine(AppDataFolder, "msal_user_cache.dat");
        private static readonly object FileLock = new object();

        public static void EnableSerialization(ITokenCache tokenCache)
        {
            tokenCache.SetBeforeAccess(BeforeAccessNotification);
            tokenCache.SetAfterAccess(AfterAccessNotification);
        }

        private static void BeforeAccessNotification(TokenCacheNotificationArgs args)
        {
            lock (FileLock)
            {
                if (File.Exists(CacheFilePath))
                {
                    try
                    {
                        byte[] encryptedData = File.ReadAllBytes(CacheFilePath);
                        byte[] decryptedData = ProtectedData.Unprotect(
                            encryptedData,
                            null,
                            DataProtectionScope.CurrentUser);

                        args.TokenCache.DeserializeMsalV3(decryptedData);
                    }
                    catch
                    {
                        // Ignore corrupt cache files and let MSAL start fresh
                    }
                }
            }
        }

        private static void AfterAccessNotification(TokenCacheNotificationArgs args)
        {
            if (args.HasStateChanged)
            {
                lock (FileLock)
                {
                    try
                    {
                        if (!Directory.Exists(AppDataFolder))
                        {
                            Directory.CreateDirectory(AppDataFolder);
                        }

                        byte[] decryptedData = args.TokenCache.SerializeMsalV3();
                        byte[] encryptedData = ProtectedData.Protect(
                            decryptedData,
                            null,
                            DataProtectionScope.CurrentUser);

                        File.WriteAllBytes(CacheFilePath, encryptedData);
                    }
                    catch
                    {
                        // Ignore non-fatal cache write errors
                    }
                }
            }
        }
    }
}
