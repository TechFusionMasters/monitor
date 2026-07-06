using System;
using System.Collections.Generic;

namespace SystemActivityTracker.Models
{
    // A user-defined (or default) bucket that application/process names are mapped to.
    // "Others" is the one system-protected category — it can't be deleted or renamed away,
    // and catches any process not explicitly mapped elsewhere.
    public class AppCategory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public bool IsProtected { get; set; }
        public List<string> ProcessNames { get; set; } = new List<string>();
    }
}
