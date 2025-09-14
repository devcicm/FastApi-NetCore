using FastApi_NetCore.Core.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Services
{
    /// <summary>
    /// Implementación en memoria del Event Bus para desarrollo y testing
    /// </summary>
    public class InMemoryEventBus : IEventBus, IDisposable
    {
        private readonly ConcurrentDictionary<Type, List<object>> _handlers;
        private readonly Channel<IEvent> _eventChannel;
        private readonly ChannelWriter<IEvent> _eventWriter;
        private readonly ChannelReader<IEvent> _eventReader;
        private readonly ILoggerService _logger;
        private readonly Task _processingTask;
        private readonly CancellationTokenSource _cancellationTokenSource;
        
        // Estadísticas
        private readonly ConcurrentDictionary<string, int> _eventsByType;
        private long _totalEventsPublished;
        private long _totalProcessingTime;
        private DateTime _lastEventTimestamp;
        private bool _disposed = false;

        public InMemoryEventBus(ILoggerService logger)
        {
            _logger = logger;
            _handlers = new ConcurrentDictionary<Type, List<object>>();
            _eventsByType = new ConcurrentDictionary<string, int>();
            _cancellationTokenSource = new CancellationTokenSource();

            // Canal para procesamiento asíncrono de eventos
            var options = new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };

            _eventChannel = Channel.CreateBounded<IEvent>(options);
            _eventWriter = _eventChannel.Writer;
            _eventReader = _eventChannel.Reader;

            // Iniciar tarea de procesamiento en background
            _processingTask = Task.Run(ProcessEventsAsync, _cancellationTokenSource.Token);

            _logger.LogInformation("[EVENT-BUS] InMemoryEventBus inicializado");
        }

        /// <summary>
        /// Publica un evento de forma asíncrona
        /// </summary>
        public async Task PublishAsync<T>(T @event) where T : IEvent
        {
            if (_disposed) return;

            try
            {
                await _eventWriter.WriteAsync(@event, _cancellationTokenSource.Token);
                
                // Actualizar estadísticas
                Interlocked.Increment(ref _totalEventsPublished);
                _eventsByType.AddOrUpdate(@event.EventName, 1, (_, count) => count + 1);
                _lastEventTimestamp = DateTime.UtcNow;

                _logger.LogDebug($"[EVENT-BUS] Evento publicado: {@event.EventName} (ID: {@event.EventId})");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[EVENT-BUS] Error publicando evento {@event.EventName}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Suscribe un handler a un tipo de evento específico
        /// </summary>
        public void Subscribe<T>(IEventHandler<T> handler) where T : IEvent
        {
            var eventType = typeof(T);
            var handlersList = _handlers.GetOrAdd(eventType, _ => new List<object>());
            
            lock (handlersList)
            {
                handlersList.Add(handler);
            }

            _logger.LogInformation($"[EVENT-BUS] Handler suscrito para evento {eventType.Name}");
        }

        /// <summary>
        /// Suscribe un handler usando una función lambda
        /// </summary>
        public void Subscribe<T>(Func<T, Task> handler) where T : IEvent
        {
            var lambdaHandler = new LambdaEventHandler<T>(handler);
            Subscribe(lambdaHandler);
        }

        /// <summary>
        /// Desuscribe un handler de un tipo de evento
        /// </summary>
        public void Unsubscribe<T>(IEventHandler<T> handler) where T : IEvent
        {
            var eventType = typeof(T);
            if (_handlers.TryGetValue(eventType, out var handlersList))
            {
                lock (handlersList)
                {
                    handlersList.Remove(handler);
                }
                _logger.LogInformation($"[EVENT-BUS] Handler desuscrito para evento {eventType.Name}");
            }
        }

        /// <summary>
        /// Procesa eventos en background
        /// </summary>
        private async Task ProcessEventsAsync()
        {
            _logger.LogInformation("[EVENT-BUS] Iniciando procesamiento de eventos en background");

            try
            {
                await foreach (var @event in _eventReader.ReadAllAsync(_cancellationTokenSource.Token))
                {
                    await ProcessEventAsync(@event);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[EVENT-BUS] Procesamiento de eventos cancelado");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[EVENT-BUS] Error en procesamiento de eventos: {ex.Message}");
            }
        }

        /// <summary>
        /// Procesa un evento individual
        /// </summary>
        private async Task ProcessEventAsync(IEvent @event)
        {
            var eventType = @event.GetType();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (_handlers.TryGetValue(eventType, out var handlersList))
                {
                    List<object> handlersSnapshot;
                    lock (handlersList)
                    {
                        handlersSnapshot = new List<object>(handlersList);
                    }

                    var tasks = new List<Task>();

                    foreach (var handler in handlersSnapshot)
                    {
                        tasks.Add(InvokeHandlerAsync(handler, @event));
                    }

                    // Ejecutar todos los handlers en paralelo
                    await Task.WhenAll(tasks);

                    _logger.LogDebug($"[EVENT-BUS] Evento {@event.EventName} procesado por {handlersSnapshot.Count} handlers");
                }
                else
                {
                    _logger.LogDebug($"[EVENT-BUS] No hay handlers para evento {@event.EventName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[EVENT-BUS] Error procesando evento {@event.EventName}: {ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
                Interlocked.Add(ref _totalProcessingTime, stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Invoca un handler específico
        /// </summary>
        private async Task InvokeHandlerAsync(object handler, IEvent @event)
        {
            try
            {
                // Usar reflexión para invocar el método HandleAsync del handler
                var handlerType = handler.GetType();
                var method = handlerType.GetMethod("HandleAsync");

                if (method != null)
                {
                    var result = method.Invoke(handler, new object[] { @event });
                    if (result is Task task)
                    {
                        await task;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[EVENT-BUS] Error en handler para evento {@event.EventName}: {ex.Message}");
                // No rethrow para no afectar otros handlers
            }
        }

        /// <summary>
        /// Obtiene estadísticas del event bus
        /// </summary>
        public EventBusStatistics GetStatistics()
        {
            var queueCount = 0;
            try
            {
                // Aproximación del número de eventos en cola
                queueCount = _eventReader.CanCount ? _eventReader.Count : 0;
            }
            catch
            {
                // Ignorar errores al obtener el count
            }

            var totalEvents = _totalEventsPublished;
            var totalTime = _totalProcessingTime;
            var averageProcessingTime = totalEvents > 0 ? TimeSpan.FromMilliseconds(totalTime / (double)totalEvents) : TimeSpan.Zero;

            return new EventBusStatistics
            {
                TotalEventsPublished = (int)totalEvents,
                TotalSubscriptions = _handlers.Values.Sum(list => list.Count),
                EventsInQueue = queueCount,
                EventsByType = new Dictionary<string, int>(_eventsByType),
                LastEventTimestamp = _lastEventTimestamp,
                AverageProcessingTime = averageProcessingTime
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _logger.LogInformation("[EVENT-BUS] Iniciando shutdown del Event Bus");

                    // Señalar cancelación
                    _cancellationTokenSource.Cancel();

                    // Cerrar el writer para señalar fin de eventos
                    _eventWriter.Complete();

                    // Esperar a que termine el procesamiento
                    if (!_processingTask.Wait(TimeSpan.FromSeconds(5)))
                    {
                        _logger.LogWarning("[EVENT-BUS] Timeout esperando fin de procesamiento");
                    }

                    _cancellationTokenSource.Dispose();
                    _logger.LogInformation("[EVENT-BUS] Event Bus disposed correctamente");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[EVENT-BUS] Error durante disposal: {ex.Message}");
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Wrapper para handlers lambda
        /// </summary>
        private class LambdaEventHandler<T> : IEventHandler<T> where T : IEvent
        {
            private readonly Func<T, Task> _handler;

            public LambdaEventHandler(Func<T, Task> handler)
            {
                _handler = handler;
            }

            public async Task HandleAsync(T @event)
            {
                await _handler(@event);
            }
        }
    }
}