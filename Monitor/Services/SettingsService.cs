using System;
using System.IO;
using System.Text.Json;
using SystemActivityTracker.Models;

namespace SystemActivityTracker.Services
{
    public class SettingsService
    {
        private const string AppFolderName = "SystemActivityTracker";
        private const string SettingsFileName = "settings.json";

        public AppSettings Load()
        {
            try
            {
                string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appFolder = Path.Combine(baseFolder, AppFolderName);
                string filePath = Path.Combine(appFolder, SettingsFileName);

                if (!File.Exists(filePath))
                {
                    return new AppSettings();
                }

                string json = File.ReadAllText(filePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(baseFolder, AppFolderName);
            Directory.CreateDirectory(appFolder);

            string filePath = Path.Combine(appFolder, SettingsFileName);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(filePath, json);
        }
    }
}
