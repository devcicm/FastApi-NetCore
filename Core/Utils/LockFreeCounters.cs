using System;
using System.Collections.Concurrent;
using System.Threading;

namespace FastApi_NetCore.Core.Utils
{
    /// <summary>
    /// Lock-free counter utilities for high-performance concurrent operations
    /// </summary>
    public static class LockFreeCounters
    {
        /// <summary>
        /// Simple atomic counter using Interlocked operations
        /// </summary>
        public class AtomicCounter
        {
            private long _value = 0;

            public long Value => Interlocked.Read(ref _value);

            public long Increment() => Interlocked.Increment(ref _value);
            public long Decrement() => Interlocked.Decrement(ref _value);
            public long Add(long value) => Interlocked.Add(ref _value, value);
            public long Exchange(long value) => Interlocked.Exchange(ref _value, value);
            
            public bool CompareAndSwap(long expected, long newValue) =>
                Interlocked.CompareExchange(ref _value, newValue, expected) == expected;

            public void Reset() => Interlocked.Exchange(ref _value, 0);
        }

        /// <summary>
        /// Atomic accumulator for averaging values
        /// </summary>
        public class AtomicAccumulator
        {
            private long _count = 0;
            private long _sum = 0;

            public long Count => Interlocked.Read(ref _count);
            public long Sum => Interlocked.Read(ref _sum);
            public double Average => Count > 0 ? (double)Sum / Count : 0;

            public void Add(long value)
            {
                Interlocked.Increment(ref _count);
                Interlocked.Add(ref _sum, value);
            }

            public (long count, long sum, double average) GetAndReset()
            {
                var count = Interlocked.Exchange(ref _count, 0);
                var sum = Interlocked.Exchange(ref _sum, 0);
                var average = count > 0 ? (double)sum / count : 0;
                return (count, sum, average);
            }
        }

        /// <summary>
        /// Atomic min/max tracker
        /// </summary>
        public class AtomicMinMax
        {
            private long _min = long.MaxValue;
            private long _max = long.MinValue;

            public long Min => _min == long.MaxValue ? 0 : _min;
            public long Max => _max == long.MinValue ? 0 : _max;

            public void Update(long value)
            {
                // Update min atomically
                long currentMin;
                do
                {
                    currentMin = _min;
                    if (value >= currentMin && currentMin != long.MaxValue) break;
                } while (Interlocked.CompareExchange(ref _min, value, currentMin) != currentMin);

                // Update max atomically
                long currentMax;
                do
                {
                    currentMax = _max;
                    if (value <= currentMax && currentMax != long.MinValue) break;
                } while (Interlocked.CompareExchange(ref _max, value, currentMax) != currentMax);
            }

            public (long min, long max) GetAndReset()
            {
                var min = Interlocked.Exchange(ref _min, long.MaxValue);
                var max = Interlocked.Exchange(ref _max, long.MinValue);
                return (min == long.MaxValue ? 0 : min, max == long.MinValue ? 0 : max);
            }
        }

        /// <summary>
        /// Lock-free named counter collection
        /// </summary>
        public class AtomicCounterCollection
        {
            private readonly ConcurrentDictionary<string, AtomicCounter> _counters = new();

            public long Increment(string name)
            {
                var counter = _counters.GetOrAdd(name, _ => new AtomicCounter());
                return counter.Increment();
            }

            public long Decrement(string name)
            {
                var counter = _counters.GetOrAdd(name, _ => new AtomicCounter());
                return counter.Decrement();
            }

            public long Add(string name, long value)
            {
                var counter = _counters.GetOrAdd(name, _ => new AtomicCounter());
                return counter.Add(value);
            }

            public long GetValue(string name)
            {
                return _counters.TryGetValue(name, out var counter) ? counter.Value : 0;
            }

            public void Reset(string name)
            {
                if (_counters.TryGetValue(name, out var counter))
                {
                    counter.Reset();
                }
            }

            public void ResetAll()
            {
                foreach (var counter in _counters.Values)
                {
                    counter.Reset();
                }
            }

            public ConcurrentDictionary<string, long> GetSnapshot()
            {
                var snapshot = new ConcurrentDictionary<string, long>();
                foreach (var kvp in _counters)
                {
                    snapshot[kvp.Key] = kvp.Value.Value;
                }
                return snapshot;
            }

            public ConcurrentDictionary<string, long> GetSnapshotAndReset()
            {
                var snapshot = new ConcurrentDictionary<string, long>();
                foreach (var kvp in _counters)
                {
                    snapshot[kvp.Key] = kvp.Value.Exchange(0);
                }
                return snapshot;
            }
        }

        /// <summary>
        /// Rate counter for tracking events per time period
        /// </summary>
        public class AtomicRateCounter
        {
            private readonly AtomicCounter _totalCount = new();
            private readonly ConcurrentQueue<long> _timestamps = new();
            private readonly int _windowSeconds;

            public AtomicRateCounter(int windowSeconds = 60)
            {
                _windowSeconds = windowSeconds;
            }

            public long TotalCount => _totalCount.Value;
            
            public void Increment()
            {
                _totalCount.Increment();
                _timestamps.Enqueue(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                
                // Clean old timestamps (optional, for memory efficiency)
                CleanOldTimestamps();
            }

            public double GetRate()
            {
                CleanOldTimestamps();
                return (double)_timestamps.Count / _windowSeconds;
            }

            private void CleanOldTimestamps()
            {
                var cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _windowSeconds;
                
                while (_timestamps.TryPeek(out var timestamp) && timestamp < cutoff)
                {
                    _timestamps.TryDequeue(out _);
                }
            }
        }
    }
}