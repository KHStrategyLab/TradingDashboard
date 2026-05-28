using System;
using System.IO;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public static class LocalSettingsLoader
    {
        private const string DefaultRelativePath = "Config/local.settings.json";

        public static AppConfig Load(string path = null)
        {
            string settingsPath = string.IsNullOrWhiteSpace(path)
                ? ResolveSettingsPath()
                : path;

            if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
            {
                throw new FileNotFoundException(
                    "local.settings.json 파일을 찾을 수 없습니다. 프로젝트 루트의 Config 폴더를 확인하세요.",
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

        private static string ResolveSettingsPath()
        {
            // 1. 실행 파일 기준 bin 폴더 아래 Config/local.settings.json
            string basePath = Path.Combine(AppContext.BaseDirectory, DefaultRelativePath);
            if (File.Exists(basePath))
                return basePath;

            // 2. 현재 작업 폴더 기준 Config/local.settings.json
            string currentPath = Path.Combine(Directory.GetCurrentDirectory(), DefaultRelativePath);
            if (File.Exists(currentPath))
                return currentPath;

            // 3. bin/Debug/... 에서 프로젝트 루트까지 거슬러 올라가며 검색
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
            DirectoryInfo directory = new DirectoryInfo(startDirectory);

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
