using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using SystemActivityTracker.Models;
using SystemActivityTracker.Utilities;

namespace SystemActivityTracker.Services
{
    // Persists yearly public holidays. Load/GetForDate are reused by Daily, Weekly,
    // Monthly, and Yearly reporting surfaces — none of them wire holidays into
    // expected-hours calculations yet.
    public class HolidayService
    {
        public List<HolidayEntry> LoadYear(int year)
        {
            try
            {
                string path = AppPaths.GetHolidaysPath(year);
                var entries = JsonFile.LoadOrDefault(path, static () => new List<HolidayEntry>());
                return entries.OrderBy(h => h.Date).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Holidays] Load failed: {ex}");
                return new List<HolidayEntry>();
            }
        }

        public HolidayEntry? GetForDate(DateTime date) =>
            LoadYear(date.Year).FirstOrDefault(h => h.Date.Date == date.Date);

        public bool IsHoliday(DateTime date) => GetForDate(date) != null;

        public void SaveYear(int year, List<HolidayEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            var sorted = entries.OrderBy(h => h.Date).ToList();
            string path = AppPaths.GetHolidaysPath(year);
            var options = new JsonSerializerOptions { WriteIndented = true };
            JsonFile.Save(path, sorted, options);
        }
    }
}
