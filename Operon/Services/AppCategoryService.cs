using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using SystemActivityTracker.Models;
using SystemActivityTracker.Services.Abstractions;
using SystemActivityTracker.Utilities;

namespace SystemActivityTracker.Services
{
    // Persists application categories and their process-name mappings, and resolves
    // which category a given process belongs to (falling back to "Others"). Shared by
    // the Settings category-management UI and the Application Usage breakdown so both
    // always see the same mapping.
    public class AppCategoryService
    {
        public const string OthersCategoryName = "Others";

        private static readonly string[] DefaultCategoryNames =
        {
            "Development", "Browser", "Communication", "Office", "Design",
            "Database", "Terminal", "Utilities", "Media", "Virtual Machine"
        };

        public List<AppCategory> LoadAll()
        {
            try
            {
                string path = AppPaths.GetAppCategoriesPath();
                var categories = JsonFile.LoadOrDefault(path, CreateDefaults);
                EnsureOthersExists(categories);
                return categories;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppCategories] Load failed: {ex}");
                return CreateDefaults();
            }
        }

        public void SaveAll(List<AppCategory> categories)
        {
            if (categories == null) throw new ArgumentNullException(nameof(categories));

            EnsureOthersExists(categories);
            var options = new JsonSerializerOptions { WriteIndented = true };
            JsonFile.Save(AppPaths.GetAppCategoriesPath(), categories, options);
        }

        public AppCategory GetCategoryForProcess(string processName, List<AppCategory> categories)
        {
            if (!string.IsNullOrWhiteSpace(processName))
            {
                foreach (var category in categories)
                {
                    if (category.ProcessNames.Any(p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return category;
                    }
                }
            }

            return categories.FirstOrDefault(c => c.IsProtected)
                ?? categories.FirstOrDefault(c => string.Equals(c.Name, OthersCategoryName, StringComparison.OrdinalIgnoreCase))
                ?? CreateOthersCategory();
        }

        // Distinct process names seen in the last `lookbackDays` of activity logs that
        // aren't mapped to any category yet. Bounded window so scanning stays cheap even
        // with years of history on disk.
        public List<string> DetectUnmappedProcessNames(
            IActivityLogReader activityLogReader,
            List<AppCategory> categories,
            DateTime today,
            int lookbackDays = 30)
        {
            var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in categories)
            {
                foreach (var processName in category.ProcessNames)
                {
                    mapped.Add(processName);
                }
            }

            var start = today.Date.AddDays(-(lookbackDays - 1));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in activityLogReader.ReadRange(start, today.Date))
            {
                if (entry.IsLocked || entry.IsIdle) continue;
                if (string.IsNullOrWhiteSpace(entry.ProcessName)) continue;
                if (mapped.Contains(entry.ProcessName)) continue;

                seen.Add(entry.ProcessName);
            }

            return seen.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<AppCategory> CreateDefaults()
        {
            var list = DefaultCategoryNames.Select(name => new AppCategory { Name = name }).ToList();
            list.Add(CreateOthersCategory());
            return list;
        }

        private static AppCategory CreateOthersCategory() =>
            new AppCategory { Name = OthersCategoryName, IsProtected = true };

        private static void EnsureOthersExists(List<AppCategory> categories)
        {
            var others = categories.FirstOrDefault(c => string.Equals(c.Name, OthersCategoryName, StringComparison.OrdinalIgnoreCase));
            if (others == null)
            {
                categories.Add(CreateOthersCategory());
            }
            else
            {
                others.IsProtected = true;
            }
        }
    }
}
