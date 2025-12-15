using System;
using System.Globalization;
using System.IO;
using System.Text;
using SystemActivityTracker.Models;

namespace SystemActivityTracker.Services
{
    public class ActivityLogWriter
    {
        private const string AppFolderName = "SystemActivityTracker";

        public void AppendRecord(ActivityRecord record)
        {
            if (record.StartTime == default)
            {
                return;
            }

            string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(baseFolder, AppFolderName);
            Directory.CreateDirectory(appFolder);

            string fileName = $"activity-log-{record.StartTime:yyyy-MM-dd}.csv";
            string filePath = Path.Combine(appFolder, fileName);

            bool fileExists = File.Exists(filePath);

            using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, Encoding.UTF8);

            if (!fileExists)
            {
                writer.WriteLine("StartTime,EndTime,ProcessName,WindowTitle,IsLocked,IsIdle");
            }

            string startTime = record.StartTime.ToString("o", CultureInfo.InvariantCulture);
            string endTime = (record.EndTime ?? DateTime.Now).ToString("o", CultureInfo.InvariantCulture);

            string processName = EscapeCsv(record.ProcessName);
            string windowTitle = EscapeCsv(record.WindowTitle);

            writer.WriteLine($"{startTime},{endTime},{processName},{windowTitle},{record.IsLocked},{record.IsIdle}");
        }

        private static string EscapeCsv(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            bool mustQuote = input.Contains(',') || input.Contains('"') || input.Contains('\n') || input.Contains('\r');
            if (!mustQuote)
            {
                return input;
            }

            string escaped = input.Replace("\"", "\"\"");
            return "\"" + escaped + "\"";
        }
    }
}
