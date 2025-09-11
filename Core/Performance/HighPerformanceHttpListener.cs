using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace FastApi_NetCore.Core.Performance
{
    /// <summary>
    /// High-performance HTTP listener with connection pooling and keep-alive optimization
    /// </summary>
    public class HighPerformanceHttpListener : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly ConnectionPool _connectionPool;
        private readonly PerformanceConfiguration _config;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task[] _listenerTasks;
        private readonly Channel<HttpListenerContext> _requestChannel;
        private volatile bool _disposed = false;

        public HighPerformanceHttpListener(PerformanceConfiguration? config = null)
        {
            _config = config ?? new PerformanceConfiguration();
            _listener = new HttpListener();
            _connectionPool = new ConnectionPool(_config);
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Configure HttpListener for high performance
            ConfigureListener();
            
            // Create high-capacity channel for incoming requests
            var channelOptions = new BoundedChannelOptions(_config.RequestChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            };
            _requestChannel = Channel.CreateBounded<HttpListenerContext>(channelOptions);
            
            // Create multiple listener tasks for parallel processing
            _listenerTasks = new Task[_config.ListenerThreads];
            for (int i = 0; i < _config.ListenerThreads; i++)
            {
                _listenerTasks[i] = Task.Run(ListenForRequestsAsync, _cancellationTokenSource.Token);
            }
        }

        private void ConfigureListener()
        {
            // Optimize HttpListener settings for high throughput
            _listener.TimeoutManager.IdleConnection = _config.IdleConnectionTimeout;
            _listener.TimeoutManager.HeaderWait = _config.HeaderWaitTimeout;
            _listener.TimeoutManager.RequestQueue = _config.RequestQueueTimeout;
            _listener.IgnoreWriteExceptions = true;
        }

        public void AddPrefix(string prefix)
        {
            _listener.Prefixes.Add(prefix);
        }

        public void Start()
        {
            _listener.Start();
        }

        public void Stop()
        {
            _listener.Stop();
        }

        public async Task<HttpListenerContext> GetContextAsync(CancellationToken cancellationToken = default)
        {
            return await _requestChannel.Reader.ReadAsync(cancellationToken);
        }

        private async Task ListenForRequestsAsync()
        {
            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested && _listener.IsListening)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();
                        
                        // Optimize connection handling
                        OptimizeConnection(context);
                        
                        // Enqueue for processing
                        try
                        {
                            await _requestChannel.Writer.WriteAsync(context, _cancellationTokenSource.Token);
                        }
                        catch (InvalidOperationException)
                        {
                            // Channel closed, close the response
                            context.Response.StatusCode = 503; // Service Unavailable
                            context.Response.Close();
                        }
                    }
                    catch (HttpListenerException ex) when (ex.ErrorCode == 995) // ERROR_OPERATION_ABORTED
                    {
                        // Expected during shutdown
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        // Expected during shutdown
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Log unexpected errors but continue listening
                        Console.WriteLine($"[PERF-LISTENER] Unexpected error: {ex.Message}");
                    }
                }
            }
            finally
            {
                _requestChannel.Writer.TryComplete();
            }
        }

        private void OptimizeConnection(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // Set keep-alive headers for connection reuse
            if (_config.EnableKeepAlive)
            {
                response.Headers.Add("Connection", "keep-alive");
                response.Headers.Add("Keep-Alive", $"timeout={_config.KeepAliveTimeout.TotalSeconds}, max={_config.MaxKeepAliveRequests}");
            }

            // Add performance headers
            response.Headers.Add("X-Server-Timing", $"listener;dur=0");
            response.Headers.Add("X-Content-Type-Options", "nosniff");
            
            // Set optimal buffer sizes
            if (request.ContentLength64 > 0 && request.ContentLength64 < _config.OptimalBufferSize)
            {
                // BufferOutput is not available in .NET Core HttpListenerResponse
                // This optimization is handled at a different level
            }

            // Register connection in pool for tracking
            _connectionPool.RegisterConnection(context);
        }

        public ConnectionPoolStatistics GetConnectionPoolStats()
        {
            return _connectionPool.GetStatistics();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _cancellationTokenSource.Cancel();
                _listener?.Stop();
                
                // Wait for listener tasks to complete
                Task.WhenAll(_listenerTasks).Wait(TimeSpan.FromSeconds(5));
                
                _requestChannel.Writer.Complete();
                _connectionPool?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PERF-LISTENER] Disposal error: {ex.Message}");
            }
            finally
            {
                _listener?.Close();
                _cancellationTokenSource?.Dispose();
            }
        }
    }

    public class ConnectionPool : IDisposable
    {
        private readonly ConcurrentDictionary<string, ConnectionInfo> _connections;
        private readonly Timer _cleanupTimer;
        private readonly PerformanceConfiguration _config;
        private long _totalConnections = 0;
        private long _activeConnections = 0;
        private long _reusedConnections = 0;

        public ConnectionPool(PerformanceConfiguration config)
        {
            _config = config;
            _connections = new ConcurrentDictionary<string, ConnectionInfo>();
            
            // Cleanup expired connections every minute
            _cleanupTimer = new Timer(CleanupExpiredConnections, null, 
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public void RegisterConnection(HttpListenerContext context)
        {
            var connectionId = GetConnectionId(context);
            var now = DateTime.UtcNow;
            
            Interlocked.Increment(ref _totalConnections);

            var connectionInfo = _connections.AddOrUpdate(connectionId,
                new ConnectionInfo { FirstSeen = now, LastUsed = now, RequestCount = 1 },
                (key, existing) =>
                {
                    existing.LastUsed = now;
                    existing.RequestCount++;
                    Interlocked.Increment(ref _reusedConnections);
                    return existing;
                });

            Interlocked.Exchange(ref _activeConnections, _connections.Count);
        }

        private string GetConnectionId(HttpListenerContext context)
        {
            var request = context.Request;
            return $"{request.RemoteEndPoint?.Address}:{request.RemoteEndPoint?.Port}";
        }

        private void CleanupExpiredConnections(object? state)
        {
            var cutoff = DateTime.UtcNow.Subtract(_config.ConnectionTrackingDuration);
            var toRemove = new List<string>();

            foreach (var kvp in _connections)
            {
                if (kvp.Value.LastUsed < cutoff)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var connectionId in toRemove)
            {
                _connections.TryRemove(connectionId, out _);
            }

            Interlocked.Exchange(ref _activeConnections, _connections.Count);
        }

        public ConnectionPoolStatistics GetStatistics()
        {
            return new ConnectionPoolStatistics
            {
                TotalConnections = Interlocked.Read(ref _totalConnections),
                ActiveConnections = Interlocked.Read(ref _activeConnections),
                ReusedConnections = Interlocked.Read(ref _reusedConnections),
                ReuseRatio = _totalConnections > 0 ? (double)_reusedConnections / _totalConnections : 0,
                TrackedConnections = _connections.Count
            };
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _connections?.Clear();
        }
    }

    public class ConnectionInfo
    {
        public DateTime FirstSeen { get; set; }
        public DateTime LastUsed { get; set; }
        public int RequestCount { get; set; }
    }

    public class PerformanceConfiguration
    {
        public int ListenerThreads { get; set; } = Environment.ProcessorCount;
        public int RequestChannelCapacity { get; set; } = 10000;
        public bool EnableKeepAlive { get; set; } = true;
        public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(120);
        public int MaxKeepAliveRequests { get; set; } = 1000;
        public int OptimalBufferSize { get; set; } = 8192;
        public TimeSpan ConnectionTrackingDuration { get; set; } = TimeSpan.FromMinutes(30);
        
        // HttpListener timeout settings
        public TimeSpan IdleConnectionTimeout { get; set; } = TimeSpan.FromSeconds(120);
        public TimeSpan HeaderWaitTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan RequestQueueTimeout { get; set; } = TimeSpan.FromSeconds(120);
    }

    public class ConnectionPoolStatistics
    {
        public long TotalConnections { get; set; }
        public long ActiveConnections { get; set; }
        public long ReusedConnections { get; set; }
        public double ReuseRatio { get; set; }
        public int TrackedConnections { get; set; }
    }
}