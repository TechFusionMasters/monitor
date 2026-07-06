using System;
using System.IO;

namespace SystemActivityTracker.Utilities
{
    internal static class AppPaths
    {
        private const string AppFolderName = "SystemActivityTracker";
        private const string LogsFolderName = "logs";
        private const string LastRunFileName = "LastRun.json";
        private const string LegacyCloseEventsFileName = "close-events.jsonl";
        private const string DailyCloseEventsFilePrefix = "close-events-";

        public static string GetAppFolder()
        {
            string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(baseFolder, AppFolderName);
            Directory.CreateDirectory(appFolder);
            return appFolder;
        }

        public static string GetLogsFolder()
        {
            string logsFolder = Path.Combine(GetAppFolder(), LogsFolderName);
            Directory.CreateDirectory(logsFolder);
            return logsFolder;
        }

        public static string GetLastRunPath() => Path.Combine(GetAppFolder(), LastRunFileName);

        public static string GetLegacyCloseEventsPath() => Path.Combine(GetAppFolder(), LegacyCloseEventsFileName);

        public static string GetDailyCloseEventsFilePrefix() => DailyCloseEventsFilePrefix;

        public static string GetDailyCloseEventsPath(DateTime date)
        {
            string name = $"{DailyCloseEventsFilePrefix}{date:yyyy-MM-dd}.jsonl";
            return Path.Combine(GetLogsFolder(), name);
        }

        public static string GetSettingsPath() => Path.Combine(GetAppFolder(), "settings.json");

        public static string GetManualTasksPath(DateTime date)
        {
            string fileName = $"manual-tasks-{date:yyyy-MM-dd}.json";
            return Path.Combine(GetAppFolder(), fileName);
        }

        public static string GetLeavesPath(int year, int month)
        {
            string fileName = $"leaves-{year:0000}-{month:00}.json";
            return Path.Combine(GetAppFolder(), fileName);
        }

        public static string GetHolidaysPath(int year)
        {
            string fileName = $"holidays-{year:0000}.json";
            return Path.Combine(GetAppFolder(), fileName);
        }

        public static string GetAppCategoriesPath() => Path.Combine(GetAppFolder(), "app-categories.json");

        public static string GetActivityLogCsvPath(DateTime date)
        {
            string fileName = $"activity-log-{date:yyyy-MM-dd}.csv";
            return Path.Combine(GetAppFolder(), fileName);
        }
    }
}
