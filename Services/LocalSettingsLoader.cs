using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public static class LocalSettingsLoader
    {
        private const string DefaultRelativePath = "Config/local.settings.json";
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true
        };
        private static readonly JsonDocumentOptions ReadNodeOptions = new()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        public static AppConfig Load(string? path = null)
        {
            string settingsPath = string.IsNullOrWhiteSpace(path)
                ? ResolveSettingsPath()
                : path;

            if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
            {
                throw new FileNotFoundException(
                    "local.settings.json was not found. Check the Config folder in the project root.",
                    settingsPath ?? DefaultRelativePath);
            }

            string json = File.ReadAllText(settingsPath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            AppConfig config = JsonSerializer.Deserialize<AppConfig>(json, options) ?? new AppConfig();
            return config;
        }

        public static void SaveStrategyMinutePreloadIdleDelaySeconds(int seconds, string? path = null)
        {
            string settingsPath = string.IsNullOrWhiteSpace(path)
                ? ResolveSettingsPath()
                : path;

            if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
                return;

            JsonNode root = JsonNode.Parse(File.ReadAllText(settingsPath), documentOptions: ReadNodeOptions) ?? new JsonObject();
            if (root is not JsonObject rootObject)
                return;

            if (rootObject["StrategyMinutePreload"] is not JsonObject preloadObject)
            {
                preloadObject = [];
                rootObject["StrategyMinutePreload"] = preloadObject;
            }

            preloadObject["IdleDelaySeconds"] = Math.Clamp(seconds, 5, 3600);
            File.WriteAllText(settingsPath, rootObject.ToJsonString(WriteOptions));
        }

        private static string ResolveSettingsPath()
        {
            // Check Config/local.settings.json under the executable folder.
            string basePath = Path.Combine(AppContext.BaseDirectory, DefaultRelativePath);
            if (File.Exists(basePath))
                return basePath;

            // Check Config/local.settings.json under the current working folder.
            string currentPath = Path.Combine(Directory.GetCurrentDirectory(), DefaultRelativePath);
            if (File.Exists(currentPath))
                return currentPath;

            // Walk up from bin/Debug/... to the project root.
            string foundFromBase = SearchUpwards(AppContext.BaseDirectory, DefaultRelativePath);
            if (!string.IsNullOrWhiteSpace(foundFromBase))
                return foundFromBase;

            string foundFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), DefaultRelativePath);
            if (!string.IsNullOrWhiteSpace(foundFromCurrent))
                return foundFromCurrent;

            return currentPath;
        }

        private static string SearchUpwards(string startDirectory, string relativePath)
        {
            DirectoryInfo? directory = new(startDirectory);

            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }

            return string.Empty;
        }
    }
}
