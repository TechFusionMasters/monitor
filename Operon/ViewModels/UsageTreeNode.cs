using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SystemActivityTracker.ViewModels
{
    public enum UsageNodeKind
    {
        Category,
        Application,
        Week,
        Day,
        Session
    }

    // One row in the Application Usage breakdown tree (Category → Application →
    // [Week →] Day → Session). Category/Application rows carry a Percent (drives the
    // progress bar); deeper rows don't. Children below the Application level are built
    // lazily on first expand via childrenFactory, so loading a month of sessions doesn't
    // pay the cost of grouping every individual session until the user actually opens
    // that branch.
    public sealed class UsageTreeNode : INotifyPropertyChanged
    {
        private readonly Func<List<UsageTreeNode>>? _childrenFactory;
        private bool _isExpanded;
        private bool _childrenLoaded;

        public UsageTreeNode(
            UsageNodeKind kind,
            string title,
            TimeSpan duration,
            double? percent = null,
            DateTime? sessionStart = null,
            DateTime? sessionEnd = null,
            Func<List<UsageTreeNode>>? childrenFactory = null)
        {
            Kind = kind;
            Title = title;
            Duration = duration;
            Percent = percent;
            SessionStart = sessionStart;
            SessionEnd = sessionEnd;
            _childrenFactory = childrenFactory;
        }

        public UsageNodeKind Kind { get; }
        public string Title { get; }
        public TimeSpan Duration { get; }
        public string DurationText => FormatHm(Duration);

        public double? Percent { get; }
        public double PercentValue => Percent ?? 0;
        public string PercentText => Percent.HasValue ? $"{Percent.Value:0}%" : string.Empty;
        public bool ShowPercentBar => Percent.HasValue;

        public bool IsSession => Kind == UsageNodeKind.Session;
        public DateTime? SessionStart { get; }
        public DateTime? SessionEnd { get; }
        public string SessionStartText => SessionStart?.ToString("HH:mm:ss") ?? string.Empty;
        public string SessionEndText => SessionEnd?.ToString("HH:mm:ss") ?? string.Empty;

        public bool HasChildren => _childrenFactory != null;

        public ObservableCollection<UsageTreeNode> Children { get; } = new ObservableCollection<UsageTreeNode>();

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
                if (value) LoadChildrenIfNeeded();
            }
        }

        private void LoadChildrenIfNeeded()
        {
            if (_childrenLoaded || _childrenFactory == null) return;
            _childrenLoaded = true;
            foreach (var child in _childrenFactory())
            {
                Children.Add(child);
            }
        }

        private static string FormatHm(TimeSpan span)
        {
            int h = (int)span.TotalHours;
            int m = span.Minutes;
            return $"{h}h {m}m";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
