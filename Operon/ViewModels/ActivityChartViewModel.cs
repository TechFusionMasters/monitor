using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;
using SystemActivityTracker.Utilities;
using System.Windows;

namespace SystemActivityTracker.ViewModels
{
    public class ActivityChartViewModel : INotifyPropertyChanged
    {
        #region Input Properties (Data Source)
        
        /// <summary>
        /// Total active time for the selected period
        /// </summary>
        public TimeSpan TotalActiveTime { get; set; }

        /// <summary>
        /// Manual tasks duration for the selected period
        /// </summary>
        public TimeSpan ManualTasksDuration { get; set; }

        /// <summary>
        /// Idle time for the selected period
        /// </summary>
        public TimeSpan IdleTime { get; set; }

        /// <summary>
        /// Locked time for the selected period
        /// </summary>
        public TimeSpan LockedTime { get; set; }

        /// <summary>
        /// Reference time for 8-hour line (default: 8 hours)
        /// </summary>
        public TimeSpan ReferenceTime { get; set; } = TimeSpan.FromHours(8);

        /// <summary>
        /// Fallback scale when expected/reference time is zero (8h daily, 40h weekly).
        /// </summary>
        public TimeSpan ScaleFallbackReference { get; set; } = TimeSpan.FromHours(8);

        /// <summary>
        /// Controls whether to show the reference line label (e.g., "8h")
        /// </summary>
        private bool _showReferenceLabel = true;
        public bool ShowReferenceLabel
        {
            get => _showReferenceLabel;
            set
            {
                if (_showReferenceLabel == value)
                {
                    return;
                }

                _showReferenceLabel = value;
                OnPropertyChanged();
            }
        }

        private bool _showReferenceLine = true;
        public bool ShowReferenceLine
        {
            get => _showReferenceLine;
            private set
            {
                if (_showReferenceLine == value)
                {
                    return;
                }

                _showReferenceLine = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Controls whether to show the chart legend
        /// </summary>
        public bool ShowLegend { get; set; } = true;
        
        /// <summary>
        /// Controls whether data labels are shown on bars
        /// </summary>
        public bool ShowDataLabels { get; set; } = true;

        /// <summary>
        /// Controls whether to show only Total Activity bar (for month view)
        /// </summary>
        private bool _showTotalActivityOnly = false;
        public bool ShowTotalActivityOnly 
        { 
            get => _showTotalActivityOnly;
            set
            {
                if (_showTotalActivityOnly != value)
                {
                    _showTotalActivityOnly = value;
                    OnPropertyChanged();
                    // Update separator visibility when mode changes
                    ShowXAxisSeparator = !value;
                    // Trigger chart update to rebuild series when visibility mode changes
                    UpdateChart();
                }
            }
        }

        /// <summary>
        /// Controls whether to show the X-axis separator line (hide in total-activity-only mode)
        /// </summary>
        private bool _showXAxisSeparator = false;
        public bool ShowXAxisSeparator
        {
            get => _showXAxisSeparator;
            set
            {
                if (_showXAxisSeparator != value)
                {
                    _showXAxisSeparator = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Reduced/capped bar width to prevent bars from growing with available width.
        /// Bars stay thin; extra width becomes gaps.
        /// </summary>
        private double _desiredBarWidth = 30.0; // Thin, capped bar width
        public double DesiredBarWidth
        {
            get => _desiredBarWidth;
            set
            {
                if (Math.Abs(_desiredBarWidth - value) > 0.001)
                {
                    _desiredBarWidth = Math.Max(4.0, value);
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Controls the maximum column width for bars (responsive sizing)
        /// </summary>
        public double MaxColumnWidth { get; set; } = 25;

        /// <summary>
        /// Controls the column padding between bars
        /// </summary>
        public double ColumnPadding { get; set; } = 3;

        /// <summary>
        /// Computed gap between adjacent bars (result of equal-spacing algorithm)
        /// </summary>
        private double _barGapBetweenBars = 3;
        public double BarGapBetweenBars
        {
            get => _barGapBetweenBars;
            set
            {
                if (Math.Abs(_barGapBetweenBars - value) > 0.001)
                {
                    _barGapBetweenBars = value;
                    ColumnPadding = _barGapBetweenBars;
                    OnPropertyChanged();
                    ApplyBarGap();
                }
            }
        }

        /// <summary>
        /// Updates bar sizing based on available chart dimensions using equal-spacing algorithm.
        /// Formula: gap = (W - N*BW) / (N + 1)
        /// where W = slot width, N = bar count per slot, BW = desired bar width (capped)
        /// </summary>
        /// <param name="availableWidth">Available width for the chart</param>
        /// <param name="availableHeight">Available height for the chart</param>
        public void UpdateBarSizing(double availableWidth, double availableHeight)
        {
            if (availableWidth > 0)
            {
                _lastChartWidth = Math.Max(PlotLeftInset + 1, availableWidth);
            }

            if (availableHeight > 0)
            {
                _lastChartHeight = Math.Max(PlotTopInset + PlotBottomInset + 1, availableHeight);
            }

            var width = _lastChartWidth;
            var height = _lastChartHeight;

            // Determine category count (number of slots along X axis)
            int categoryCount = Math.Max(1, XAxisLabels?.Length ?? 1);
            try
            {
                foreach (var s in ChartSeries)
                {
                    if (s is ColumnSeries cs && cs.Values != null && cs.Values.Count > 0)
                    {
                        categoryCount = cs.Values.Count;
                        break;
                    }
                }
            }
            catch
            {
                // Use label count as fallback
            }

            // Number of bars per slot (typically 3 for Active/Idle/Locked)
            int barCountPerSlot = Math.Max(1, ChartSeries?.Count ?? 1);

            // Compute slot width - total available width divided by number of categories
            double slotWidth = width > 0 ? width / (double)Math.Max(1, categoryCount) : 10.0;

            // Use the capped bar width (does NOT grow with available width)
            double BW = DesiredBarWidth;

            // Compute equal spacing: gap = (W - N*BW) / (N + 1)
            // This distributes remaining width equally across (N+1) spaces:
            // left outer + (N-1) between + right outer
            double remainingWidth = Math.Max(0, slotWidth - (barCountPerSlot * BW));
            double gap = remainingWidth / (double)(barCountPerSlot + 1);

            // Clamp gap to reasonable bounds (minimum 0.5, maximum 10)
            gap = Math.Max(0.5, Math.Min(10.0, gap));

            // Clamp bar width for very tight spaces
            double computedBW = BW;
            if (slotWidth < BW * 2)
            {
                // Fallback: divide space equally if too tight
                computedBW = Math.Max(4.0, slotWidth / (double)(barCountPerSlot + 1));
            }

            // Update sizing properties
            MaxColumnWidth = computedBW;
            BarGapBetweenBars = gap;

            // Apply sizing to any existing series so UI updates immediately
            ApplyBarGap();
            UpdateReferenceLineLayout(Math.Max(0, ReferenceTime.TotalSeconds), YAxisMax);
        }

        /// <summary>
        /// Theme-aware X-axis brush color
        /// </summary>
        public System.Windows.Media.Brush XAxisBrush
        {
            get
            {
                // Detect current theme (simplified detection)
                var isDarkTheme = IsDarkThemeEnabled();
                var color = isDarkTheme ? System.Windows.Media.Color.FromRgb(204, 204, 204) : System.Windows.Media.Color.FromRgb(102, 102, 102); // #CCC for dark, #666 for light
                return new SolidColorBrush(color);
            }
        }

        #endregion

        #region Output Properties (UI Bindings)

        private SeriesCollection _chartSeries = new SeriesCollection();
        public SeriesCollection ChartSeries
        {
            get => _chartSeries;
            private set
            {
                _chartSeries = value;
                OnPropertyChanged();
            }
        }

        private string[] _xAxisLabels = { "Total Active", "Locked", "Idle" };
        public string[] XAxisLabels
        {
            get => _xAxisLabels;
            set
            {
                _xAxisLabels = value;
                OnPropertyChanged();
            }
        }

        private Func<double, string> _yAxisFormatter = value => $"{value / 3600.0:F1}h";
        public Func<double, string> YAxisFormatter
        {
            get => _yAxisFormatter;
            private set
            {
                _yAxisFormatter = value;
                OnPropertyChanged();
            }
        }

        private double _yAxisMax = 10;
        public double YAxisMax
        {
            get => _yAxisMax;
            private set
            {
                _yAxisMax = value;
                OnPropertyChanged();
            }
        }

        private double _eightHourLinePosition;
        public double EightHourLinePosition
        {
            get => _eightHourLinePosition;
            private set
            {
                _eightHourLinePosition = value;
                OnPropertyChanged();
            }
        }

        private double _eightHourLabelPosition;
        public double EightHourLabelPosition
        {
            get => _eightHourLabelPosition;
            private set
            {
                _eightHourLabelPosition = value;
                OnPropertyChanged();
            }
        }

        private double _eightHourLabelTextPosition;
        public double EightHourLabelTextPosition
        {
            get => _eightHourLabelTextPosition;
            private set
            {
                _eightHourLabelTextPosition = value;
                OnPropertyChanged();
            }
        }

        private string _referenceLabelText = "8h";
        public string ReferenceLabelText
        {
            get => _referenceLabelText;
            private set
            {
                _referenceLabelText = value;
                OnPropertyChanged();
            }
        }

        public double ReferenceLinePlotWidth =>
            Math.Max(1, _lastChartWidth - PlotLeftInset - PlotRightInset);

        // Tooltip properties
        private string _tooltipTotalActive = "";
        public string TooltipTotalActive
        {
            get => _tooltipTotalActive;
            private set
            {
                _tooltipTotalActive = value;
                OnPropertyChanged();
            }
        }

        private string _tooltipActive = "";
        public string TooltipActive
        {
            get => _tooltipActive;
            private set
            {
                _tooltipActive = value;
                OnPropertyChanged();
            }
        }

        private string _tooltipManual = "";
        public string TooltipManual
        {
            get => _tooltipManual;
            private set
            {
                _tooltipManual = value;
                OnPropertyChanged();
            }
        }

        private string _tooltipIdle = "";
        public string TooltipIdle
        {
            get => _tooltipIdle;
            private set
            {
                _tooltipIdle = value;
                OnPropertyChanged();
            }
        }

        private string _tooltipLocked = "";
        public string TooltipLocked
        {
            get => _tooltipLocked;
            private set
            {
                _tooltipLocked = value;
                OnPropertyChanged();
            }
        }

        // Computed properties for UI binding
        public string TotalActiveText => TotalActiveTime.ToHoursMinutes();
        public string ManualTasksText => ManualTasksDuration.ToHoursMinutes();
        public string TotalActiveWithManualText => (TotalActiveTime + ManualTasksDuration).ToHoursMinutes();
        public string IdleText => IdleTime.ToHoursMinutes();
        public string LockedText => LockedTime.ToHoursMinutes();

        #endregion

        #region Events

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Updates the chart with new data
        /// </summary>
        public void UpdateChart()
        {
            InitializeChartIfNeeded();
            UpdateChartData();
            UpdateTooltipData();
        }

        /// <summary>
        /// Sets all data values at once and updates the chart
        /// </summary>
        public void SetData(TimeSpan totalActive, TimeSpan manualTasks, TimeSpan idle, TimeSpan locked)
        {
            TotalActiveTime = totalActive;
            ManualTasksDuration = manualTasks;
            IdleTime = idle;
            LockedTime = locked;
            UpdateChart();
        }

        #endregion

        #region Private Methods

        private double _lastChartWidth = 200;
        private double _lastChartHeight = 200;
        private const double PlotTopInset = 8;
        private const double PlotBottomInset = 62;
        private const double PlotLeftInset = 50;
        private const double PlotRightInset = 16;

        private void UpdateReferenceLineLayout(double referenceSeconds, double yAxisMax)
        {
            var showReference = referenceSeconds > 0;
            if (ShowReferenceLine != showReference)
            {
                ShowReferenceLine = showReference;
            }

            if (!showReference)
            {
                ShowReferenceLabel = false;
                return;
            }

            if (!ShowTotalActivityOnly)
            {
                ShowReferenceLabel = true;
            }

            var plotHeight = Math.Max(1, _lastChartHeight - PlotTopInset - PlotBottomInset);
            var referenceRatio = yAxisMax > 0 ? referenceSeconds / yAxisMax : 0;
            referenceRatio = Math.Max(0, Math.Min(1, referenceRatio));

            // Plot-local coordinates: bottom of canvas = Y-axis zero.
            EightHourLinePosition = plotHeight - (referenceRatio * plotHeight);
            EightHourLabelPosition = Math.Max(0, EightHourLinePosition - 10);
            EightHourLabelTextPosition = Math.Max(0, EightHourLinePosition - 8);
            ReferenceLabelText = $"{ReferenceTime.TotalHours:F0}h";

            OnPropertyChanged(nameof(ReferenceLinePlotWidth));
        }

        private void InitializeChartIfNeeded()
        {
            if (ChartSeries.Count == 0)
            {
                // Total Active series (index 0)
                var totalActiveSeries = new ColumnSeries
                {
                    Title = "Total Active",
                    Values = new ChartValues<double> { 0 },
                    DataLabels = ShowDataLabels,
                    StrokeThickness = 0,
                    ColumnPadding = ColumnPadding,  // Use responsive padding
                    MaxColumnWidth = MaxColumnWidth  // Use responsive max width
                };

                ChartSeries.Add(totalActiveSeries);

                // Only add Locked and Idle series if not in total-activity-only mode
                if (!ShowTotalActivityOnly)
                {
                    // Locked series (index 1)
                    var lockedSeries = new ColumnSeries
                    {
                        Title = "Locked",
                        Values = new ChartValues<double> { 0 },
                        DataLabels = ShowDataLabels,
                        StrokeThickness = 0,
                        Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)), // #6B7280 (Neutral Grey)
                        ColumnPadding = ColumnPadding,  // Use responsive padding
                        MaxColumnWidth = MaxColumnWidth   // Use responsive max width
                    };

                    // Idle series (index 2)
                    var idleSeries = new ColumnSeries
                    {
                        Title = "Idle",
                        Values = new ChartValues<double> { 0 },
                        DataLabels = ShowDataLabels,
                        StrokeThickness = 0,
                        Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175)), // #9CA3AF (Light Grey)
                        ColumnPadding = ColumnPadding,  // Use responsive padding
                        MaxColumnWidth = MaxColumnWidth   // Use responsive max width
                    };

                    ChartSeries.Add(lockedSeries);
                    ChartSeries.Add(idleSeries);

                    // Set X-axis labels for 3-series view
                    XAxisLabels = new[] { "Total Active", "Locked", "Idle" };
                    // Ensure bar gaps/widths are applied to newly-added series
                    ApplyBarGap();
                }
                else
                {
                    // Set X-axis labels for 1-series (total-activity-only) view
                    XAxisLabels = new[] { "Total Active" };
                    ApplyBarGap();
                }
            }
        }

        private void ApplyBarGap()
        {
            try
            {
                foreach (var s in ChartSeries)
                {
                    if (s is ColumnSeries cs)
                    {
                        cs.ColumnPadding = ColumnPadding;
                        cs.MaxColumnWidth = MaxColumnWidth;
                    }
                }
            }
            catch
            {
                // ignore if ChartSeries not yet initialized
            }
        }

        private void UpdateChartData()
        {
            InitializeChartIfNeeded();

            if (ChartSeries == null || ChartSeries.Count == 0) return;

            // Use seconds as the base unit for all calculations; never render negative heights.
            var totalActiveSeconds = Math.Max(0, (TotalActiveTime + ManualTasksDuration).TotalSeconds);
            var lockedSeconds = Math.Max(0, LockedTime.TotalSeconds);
            var idleSeconds = Math.Max(0, IdleTime.TotalSeconds);

            var referenceSeconds = Math.Max(0, ReferenceTime.TotalSeconds);
            var fallbackSeconds = ScaleFallbackReference.TotalSeconds > 0
                ? ScaleFallbackReference.TotalSeconds
                : TimeSpan.FromHours(8).TotalSeconds;

            var maxValue = Math.Max(totalActiveSeconds, Math.Max(lockedSeconds, idleSeconds));
            YAxisMax = Math.Max(maxValue, Math.Max(referenceSeconds, fallbackSeconds)) * 1.1;

            if (YAxisMax <= 0)
            {
                YAxisMax = fallbackSeconds * 1.1;
            }

            UpdateReferenceLineLayout(referenceSeconds, YAxisMax);

            // Update Total Active series (index 0)
            if (ChartSeries.Count > 0 && ChartSeries[0] is ColumnSeries totalActiveSeries && totalActiveSeries.Values != null && totalActiveSeries.Values.Count > 0)
            {
                totalActiveSeries.Values[0] = totalActiveSeconds;
                totalActiveSeries.Fill = GetTotalActiveColor(totalActiveSeconds / 60.0);
                totalActiveSeries.LabelPoint = point => FormatSummaryLabel(totalActiveSeconds);
            }

            // Update Locked series (index 1) - only if it exists (3-series mode)
            if (ChartSeries.Count > 1 && ChartSeries[1] is ColumnSeries lockedSeries && lockedSeries.Values != null && lockedSeries.Values.Count > 0)
            {
                lockedSeries.Values[0] = lockedSeconds;
                lockedSeries.Fill = lockedSeconds > 0
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128))
                    : System.Windows.Media.Brushes.Transparent;
                lockedSeries.LabelPoint = point => lockedSeconds > 0 ? FormatSummaryLabel(lockedSeconds) : string.Empty;
            }

            // Update Idle series (index 2) - only if it exists (3-series mode)
            if (ChartSeries.Count > 2 && ChartSeries[2] is ColumnSeries idleSeries && idleSeries.Values != null && idleSeries.Values.Count > 0)
            {
                idleSeries.Values[0] = idleSeconds;
                idleSeries.Fill = idleSeconds > 0
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175))
                    : System.Windows.Media.Brushes.Transparent;
                idleSeries.LabelPoint = point => idleSeconds > 0 ? FormatSummaryLabel(idleSeconds) : string.Empty;
            }

            // Notify property changes for computed properties
            OnPropertyChanged(nameof(TotalActiveText));
            OnPropertyChanged(nameof(ManualTasksText));
            OnPropertyChanged(nameof(TotalActiveWithManualText));
            OnPropertyChanged(nameof(IdleText));
            OnPropertyChanged(nameof(LockedText));
        }

        private void UpdateTooltipData()
        {
            var totalActiveWithManual = TotalActiveTime + ManualTasksDuration;
            
            TooltipTotalActive = $"Total Active: {totalActiveWithManual.ToHoursMinutes()}";
            TooltipActive = $"Active (tracked): {TotalActiveTime.ToHoursMinutes()}";
            TooltipManual = $"Offline: {ManualTasksDuration.ToHoursMinutes()}";
            TooltipIdle = $"Idle: {IdleTime.ToHoursMinutes()}";
            TooltipLocked = $"Locked: {LockedTime.ToHoursMinutes()}";
        }

        private System.Windows.Media.Brush GetTotalActiveColor(double totalActiveMinutes)
        {
            var referenceHours = ReferenceTime.TotalHours;
            var hours = Math.Max(0, totalActiveMinutes / 60.0);

            if (referenceHours <= 0)
            {
                if (hours <= 0)
                {
                    return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175)); // #9CA3AF
                }

                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // #EF4444
            }
            
            // Clamp to cap (1.25x reference time)
            var capHours = referenceHours * 1.25;
            if (hours >= capHours)
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)); // #10B981 (Green)
            
            // Define color stops as percentages of reference time
            var colorStops = new[]
            {
                new { Hours = referenceHours * 0.25, Color = (R: (byte)239, G: (byte)68, B: (byte)68) },     // 25% = Red
                new { Hours = referenceHours * 0.5, Color = (R: (byte)251, G: (byte)146, B: (byte)60) },    // 50% = Orange
                new { Hours = referenceHours * 0.75, Color = (R: (byte)250, G: (byte)204, B: (byte)21) },   // 75% = Yellow
                new { Hours = referenceHours, Color = (R: (byte)34, G: (byte)197, B: (byte)94) },          // 100% = Light Green
                new { Hours = capHours, Color = (R: (byte)16, G: (byte)185, B: (byte)129) }               // 125% = Green
            };
            
            // Find the appropriate segment and interpolate
            for (int i = 0; i < colorStops.Length - 1; i++)
            {
                var start = colorStops[i];
                var end = colorStops[i + 1];
                
                if (hours >= start.Hours && hours <= end.Hours)
                {
                    // At exact boundary, return exact color
                    if (Math.Abs(hours - start.Hours) < 0.001)
                        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(start.Color.R, start.Color.G, start.Color.B));
                    
                    if (Math.Abs(hours - end.Hours) < 0.001)
                        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(end.Color.R, end.Color.G, end.Color.B));
                    
                    var denominator = end.Hours - start.Hours;
                    if (Math.Abs(denominator) < 0.001)
                    {
                        continue;
                    }

                    // Interpolate between colors
                    var t = (hours - start.Hours) / denominator;
                    var interpolatedColor = InterpolateRgb(start.Color, end.Color, t);
                    return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(interpolatedColor.R, interpolatedColor.G, interpolatedColor.B));
                }
            }
            
            // Below minimum, return red
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // #EF4444
        }
        
        private static (byte R, byte G, byte B) InterpolateRgb((byte R, byte G, byte B) start, (byte R, byte G, byte B) end, double t)
        {
            // Clamp t to [0, 1] range
            t = Math.Max(0, Math.Min(1, t));
            
            var r = (byte)(start.R + (end.R - start.R) * t);
            var g = (byte)(start.G + (end.G - start.G) * t);
            var b = (byte)(start.B + (end.B - start.B) * t);
            
            return (r, g, b);
        }

        private string FormatSummaryLabel(double seconds)
        {
            var timeSpan = TimeSpan.FromSeconds(seconds);
            var hours = (int)timeSpan.TotalHours;
            var mins = timeSpan.Minutes;
            return $"{hours}.{mins:D2}h";
        }

        /// <summary>
        /// Detects if dark theme is enabled (simplified implementation)
        /// </summary>
        private bool IsDarkThemeEnabled()
        {
            try
            {
                // Try to detect theme from system settings
                var app = System.Windows.Application.Current;
                if (app?.Resources != null)
                {
                    // Check for common dark theme indicators
                    if (app.Resources.Contains("SystemControlBackgroundBaseHighBrush"))
                    {
                        var brush = app.Resources["SystemControlBackgroundBaseHighBrush"] as SolidColorBrush;
                        if (brush != null)
                        {
                            // If background is dark, it's dark theme
                            var brightness = (brush.Color.R * 0.299) + (brush.Color.G * 0.587) + (brush.Color.B * 0.114);
                            return brightness < 128; // Threshold for dark theme
                        }
                    }
                }
                
                // Fallback: Check system preference
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key?.GetValue("AppsUseLightTheme") is int lightThemeValue)
                    {
                        return lightThemeValue == 0; // 0 = dark theme, 1 = light theme
                    }
                }
            }
            catch
            {
                // If detection fails, default to light theme
            }
            
            return false; // Default to light theme
        }

        #endregion
    }
}
