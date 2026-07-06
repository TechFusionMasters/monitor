using System;

namespace SystemActivityTracker.Models
{
    // A yearly public holiday. Holidays are always non-working days — there is no
    // working/non-working flag, matching the always-off nature of a company holiday.
    public class HolidayEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime Date { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
