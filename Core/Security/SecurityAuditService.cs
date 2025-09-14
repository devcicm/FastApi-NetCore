using FastApi_NetCore.Core.Events;
using FastApi_NetCore.Core.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Security
{
    /// <summary>
    /// Tipos de eventos de seguridad
    /// </summary>
    public enum SecurityEventType
    {
        Authentication,
        Authorization,
        RateLimitViolation,
        IpBlocked,
        SuspiciousActivity,
        DataAccess,
        ConfigurationChange,
        SystemAccess,
        PluginActivity
    }

    /// <summary>
    /// Severidad del evento de seguridad
    /// </summary>
    public enum SecurityEventSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// Evento de auditoría de seguridad
    /// </summary>
    public class SecurityAuditEvent
    {
        public Guid EventId { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public SecurityEventType EventType { get; set; }
        public SecurityEventSeverity Severity { get; set; }
        public string Source { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string ClientIP { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public string Resource { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, object> Metadata { get; set; } = new();
        public bool Success { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty; // Para integridad
        public string PreviousEventHash { get; set; } = string.Empty; // Para cadena de integridad
    }

    /// <summary>
    /// Interface para el servicio de auditoría de seguridad
    /// </summary>
    public interface ISecurityAuditService
    {
        Task LogSecurityEventAsync(SecurityAuditEvent auditEvent);
        Task LogAuthenticationAttemptAsync(string userId, string clientIP, bool success, string? errorMessage = null);
        Task LogAuthorizationAttemptAsync(string userId, string resource, string action, bool success, string? roles = null);
        Task LogRateLimitViolationAsync(string clientIP, string endpoint, int attemptedRequests);
        Task LogSuspiciousActivityAsync(string clientIP, string description, Dictionary<string, object>? details = null);
        Task LogDataAccessAsync(string userId, string resource, string operation, bool success);
        Task LogSystemEventAsync(string component, string operation, bool success, Dictionary<string, object>? metadata = null);
        Task<IEnumerable<SecurityAuditEvent>> GetSecurityEventsAsync(DateTime from, DateTime to, SecurityEventType? eventType = null);
        Task<SecurityAuditStatistics> GetAuditStatisticsAsync();
        Task<bool> ValidateAuditIntegrityAsync();
    }

    /// <summary>
    /// Servicio de auditoría de seguridad con almacenamiento inmutable
    /// </summary>
    public class SecurityAuditService : ISecurityAuditService, IDisposable
    {
        private readonly ILoggerService _logger;
        private readonly IEventBus _eventBus;
        private readonly Channel<SecurityAuditEvent> _auditChannel;
        private readonly ChannelWriter<SecurityAuditEvent> _auditWriter;
        private readonly ChannelReader<SecurityAuditEvent> _auditReader;
        private readonly Task _processingTask;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly string _auditLogPath;
        private readonly SemaphoreSlim _fileLock;
        
        // Cache para estadísticas y búsquedas rápidas
        private readonly ConcurrentDictionary<SecurityEventType, long> _eventCounts;
        private readonly ConcurrentQueue<SecurityAuditEvent> _recentEvents;
        private string _lastEventHash = string.Empty;
        private long _totalEvents = 0;
        private bool _disposed = false;

        public SecurityAuditService(ILoggerService logger, IEventBus eventBus, string auditLogPath = "security_audit.log")
        {
            _logger = logger;
            _eventBus = eventBus;
            _auditLogPath = Path.GetFullPath(auditLogPath);
            _fileLock = new SemaphoreSlim(1, 1);
            _eventCounts = new ConcurrentDictionary<SecurityEventType, long>();
            _recentEvents = new ConcurrentQueue<SecurityAuditEvent>();
            _cancellationTokenSource = new CancellationTokenSource();

            // Configurar canal para procesamiento asíncrono
            var options = new BoundedChannelOptions(10000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };

            _auditChannel = Channel.CreateBounded<SecurityAuditEvent>(options);
            _auditWriter = _auditChannel.Writer;
            _auditReader = _auditChannel.Reader;

            // Asegurar que el directorio existe
            var directory = Path.GetDirectoryName(_auditLogPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Cargar último hash para continuidad de la cadena
            _ = Task.Run(LoadLastEventHashAsync);

            // Iniciar procesamiento en background
            _processingTask = Task.Run(ProcessAuditEventsAsync, _cancellationTokenSource.Token);

            // Suscribirse a eventos del sistema
            SubscribeToSystemEvents();

            _logger.LogInformation($"[SECURITY-AUDIT] Servicio iniciado. Log: {_auditLogPath}");
        }

        public async Task LogSecurityEventAsync(SecurityAuditEvent auditEvent)
        {
            if (_disposed) return;

            try
            {
                // Calcular hash para integridad
                auditEvent.PreviousEventHash = _lastEventHash;
                auditEvent.Hash = CalculateEventHash(auditEvent);

                await _auditWriter.WriteAsync(auditEvent, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SECURITY-AUDIT] Error logging security event: {ex.Message}");
            }
        }

        public async Task LogAuthenticationAttemptAsync(string userId, string clientIP, bool success, string? errorMessage = null)
        {
            var auditEvent = new SecurityAuditEvent
            {
                EventType = SecurityEventType.Authentication,
                Severity = success ? SecurityEventSeverity.Low : SecurityEventSeverity.Medium,
                Source = "Authentication",
                UserId = userId,
                ClientIP = clientIP,
                Action = "Login",
                Description = success ? "User authentication successful" : "User authentication failed",
                Success = success,
                ErrorCode = success ? string.Empty : "AUTH_FAILED"
            };

            if (!success && !string.IsNullOrEmpty(errorMessage))
            {
                auditEvent.Metadata["ErrorMessage"] = errorMessage;
            }

            await LogSecurityEventAsync(auditEvent);
        }

        public async Task LogAuthorizationAttemptAsync(string userId, string resource, string action, bool success, string? roles = null)
        {
            var auditEvent = new SecurityAuditEvent
            {
                EventType = SecurityEventType.Authorization,
                Severity = success ? SecurityEventSeverity.Low : SecurityEventSeverity.High,
                Source = "Authorization",
                UserId = userId,
                Resource = resource,
                Action = action,
                Description = success ? "Authorization granted" : "Authorization denied",
                Success = success,
                ErrorCode = success ? string.Empty : "AUTHZ_DENIED"
            };

            if (!string.IsNullOrEmpty(roles))
            {
                auditEvent.Metadata["RequiredRoles"] = roles;
            }

            await LogSecurityEventAsync(auditEvent);
        }

        public async Task LogRateLimitViolationAsync(string clientIP, string endpoint, int attemptedRequests)
        {
            var auditEvent = new SecurityAuditEvent
            {
                EventType = SecurityEventType.RateLimitViolation,
                Severity = SecurityEventSeverity.Medium,
                Source = "RateLimit",
                ClientIP = clientIP,
                Resource = endpoint,
                Action = "Request",
                Description = $"Rate limit exceeded: {attemptedRequests} requests",
                Success = false,
                ErrorCode = "RATE_LIMIT_EXCEEDED"
            };

            auditEvent.Metadata["AttemptedRequests"] = attemptedRequests;

            await LogSecurityEventAsync(auditEvent);
        }

        public async Task LogSuspiciousActivityAsync(string clientIP, string description, Dictionary<string, object>? details = null)
        {
            var auditEvent = new SecurityAuditEvent
            {
                EventType = SecurityEventType.SuspiciousActivity,
                Severity = SecurityEventSeverity.High,
                Source = "SecurityMonitor",
                ClientIP = clientIP,
                Description = description,
                Success = false,
                ErrorCode = "SUSPICIOUS_ACTIVITY"
            };

            if (details != null)
            {
                foreach (var detail in details)
                {
                    auditEvent.Metadata[detail.Key] = detail.Value;
                }
            }

            await LogSecurityEventAsync(auditEvent);
        }

        public async Task LogDataAccessAsync(string userId, string resource, string operation, bool success)
        {
            var auditEvent = new SecurityAuditEvent
            {
                EventType = SecurityEventType.DataAccess,
                Severity = success ? SecurityEventSeverity.Low : SecurityEventSeverity.Medium,
                Source = "DataAccess",
                UserId = userId,
                Resource = resource,
                Action = operation,
                Description = $"Data access: {operation} on {resource}",
                Success = success
            };

            await LogSecurityEventAsync(auditEvent);
        }

        public async Task LogSystemEventAsync(string component, string operation, bool success, Dictionary<string, object>? metadata = null)
        {
            var auditEvent = new SecurityAuditEvent
            {
                EventType = SecurityEventType.SystemAccess,
                Severity = success ? SecurityEventSeverity.Low : SecurityEventSeverity.High,
                Source = component,
                Action = operation,
                Description = $"System operation: {operation} by {component}",
                Success = success
            };

            if (metadata != null)
            {
                foreach (var item in metadata)
                {
                    auditEvent.Metadata[item.Key] = item.Value;
                }
            }

            await LogSecurityEventAsync(auditEvent);
        }

        public async Task<IEnumerable<SecurityAuditEvent>> GetSecurityEventsAsync(DateTime from, DateTime to, SecurityEventType? eventType = null)
        {
            var events = new List<SecurityAuditEvent>();

            try
            {
                await _fileLock.WaitAsync();

                if (!File.Exists(_auditLogPath))
                {
                    return events;
                }

                await using var fileStream = new FileStream(_auditLogPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(fileStream);

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    try
                    {
                        var auditEvent = JsonSerializer.Deserialize<SecurityAuditEvent>(line);
                        if (auditEvent != null &&
                            auditEvent.Timestamp >= from &&
                            auditEvent.Timestamp <= to &&
                            (eventType == null || auditEvent.EventType == eventType))
                        {
                            events.Add(auditEvent);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning($"[SECURITY-AUDIT] Error parsing audit log line: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SECURITY-AUDIT] Error reading security events: {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }

            return events;
        }

        public async Task<SecurityAuditStatistics> GetAuditStatisticsAsync()
        {
            return new SecurityAuditStatistics
            {
                TotalEvents = _totalEvents,
                EventsByType = new Dictionary<SecurityEventType, long>(_eventCounts),
                StartTime = DateTime.UtcNow.AddDays(-1), // Placeholder
                LastEventTime = _recentEvents.TryPeek(out var lastEvent) ? lastEvent.Timestamp : DateTime.MinValue,
                IntegrityStatus = await ValidateAuditIntegrityAsync()
            };
        }

        public async Task<bool> ValidateAuditIntegrityAsync()
        {
            try
            {
                await _fileLock.WaitAsync();

                if (!File.Exists(_auditLogPath))
                {
                    return true; // No hay archivo, integridad OK
                }

                await using var fileStream = new FileStream(_auditLogPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(fileStream);

                string? line;
                string previousHash = string.Empty;
                int validatedEvents = 0;

                while ((line = await reader.ReadLineAsync()) != null)
                {
                    try
                    {
                        var auditEvent = JsonSerializer.Deserialize<SecurityAuditEvent>(line);
                        if (auditEvent != null)
                        {
                            // Validar hash del evento
                            var calculatedHash = CalculateEventHash(auditEvent);
                            if (calculatedHash != auditEvent.Hash)
                            {
                                _logger.LogError($"[SECURITY-AUDIT] Integrity violation: Event {auditEvent.EventId} hash mismatch");
                                return false;
                            }

                            // Validar cadena de hash
                            if (auditEvent.PreviousEventHash != previousHash)
                            {
                                _logger.LogError($"[SECURITY-AUDIT] Integrity violation: Event {auditEvent.EventId} chain broken");
                                return false;
                            }

                            previousHash = auditEvent.Hash;
                            validatedEvents++;
                        }
                    }
                    catch (JsonException)
                    {
                        return false; // Archivo corrupto
                    }
                }

                _logger.LogInformation($"[SECURITY-AUDIT] Integrity validation passed: {validatedEvents} events validated");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SECURITY-AUDIT] Error validating integrity: {ex.Message}");
                return false;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private async Task ProcessAuditEventsAsync()
        {
            _logger.LogInformation("[SECURITY-AUDIT] Starting audit event processing");

            try
            {
                await foreach (var auditEvent in _auditReader.ReadAllAsync(_cancellationTokenSource.Token))
                {
                    await WriteAuditEventToFileAsync(auditEvent);
                    UpdateStatistics(auditEvent);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[SECURITY-AUDIT] Audit processing cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SECURITY-AUDIT] Error in audit processing: {ex.Message}");
            }
        }

        private async Task WriteAuditEventToFileAsync(SecurityAuditEvent auditEvent)
        {
            try
            {
                await _fileLock.WaitAsync(_cancellationTokenSource.Token);

                var json = JsonSerializer.Serialize(auditEvent, new JsonSerializerOptions
                {
                    WriteIndented = false
                });

                await using var fileStream = new FileStream(_auditLogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                await using var writer = new StreamWriter(fileStream, Encoding.UTF8);
                await writer.WriteLineAsync(json);
                await writer.FlushAsync();

                _lastEventHash = auditEvent.Hash;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SECURITY-AUDIT] Error writing audit event: {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private void UpdateStatistics(SecurityAuditEvent auditEvent)
        {
            Interlocked.Increment(ref _totalEvents);
            _eventCounts.AddOrUpdate(auditEvent.EventType, 1, (_, count) => count + 1);

            // Mantener cache de eventos recientes (últimos 1000)
            _recentEvents.Enqueue(auditEvent);
            while (_recentEvents.Count > 1000)
            {
                _recentEvents.TryDequeue(out _);
            }
        }

        private string CalculateEventHash(SecurityAuditEvent auditEvent)
        {
            // Crear string para hash excluyendo el hash mismo
            var dataForHash = $"{auditEvent.EventId}{auditEvent.Timestamp:O}{auditEvent.EventType}{auditEvent.Severity}" +
                             $"{auditEvent.Source}{auditEvent.UserId}{auditEvent.ClientIP}{auditEvent.Resource}" +
                             $"{auditEvent.Action}{auditEvent.Description}{auditEvent.Success}{auditEvent.ErrorCode}" +
                             $"{auditEvent.PreviousEventHash}";

            // Agregar metadata
            foreach (var metadata in auditEvent.Metadata.OrderBy(x => x.Key))
            {
                dataForHash += $"{metadata.Key}:{metadata.Value}";
            }

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(dataForHash));
            return Convert.ToBase64String(hashBytes);
        }

        private async Task LoadLastEventHashAsync()
        {
            try
            {
                if (!File.Exists(_auditLogPath))
                {
                    return;
                }

                // Leer el último evento del archivo
                await using var fileStream = new FileStream(_auditLogPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(fileStream);

                string? lastLine = null;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    lastLine = line;
                }

                if (!string.IsNullOrEmpty(lastLine))
                {
                    var lastEvent = JsonSerializer.Deserialize<SecurityAuditEvent>(lastLine);
                    if (lastEvent != null)
                    {
                        _lastEventHash = lastEvent.Hash;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SECURITY-AUDIT] Could not load last event hash: {ex.Message}");
            }
        }

        private void SubscribeToSystemEvents()
        {
            // Suscribirse a eventos de autenticación
            _eventBus.Subscribe<UserAuthenticatedEvent>(async evt =>
            {
                await LogAuthenticationAttemptAsync(evt.UserId, evt.ClientIP, true);
            });

            _eventBus.Subscribe<UserAuthenticationFailedEvent>(async evt =>
            {
                await LogAuthenticationAttemptAsync(evt.AttemptedUserId, evt.ClientIP, false, evt.FailureReason);
            });

            // Suscribirse a eventos de rate limiting
            _eventBus.Subscribe<RateLimitExceededEvent>(async evt =>
            {
                await LogRateLimitViolationAsync(evt.ClientId, evt.Endpoint, evt.RequestCount);
            });

            // Suscribirse a eventos de seguridad
            _eventBus.Subscribe<SecurityViolationEvent>(async evt =>
            {
                await LogSuspiciousActivityAsync(evt.ClientIP, evt.Description, evt.Details);
            });

            _eventBus.Subscribe<IpBlockedEvent>(async evt =>
            {
                var auditEvent = new SecurityAuditEvent
                {
                    EventType = SecurityEventType.IpBlocked,
                    Severity = SecurityEventSeverity.High,
                    Source = "IPFilter",
                    ClientIP = evt.ClientIP,
                    Action = "Block",
                    Description = evt.Reason,
                    Success = true
                };

                if (evt.BlockDuration.HasValue)
                {
                    auditEvent.Metadata["BlockDuration"] = evt.BlockDuration.Value.ToString();
                }

                await LogSecurityEventAsync(auditEvent);
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationTokenSource.Cancel();
                _auditWriter.Complete();

                try
                {
                    _processingTask.Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[SECURITY-AUDIT] Error during shutdown: {ex.Message}");
                }

                _fileLock.Dispose();
                _cancellationTokenSource.Dispose();
                _disposed = true;

                _logger.LogInformation("[SECURITY-AUDIT] Security audit service disposed");
            }
        }
    }

    /// <summary>
    /// Estadísticas del sistema de auditoría
    /// </summary>
    public class SecurityAuditStatistics
    {
        public long TotalEvents { get; set; }
        public Dictionary<SecurityEventType, long> EventsByType { get; set; } = new();
        public DateTime StartTime { get; set; }
        public DateTime LastEventTime { get; set; }
        public bool IntegrityStatus { get; set; }
    }
}