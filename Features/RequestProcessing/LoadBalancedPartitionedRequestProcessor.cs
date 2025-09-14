using FastApi_NetCore.Core.Utils;
using FastApi_NetCore.Core.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.RequestProcessing
{
    /// <summary>
    /// Request processor con balanceador de cargas integrado para evitar contención de hilos
    /// </summary>
    public class LoadBalancedPartitionedRequestProcessor : IDisposable
    {
        private readonly ILoggerService _logger;
        private readonly RequestProcessorConfiguration _config;
        private readonly RequestLoadBalancer _loadBalancer;
        private readonly PartitionWorker[] _partitionWorkers;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private volatile bool _disposed = false;

        // Métricas de monitoreo
        private long _totalEnqueued = 0;
        private long _totalProcessed = 0;
        private long _totalDropped = 0;

        public LoadBalancedPartitionedRequestProcessor(
            ILoggerService logger,
            RequestProcessorConfiguration? config = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? new RequestProcessorConfiguration();
            _cancellationTokenSource = new CancellationTokenSource();

            // Crear load balancer inteligente
            _loadBalancer = new RequestLoadBalancer(_config, _logger);

            // Crear workers aislados por partición
            _partitionWorkers = new PartitionWorker[_config.BasePartitions];
            for (int i = 0; i < _config.BasePartitions; i++)
            {
                _partitionWorkers[i] = new PartitionWorker(i, _config, _logger, _cancellationTokenSource.Token);
            }

            _logger.LogInformation($"[LOAD-BALANCED-PROCESSOR] Initialized with {_config.BasePartitions} isolated workers");
        }

        public async Task<bool> EnqueueRequestAsync(HttpListenerContext context, Func<HttpListenerContext, Task> handler)
        {
            if (_disposed) return false;

            try
            {
                // Crear task de request
                var requestTask = new HttpRequestTask
                {
                    Context = context,
                    Handler = handler,
                    EnqueuedAt = DateTime.UtcNow,
                    RequestId = Guid.NewGuid().ToString("N")[..8]
                };

                // Load balancer decide la partición óptima (evita contención)
                var selectedPartition = _loadBalancer.SelectOptimalPartition(context, _partitionWorkers);
                
                if (selectedPartition == -1)
                {
                    Interlocked.Increment(ref _totalDropped);
                    _logger.LogWarning($"[LOAD-BALANCED-PROCESSOR] All partitions are full, dropping request {requestTask.RequestId}");
                    return false;
                }

                // Encolar en la partición seleccionada (sin contención)
                var enqueued = await _partitionWorkers[selectedPartition].TryEnqueueAsync(requestTask);
                
                if (enqueued)
                {
                    Interlocked.Increment(ref _totalEnqueued);
                    _logger.LogDebug($"[LOAD-BALANCED-PROCESSOR] Request {requestTask.RequestId} enqueued to partition {selectedPartition}");
                    return true;
                }
                else
                {
                    Interlocked.Increment(ref _totalDropped);
                    _logger.LogWarning($"[LOAD-BALANCED-PROCESSOR] Failed to enqueue request {requestTask.RequestId} to partition {selectedPartition}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalDropped);
                _logger.LogError($"[LOAD-BALANCED-PROCESSOR] Error enqueueing request: {ex}");
                return false;
            }
        }

        public void GetMetrics(out long enqueued, out long processed, out long dropped)
        {
            enqueued = Interlocked.Read(ref _totalEnqueued);
            processed = Interlocked.Read(ref _totalProcessed);
            dropped = Interlocked.Read(ref _totalDropped);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _logger.LogInformation("[LOAD-BALANCED-PROCESSOR] Shutting down...");

            try
            {
                _cancellationTokenSource.Cancel();
                
                // Dispose workers en paralelo
                var disposeTasks = new Task[_partitionWorkers.Length];
                for (int i = 0; i < _partitionWorkers.Length; i++)
                {
                    var worker = _partitionWorkers[i];
                    disposeTasks[i] = Task.Run(() => worker.Dispose());
                }
                
                Task.WaitAll(disposeTasks, TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogError($"[LOAD-BALANCED-PROCESSOR] Error during shutdown: {ex}");
            }
            finally
            {
                _cancellationTokenSource.Dispose();
                _logger.LogInformation("[LOAD-BALANCED-PROCESSOR] Shutdown completed");
            }
        }
    }

    /// <summary>
    /// Load balancer inteligente que evita contención entre workers
    /// </summary>
    internal class RequestLoadBalancer
    {
        private readonly RequestProcessorConfiguration _config;
        private readonly ILoggerService _logger;
        private int _roundRobinCounter = 0;

        public RequestLoadBalancer(RequestProcessorConfiguration config, ILoggerService logger)
        {
            _config = config;
            _logger = logger;
        }

        public int SelectOptimalPartition(HttpListenerContext context, PartitionWorker[] workers)
        {
            // Estrategia 1: Encontrar worker menos ocupado entre los saludables
            int leastBusyIndex = -1;
            int leastQueueDepth = int.MaxValue;
            double bestErrorRate = double.MaxValue;

            for (int i = 0; i < workers.Length; i++)
            {
                if (workers[i].IsHealthy)
                {
                    var queueDepth = workers[i].QueueDepth;
                    var errorRate = workers[i].ProcessedCount > 0 ?
                        (workers[i].ErrorCount * 100.0 / workers[i].ProcessedCount) : 0;

                    // Prioritize workers with low queue depth AND low error rate
                    if (queueDepth < leastQueueDepth ||
                        (queueDepth == leastQueueDepth && errorRate < bestErrorRate))
                    {
                        leastQueueDepth = queueDepth;
                        bestErrorRate = errorRate;
                        leastBusyIndex = i;
                    }
                }
            }

            // Si encontramos un worker saludable con capacidad disponible
            if (leastBusyIndex != -1 && leastQueueDepth < _config.MaxQueueDepthPerPartition * 0.8) // 80% threshold
            {
                return leastBusyIndex;
            }

            // Estrategia 2: Round robin entre workers saludables con capacidad
            int attempts = 0;
            while (attempts < workers.Length)
            {
                var index = Interlocked.Increment(ref _roundRobinCounter) % workers.Length;
                if (workers[index].IsHealthy && workers[index].HasCapacity)
                {
                    // Additional check for error rate
                    var errorRate = workers[index].ProcessedCount > 0 ?
                        (workers[index].ErrorCount * 100.0 / workers[index].ProcessedCount) : 0;

                    if (errorRate < 15) // Only use workers with <15% error rate
                    {
                        return index;
                    }
                }
                attempts++;
            }

            // Estrategia 3: Emergency fallback - any healthy worker
            for (int i = 0; i < workers.Length; i++)
            {
                if (workers[i].IsHealthy)
                {
                    _logger.LogWarning($"[LOAD-BALANCER] Emergency fallback to worker {i} (capacity: {workers[i].QueueDepth}/{_config.MaxQueueDepthPerPartition})");
                    return i;
                }
            }

            // No hay workers disponibles
            var healthyCount = CountHealthyWorkers(workers);
            _logger.LogError($"[LOAD-BALANCER] CRITICAL: No healthy workers available! Healthy: {healthyCount}/{workers.Length}");

            // Log worker states for debugging
            for (int i = 0; i < workers.Length; i++)
            {
                var errorRate = workers[i].ProcessedCount > 0 ?
                    (workers[i].ErrorCount * 100.0 / workers[i].ProcessedCount) : 0;
                _logger.LogError($"[LOAD-BALANCER] Worker {i}: Health={workers[i].IsHealthy}, Queue={workers[i].QueueDepth}, ErrorRate={errorRate:F1}%");
            }

            return -1;
        }

        private int CountHealthyWorkers(PartitionWorker[] workers)
        {
            int count = 0;
            foreach (var worker in workers)
            {
                if (worker.IsHealthy) count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Worker aislado por partición para evitar contención de recursos
    /// </summary>
    internal class PartitionWorker : IDisposable
    {
        private readonly int _partitionId;
        private readonly ILoggerService _logger;
        private readonly Channel<HttpRequestTask> _channel;
        private readonly ChannelWriter<HttpRequestTask> _writer;
        private readonly ChannelReader<HttpRequestTask> _reader;
        private readonly Task _processorTask;
        private readonly CancellationToken _cancellationToken;
        private volatile bool _disposed = false;
        
        // Estado de salud del worker
        private volatile bool _isHealthy = true;
        private volatile int _queueDepth = 0;
        private long _processedCount = 0;
        private long _errorCount = 0;

        public int PartitionId => _partitionId;
        public bool IsHealthy => _isHealthy && !_disposed;
        public bool HasCapacity => _queueDepth < _maxCapacity;
        public int QueueDepth => _queueDepth;
        public long ProcessedCount => Interlocked.Read(ref _processedCount);
        public long ErrorCount => Interlocked.Read(ref _errorCount);

        private readonly int _maxCapacity;

        public PartitionWorker(int partitionId, RequestProcessorConfiguration config, ILoggerService logger, CancellationToken cancellationToken)
        {
            _partitionId = partitionId;
            _logger = logger;
            _cancellationToken = cancellationToken;
            _maxCapacity = config.MaxQueueDepthPerPartition;

            // Crear canal aislado para esta partición
            var channelOptions = new BoundedChannelOptions(_maxCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,    // Un solo hilo consumidor por partición (evita contención)
                SingleWriter = false,   // Múltiples hilos pueden encolar
                AllowSynchronousContinuations = false
            };

            _channel = Channel.CreateBounded<HttpRequestTask>(channelOptions);
            _writer = _channel.Writer;
            _reader = _channel.Reader;

            // Iniciar hilo procesador dedicado para esta partición
            _processorTask = Task.Run(ProcessRequestsAsync, cancellationToken);

            _logger.LogDebug($"[PARTITION-WORKER-{_partitionId}] Initialized with capacity {_maxCapacity}");
        }

        public async Task<bool> TryEnqueueAsync(HttpRequestTask requestTask)
        {
            if (_disposed || !_isHealthy) return false;

            try
            {
                // Intento de escritura sin bloqueo (evita contención)
                if (_writer.TryWrite(requestTask))
                {
                    Interlocked.Increment(ref _queueDepth);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PARTITION-WORKER-{_partitionId}] Error enqueueing request: {ex}");
                return false;
            }
        }

        private async Task ProcessRequestsAsync()
        {
            _logger.LogInformation($"[PARTITION-WORKER-{_partitionId}] Started processing requests");

            try
            {
                await foreach (var requestTask in _reader.ReadAllAsync(_cancellationToken))
                {
                    try
                    {
                        // Procesar request en hilo dedicado (sin contención)
                        await requestTask.Handler(requestTask.Context);
                        
                        Interlocked.Increment(ref _processedCount);
                        Interlocked.Decrement(ref _queueDepth);
                        
                        _logger.LogDebug($"[PARTITION-WORKER-{_partitionId}] Processed request {requestTask.RequestId}");
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _errorCount);
                        Interlocked.Decrement(ref _queueDepth);
                        
                        _logger.LogError($"[PARTITION-WORKER-{_partitionId}] Error processing request {requestTask.RequestId}: {ex}");
                        
                        // Circuit breaker: Determinar si este worker debe marcarse como no saludable
                        var errorRate = ProcessedCount > 0 ? (ErrorCount * 100.0 / ProcessedCount) : 0;
                        if (ErrorCount > 10 && errorRate > 25) // 25% error threshold
                        {
                            _isHealthy = false;
                            _logger.LogWarning($"[PARTITION-WORKER-{_partitionId}] Circuit breaker OPEN - marked as unhealthy (Error rate: {errorRate:F1}%, Errors: {ErrorCount}/{ProcessedCount})");

                            // Schedule recovery attempt after cooldown period
                            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
                            {
                                if (ErrorCount > ProcessedCount / 2) return; // Still too many errors

                                _isHealthy = true;
                                _logger.LogInformation($"[PARTITION-WORKER-{_partitionId}] Circuit breaker HALF-OPEN - attempting recovery");
                            });
                        }
                        // Auto-recovery for workers with improving error rates
                        else if (!_isHealthy && ErrorCount < 5 && errorRate < 10)
                        {
                            _isHealthy = true;
                            _logger.LogInformation($"[PARTITION-WORKER-{_partitionId}] Circuit breaker CLOSED - worker recovered (Error rate: {errorRate:F1}%)");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($"[PARTITION-WORKER-{_partitionId}] Processing cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PARTITION-WORKER-{_partitionId}] Critical error in processing loop: {ex}");
                _isHealthy = false;
            }
            
            _logger.LogInformation($"[PARTITION-WORKER-{_partitionId}] Stopped processing. Processed: {ProcessedCount}, Errors: {ErrorCount}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _logger.LogDebug($"[PARTITION-WORKER-{_partitionId}] Disposing...");

            // Complete the writer to allow the processing loop to finish gracefully.
            // The cancellation token is managed by the parent processor and should have been triggered.
            _writer.TryComplete();

            // Avoid blocking with .Wait() to prevent deadlocks.
            // A short, final wait after cancellation is a pragmatic compromise.
            try
            {
                if (!_processorTask.Wait(TimeSpan.FromSeconds(2)))
                {
                    _logger.LogWarning($"[PARTITION-WORKER-{_partitionId}] Processor task did not complete within the timeout during dispose.");
                }
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is TaskCanceledException || e is OperationCanceledException))
            {
                // This is an expected exception when the task is cancelled.
                _logger.LogDebug($"[PARTITION-WORKER-{_partitionId}] Processor task was successfully cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PARTITION-WORKER-{_partitionId}] Error during dispose: {ex}");
            }
            
            _logger.LogDebug($"[PARTITION-WORKER-{_partitionId}] Disposed");
        }
    }
}
