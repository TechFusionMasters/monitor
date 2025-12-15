using System;
using Microsoft.Win32;

namespace SystemActivityTracker.Services
{
    public class SessionStateService : IDisposable
    {
        private bool _isDisposed;

        public bool IsLocked { get; private set; }

        public event EventHandler<bool>? LockStateChanged;

        public SessionStateService()
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    UpdateLockState(true);
                    break;
                case SessionSwitchReason.SessionUnlock:
                    UpdateLockState(false);
                    break;
            }
        }

        private void UpdateLockState(bool isLocked)
        {
            if (IsLocked == isLocked)
            {
                return;
            }

            IsLocked = isLocked;
            LockStateChanged?.Invoke(this, IsLocked);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

        ~SessionStateService()
        {
            Dispose();
        }
    }
}
