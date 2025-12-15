using System;
using System.Collections.Generic;
using System.Timers;
using SystemActivityTracker.Models;

namespace SystemActivityTracker.Services
{
    public class TrackingService : IDisposable
    {
        private readonly SessionStateService _sessionStateService;
        private readonly ActivityLogWriter _logWriter = new ActivityLogWriter();
        private readonly System.Timers.Timer _timer;
        private TimeSpan _idleThreshold = TimeSpan.FromMinutes(2);
        private AppSettings _settings;
        private readonly object _syncRoot = new object();

        private ActivityRecord? _currentRecord;
        private readonly List<ActivityRecord> _completedRecords = new List<ActivityRecord>();
        private bool _isRunning;
        private bool _isDisposed;

        public event EventHandler<ActivityRecord>? ActivityRecordCreated;

        public bool IsRunning => _isRunning;

        public TrackingService(SessionStateService sessionStateService, SettingsService? settingsService = null)
        {
            _sessionStateService = sessionStateService ?? throw new ArgumentNullException(nameof(sessionStateService));

            // Load settings (or use defaults)
            _settings = settingsService?.Load() ?? new AppSettings();
            if (_settings.IdleThresholdMinutes <= 0)
            {
                _settings.IdleThresholdMinutes = 2;
            }
            if (_settings.PollIntervalSeconds <= 0)
            {
                _settings.PollIntervalSeconds = 5;
            }

            _idleThreshold = TimeSpan.FromMinutes(_settings.IdleThresholdMinutes);

            _timer = new System.Timers.Timer(_settings.PollIntervalSeconds * 1000);
            _timer.AutoReset = true;
            _timer.Elapsed += OnTimerElapsed;
        }

        public void ApplySettings(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            if (settings.IdleThresholdMinutes <= 0)
            {
                settings.IdleThresholdMinutes = 2;
            }
            if (settings.PollIntervalSeconds <= 0)
            {
                settings.PollIntervalSeconds = 5;
            }

            _settings = settings;
            _idleThreshold = TimeSpan.FromMinutes(_settings.IdleThresholdMinutes);

            _timer.Interval = _settings.PollIntervalSeconds * 1000;
        }

        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            _timer.Start();
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _timer.Stop();
            _isRunning = false;

            lock (_syncRoot)
            {
                if (_currentRecord != null && _currentRecord.EndTime == null)
                {
                    _currentRecord.EndTime = DateTime.Now;
                    _completedRecords.Add(_currentRecord);
                    _logWriter.AppendRecord(_currentRecord);
                    ActivityRecordCreated?.Invoke(this, _currentRecord);
                    _currentRecord = null;
                }
            }
        }

        public void FlushCurrentRecord()
        {
            lock (_syncRoot)
            {
                if (_currentRecord == null)
                {
                    return;
                }

                DateTime now = DateTime.Now;

                bool isLocked = _currentRecord.IsLocked;
                bool isIdle = _currentRecord.IsIdle;
                string processName = _currentRecord.ProcessName;
                string windowTitle = _currentRecord.WindowTitle;

                _currentRecord.EndTime = now;
                _completedRecords.Add(_currentRecord);
                _logWriter.AppendRecord(_currentRecord);
                ActivityRecordCreated?.Invoke(this, _currentRecord);

                _currentRecord = new ActivityRecord
                {
                    StartTime = now,
                    ProcessName = processName,
                    WindowTitle = windowTitle,
                    IsLocked = isLocked,
                    IsIdle = isIdle
                };
            }
        }

        public void Shutdown()
        {
            lock (_syncRoot)
            {
                if (_isRunning)
                {
                    _timer.Stop();
                    _isRunning = false;
                }

                if (_currentRecord != null && _currentRecord.EndTime == null)
                {
                    _currentRecord.EndTime = DateTime.Now;
                    _completedRecords.Add(_currentRecord);
                    _logWriter.AppendRecord(_currentRecord);
                    ActivityRecordCreated?.Invoke(this, _currentRecord);
                    _currentRecord = null;
                }
            }
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            lock (_syncRoot)
            {
                UpdateActivityState();
            }
        }

        private void UpdateActivityState()
        {
            DateTime now = DateTime.Now;

            bool isLocked = _sessionStateService.IsLocked;
            bool isIdle = false;
            string processName = string.Empty;
            string windowTitle = string.Empty;

            if (isLocked)
            {
                isIdle = false;
                processName = "LOCKED";
                windowTitle = string.Empty;
            }
            else
            {
                var idleTime = IdleTimeHelper.GetIdleTime();
                if (idleTime > _idleThreshold)
                {
                    isIdle = true;
                    processName = "IDLE";
                    windowTitle = string.Empty;
                }
                else
                {
                    isIdle = false;
                    if (!ActiveWindowHelper.TryGetActiveWindow(out processName, out windowTitle))
                    {
                        processName = string.Empty;
                        windowTitle = string.Empty;
                    }
                }
            }

            if (_currentRecord == null)
            {
                _currentRecord = new ActivityRecord
                {
                    StartTime = now,
                    ProcessName = processName,
                    WindowTitle = windowTitle,
                    IsLocked = isLocked,
                    IsIdle = isIdle
                };

                return;
            }

            bool stateChanged =
                _currentRecord.IsLocked != isLocked ||
                _currentRecord.IsIdle != isIdle ||
                !string.Equals(_currentRecord.ProcessName, processName, StringComparison.Ordinal) ||
                !string.Equals(_currentRecord.WindowTitle, windowTitle, StringComparison.Ordinal);

            if (!stateChanged)
            {
                return;
            }

            _currentRecord.EndTime = now;
            _completedRecords.Add(_currentRecord);
            _logWriter.AppendRecord(_currentRecord);
            ActivityRecordCreated?.Invoke(this, _currentRecord);

            _currentRecord = new ActivityRecord
            {
                StartTime = now,
                ProcessName = processName,
                WindowTitle = windowTitle,
                IsLocked = isLocked,
                IsIdle = isIdle
            };
        }

        public IReadOnlyList<ActivityRecord> GetCompletedRecords()
        {
            lock (_syncRoot)
            {
                return _completedRecords.AsReadOnly();
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            Shutdown();
            _timer.Stop();
            _timer.Elapsed -= OnTimerElapsed;
            _timer.Dispose();

            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

        ~TrackingService()
        {
            Dispose();
        }
    }
}
