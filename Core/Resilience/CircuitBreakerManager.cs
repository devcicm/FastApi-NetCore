using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Resilience
{
    /// <summary>
    /// High-performance circuit breaker implementation for API resilience
    /// </summary>
    public class CircuitBreakerManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, CircuitBreaker> _circuitBreakers;
        private readonly Timer _resetTimer;
        private readonly CircuitBreakerConfiguration _config;

        public CircuitBreakerManager(CircuitBreakerConfiguration? config = null)
        {
            _config = config ?? new CircuitBreakerConfiguration();
            _circuitBreakers = new ConcurrentDictionary<string, CircuitBreaker>();
            
            // Check for breakers that can be reset every 30 seconds
            _resetTimer = new Timer(CheckBreakerResets, null, 
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        public CircuitBreaker GetOrCreateBreaker(string name, CircuitBreakerPolicy? policy = null)
        {
            return _circuitBreakers.GetOrAdd(name, key => 
                new CircuitBreaker(key, policy ?? _config.DefaultPolicy));
        }

        public async Task<T> ExecuteAsync<T>(string breakerName, Func<Task<T>> operation, 
            CircuitBreakerPolicy? policy = null)
        {
            var breaker = GetOrCreateBreaker(breakerName, policy);
            return await breaker.ExecuteAsync(operation);
        }

        public async Task ExecuteAsync(string breakerName, Func<Task> operation, 
            CircuitBreakerPolicy? policy = null)
        {
            var breaker = GetOrCreateBreaker(breakerName, policy);
            await breaker.ExecuteAsync(operation);
        }

        private void CheckBreakerResets(object? state)
        {
            foreach (var breaker in _circuitBreakers.Values)
            {
                breaker.TryReset();
            }
        }

        public CircuitBreakerStatistics GetStatistics()
        {
            var stats = new CircuitBreakerStatistics();
            
            foreach (var kvp in _circuitBreakers)
            {
                var breakerStats = kvp.Value.GetStatistics();
                stats.Breakers[kvp.Key] = breakerStats;
                
                stats.TotalRequests += breakerStats.TotalRequests;
                stats.SuccessfulRequests += breakerStats.SuccessfulRequests;
                stats.FailedRequests += breakerStats.FailedRequests;
                stats.CircuitOpenRequests += breakerStats.CircuitOpenRequests;
                
                if (breakerStats.State == CircuitBreakerState.Open)
                    stats.OpenBreakers++;
                else if (breakerStats.State == CircuitBreakerState.HalfOpen)
                    stats.HalfOpenBreakers++;
                else
                    stats.ClosedBreakers++;
            }
            
            return stats;
        }

        public void Dispose()
        {
            _resetTimer?.Dispose();
            foreach (var breaker in _circuitBreakers.Values)
            {
                breaker.Dispose();
            }
            _circuitBreakers.Clear();
        }
    }

    public class CircuitBreaker : IDisposable
    {
        private readonly string _name;
        private readonly CircuitBreakerPolicy _policy;
        private readonly object _lock = new object();
        
        private CircuitBreakerState _state = CircuitBreakerState.Closed;
        private int _failureCount = 0;
        private DateTime _lastFailureTime = DateTime.MinValue;
        private DateTime _nextAttemptTime = DateTime.MinValue;
        
        // Statistics
        private long _totalRequests = 0;
        private long _successfulRequests = 0;
        private long _failedRequests = 0;
        private long _circuitOpenRequests = 0;

        public CircuitBreaker(string name, CircuitBreakerPolicy policy)
        {
            _name = name;
            _policy = policy;
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
        {
            if (!CanExecute())
            {
                Interlocked.Increment(ref _circuitOpenRequests);
                throw new CircuitBreakerOpenException(_name, _nextAttemptTime);
            }

            Interlocked.Increment(ref _totalRequests);

            try
            {
                var result = await operation();
                OnSuccess();
                return result;
            }
            catch (Exception ex)
            {
                OnFailure(ex);
                throw;
            }
        }

        public async Task ExecuteAsync(Func<Task> operation)
        {
            await ExecuteAsync(async () =>
            {
                await operation();
                return true; // Dummy return value
            });
        }

        private bool CanExecute()
        {
            lock (_lock)
            {
                switch (_state)
                {
                    case CircuitBreakerState.Closed:
                        return true;
                        
                    case CircuitBreakerState.Open:
                        if (DateTime.UtcNow >= _nextAttemptTime)
                        {
                            _state = CircuitBreakerState.HalfOpen;
                            return true;
                        }
                        return false;
                        
                    case CircuitBreakerState.HalfOpen:
                        return true;
                        
                    default:
                        return false;
                }
            }
        }

        private void OnSuccess()
        {
            Interlocked.Increment(ref _successfulRequests);
            
            lock (_lock)
            {
                if (_state == CircuitBreakerState.HalfOpen)
                {
                    // Reset to closed state
                    _state = CircuitBreakerState.Closed;
                    _failureCount = 0;
                }
                else if (_state == CircuitBreakerState.Closed)
                {
                    // Gradual recovery in closed state
                    if (_failureCount > 0)
                    {
                        _failureCount = Math.Max(0, _failureCount - 1);
                    }
                }
            }
        }

        private void OnFailure(Exception exception)
        {
            Interlocked.Increment(ref _failedRequests);
            
            lock (_lock)
            {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;

                if (_state == CircuitBreakerState.HalfOpen)
                {
                    // Immediately open on failure in half-open state
                    OpenCircuit();
                }
                else if (_state == CircuitBreakerState.Closed && 
                         _failureCount >= _policy.FailureThreshold)
                {
                    // Open circuit if threshold exceeded
                    OpenCircuit();
                }
            }
        }

        private void OpenCircuit()
        {
            _state = CircuitBreakerState.Open;
            _nextAttemptTime = DateTime.UtcNow.Add(_policy.OpenTimeout);
        }

        public void TryReset()
        {
            lock (_lock)
            {
                if (_state == CircuitBreakerState.Open && DateTime.UtcNow >= _nextAttemptTime)
                {
                    _state = CircuitBreakerState.HalfOpen;
                }
            }
        }

        public CircuitBreakerInfo GetStatistics()
        {
            lock (_lock)
            {
                return new CircuitBreakerInfo
                {
                    Name = _name,
                    State = _state,
                    FailureCount = _failureCount,
                    LastFailureTime = _lastFailureTime,
                    NextAttemptTime = _nextAttemptTime,
                    TotalRequests = Interlocked.Read(ref _totalRequests),
                    SuccessfulRequests = Interlocked.Read(ref _successfulRequests),
                    FailedRequests = Interlocked.Read(ref _failedRequests),
                    CircuitOpenRequests = Interlocked.Read(ref _circuitOpenRequests),
                    FailureThreshold = _policy.FailureThreshold,
                    OpenTimeout = _policy.OpenTimeout
                };
            }
        }

        public void Dispose()
        {
            // Nothing specific to dispose for now
        }
    }

    public enum CircuitBreakerState
    {
        Closed,   // Normal operation
        Open,     // Circuit is open, requests are rejected
        HalfOpen  // Testing if service has recovered
    }

    public class CircuitBreakerPolicy
    {
        public int FailureThreshold { get; set; } = 5;
        public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public Func<Exception, bool>? ExceptionPredicate { get; set; }
    }

    public class CircuitBreakerConfiguration
    {
        public CircuitBreakerPolicy DefaultPolicy { get; set; } = new CircuitBreakerPolicy
        {
            FailureThreshold = 5,
            OpenTimeout = TimeSpan.FromSeconds(30)
        };
    }

    public class CircuitBreakerOpenException : Exception
    {
        public string CircuitBreakerName { get; }
        public DateTime NextAttemptTime { get; }

        public CircuitBreakerOpenException(string name, DateTime nextAttemptTime)
            : base($"Circuit breaker '{name}' is open. Next attempt at {nextAttemptTime:yyyy-MM-dd HH:mm:ss} UTC")
        {
            CircuitBreakerName = name;
            NextAttemptTime = nextAttemptTime;
        }
    }

    public class CircuitBreakerInfo
    {
        public string Name { get; set; } = "";
        public CircuitBreakerState State { get; set; }
        public int FailureCount { get; set; }
        public DateTime LastFailureTime { get; set; }
        public DateTime NextAttemptTime { get; set; }
        public long TotalRequests { get; set; }
        public long SuccessfulRequests { get; set; }
        public long FailedRequests { get; set; }
        public long CircuitOpenRequests { get; set; }
        public int FailureThreshold { get; set; }
        public TimeSpan OpenTimeout { get; set; }
        
        public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0;
        public double FailureRate => TotalRequests > 0 ? (double)FailedRequests / TotalRequests : 0;
    }

    public class CircuitBreakerStatistics
    {
        public Dictionary<string, CircuitBreakerInfo> Breakers { get; set; } = new Dictionary<string, CircuitBreakerInfo>();
        public long TotalRequests { get; set; }
        public long SuccessfulRequests { get; set; }
        public long FailedRequests { get; set; }
        public long CircuitOpenRequests { get; set; }
        public int OpenBreakers { get; set; }
        public int HalfOpenBreakers { get; set; }
        public int ClosedBreakers { get; set; }
        
        public double OverallSuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0;
        public double OverallFailureRate => TotalRequests > 0 ? (double)FailedRequests / TotalRequests : 0;
    }
}