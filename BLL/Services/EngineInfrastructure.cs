using Hangfire;
using Hangfire.Storage;
using System;
using System.Collections.Generic;
using System.Threading;

namespace BLL.Algorithms
{
    // נעילה מבוזרת לריצות המנוע: הריצה השעתית, ריצה ידנית ושיבוץ מחדש לא רצים במקביל
    // (גם כשיש כמה שרתים). מאחורי הממשק - נעילה של Hangfire במסד.
    public interface IEngineLock
    {
        // null = לא הצלחנו לקבל את הנעילה תוך זמן ההמתנה (ריצה אחרת פעילה)
        IDisposable? TryAcquire(string resource, TimeSpan wait);
    }

    public sealed class HangfireEngineLock : IEngineLock
    {
        private readonly JobStorage _storage;

        public HangfireEngineLock(JobStorage storage)
        {
            _storage = storage;
        }

        public IDisposable? TryAcquire(string resource, TimeSpan wait)
        {
            var connection = _storage.GetConnection();
            try
            {
                var handle = connection.AcquireDistributedLock("food-rescue:" + resource, wait);
                return new Releaser(handle, connection);
            }
            catch (DistributedLockTimeoutException)
            {
                connection.Dispose();
                return null;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private sealed class Releaser : IDisposable
        {
            private readonly IDisposable _handle;
            private readonly IStorageConnection _connection;

            public Releaser(IDisposable handle, IStorageConnection connection)
            {
                _handle = handle;
                _connection = connection;
            }

            public void Dispose()
            {
                _handle.Dispose();
                _connection.Dispose();
            }
        }
    }

    // נעילה בתוך התהליך בלבד (בדיקות / סביבה בלי Hangfire)
    public sealed class InProcessEngineLock : IEngineLock
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public IDisposable? TryAcquire(string resource, TimeSpan wait) =>
            _semaphore.Wait(wait) ? new Releaser(_semaphore) : null;

        private sealed class Releaser : IDisposable
        {
            private SemaphoreSlim? _semaphore;
            public Releaser(SemaphoreSlim semaphore) { _semaphore = semaphore; }
            public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
        }
    }

    // תוצאת ריצה של המנוע
    public sealed class EngineRunResult
    {
        public bool Started { get; init; } = true;
        public string Trigger { get; init; } = "Scheduled";
        public DateTime StartedAt { get; init; }
        public DateTime? FinishedAt { get; set; }
        public int DonationsProcessed { get; set; }
        public int MatchedCount { get; set; }
        public int RoutesCreated { get; set; }
        public List<string> FailedConstraints { get; } = new();
        public string? Error { get; set; }

        public double DurationSeconds => ((FinishedAt ?? DateTime.UtcNow) - StartedAt).TotalSeconds;

        public static EngineRunResult NotStarted(string trigger) =>
            new() { Started = false, Trigger = trigger, StartedAt = DateTime.UtcNow, FinishedAt = DateTime.UtcNow };
    }

    // מצב המנוע בתהליך הנוכחי (Singleton) - לתצוגה ב-engine-status
    public sealed class EngineRunState
    {
        private readonly object _lock = new();
        private EngineRunResult? _last;
        private EngineRunResult? _current;

        public void Started(EngineRunResult run)
        {
            lock (_lock) _current = run;
        }

        public void Finished(EngineRunResult run)
        {
            lock (_lock)
            {
                _last = run;
                if (ReferenceEquals(_current, run)) _current = null;
            }
        }

        public (bool IsRunning, EngineRunResult? Last) Snapshot()
        {
            lock (_lock) return (_current != null, _last);
        }
    }
}
