using FastApi_NetCore.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Diagnostics
{
    /// <summary>
    /// Analizador específico para conexiones HTTP y problemas de colgado de respuestas
    /// </summary>
    public class HttpConnectionAnalyzer
    {
        private readonly ILoggerService _logger;

        public HttpConnectionAnalyzer(ILoggerService logger)
        {
            _logger = logger;
        }

        public async Task AnalyzeHttpContextAsync(HttpListenerContext context, string stage)
        {
            var contextId = GetContextId(context);
            
            _logger.LogInformation($"[HTTP-ANALYZER] ===== ANÁLISIS HTTP CONTEXT [{stage}] =====");
            _logger.LogInformation($"[HTTP-ANALYZER] Context ID: {contextId}");
            
            try
            {
                await AnalyzeRequestAsync(context.Request, contextId);
                await AnalyzeResponseAsync(context.Response, contextId, stage);
                await AnalyzeConnectionStateAsync(context, contextId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HTTP-ANALYZER] Error analizando contexto {contextId}: {ex}");
            }
            
            _logger.LogInformation($"[HTTP-ANALYZER] ===== FIN ANÁLISIS [{stage}] =====");
        }

        private async Task AnalyzeRequestAsync(HttpListenerRequest request, string contextId)
        {
            _logger.LogInformation($"[HTTP-ANALYZER] 📨 REQUEST ANALYSIS - Context: {contextId}");
            
            try
            {
                _logger.LogInformation($"[HTTP-ANALYZER]   Method: {request.HttpMethod}");
                _logger.LogInformation($"[HTTP-ANALYZER]   URL: {request.Url}");
                _logger.LogInformation($"[HTTP-ANALYZER]   Content-Length: {request.ContentLength64}");
                _logger.LogInformation($"[HTTP-ANALYZER]   Content-Type: {request.ContentType ?? "null"}");
                _logger.LogInformation($"[HTTP-ANALYZER]   User-Agent: {request.UserAgent ?? "null"}");
                _logger.LogInformation($"[HTTP-ANALYZER]   Remote-EndPoint: {request.RemoteEndPoint}");
                _logger.LogInformation($"[HTTP-ANALYZER]   Local-EndPoint: {request.LocalEndPoint}");
                _logger.LogInformation($"[HTTP-ANALYZER]   Keep-Alive: {request.KeepAlive}");
                _logger.LogInformation($"[HTTP-ANALYZER]   Protocol-Version: {request.ProtocolVersion}");
                
                // Analizar headers
                _logger.LogInformation($"[HTTP-ANALYZER]   Headers Count: {request.Headers.Count}");
                foreach (string key in request.Headers.AllKeys ?? new string[0])
                {
                    if (key != null)
                    {
                        var value = request.Headers[key];
                        _logger.LogInformation($"[HTTP-ANALYZER]     {key}: {value}");
                    }
                }

                // Verificar si hay cuerpo de request
                if (request.HasEntityBody && request.ContentLength64 > 0)
                {
                    _logger.LogInformation($"[HTTP-ANALYZER]   Body presente - Size: {request.ContentLength64} bytes");
                    
                    // Verificar estado del stream
                    var reflectionAnalysis = AnalyzeStreamWithReflection(request.InputStream, "Request.InputStream");
                    _logger.LogInformation($"[HTTP-ANALYZER]   InputStream: {reflectionAnalysis}");
                }
                else
                {
                    _logger.LogInformation($"[HTTP-ANALYZER]   Sin body en request");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HTTP-ANALYZER] Error analizando request: {ex}");
            }
        }

        private async Task AnalyzeResponseAsync(HttpListenerResponse response, string contextId, string stage)
        {
            _logger.LogInformation($"[HTTP-ANALYZER] 📤 RESPONSE ANALYSIS - Context: {contextId} - Stage: {stage}");
            
            try
            {
                _logger.LogInformation($"[HTTP-ANALYZER]   Status-Code: {response.StatusCode}");
                _logger.LogInformation($"[HTTP-ANALYZER]   Status-Description: {response.StatusDescription ?? "null"}");
                _logger.LogInformation($"[HTTP-ANALYZER]   Content-Length: {response.ContentLength64}");
                _logger.LogInformation($"[HTTP-ANALYZER]   Content-Type: {response.ContentType ?? "null"}");
                _logger.LogInformation($"[HTTP-ANALYZER]   Keep-Alive: {response.KeepAlive}");
                
                // Verificar estado crítico del OutputStream
                var outputStreamAnalysis = AnalyzeStreamWithReflection(response.OutputStream, "Response.OutputStream");
                _logger.LogInformation($"[HTTP-ANALYZER]   OutputStream: {outputStreamAnalysis}");
                
                // Verificar si se puede escribir
                bool canWrite = false;
                try
                {
                    canWrite = response.OutputStream.CanWrite;
                    _logger.LogInformation($"[HTTP-ANALYZER]   OutputStream.CanWrite: {canWrite}");
                }
                catch (ObjectDisposedException)
                {
                    _logger.LogWarning($"[HTTP-ANALYZER]   OutputStream.CanWrite: DISPOSED");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[HTTP-ANALYZER]   OutputStream.CanWrite: ERROR - {ex.Message}");
                }

                // Analizar headers de response
                _logger.LogInformation($"[HTTP-ANALYZER]   Response Headers Count: {response.Headers.Count}");
                foreach (string key in response.Headers.AllKeys ?? new string[0])
                {
                    if (key != null)
                    {
                        var value = response.Headers[key];
                        _logger.LogInformation($"[HTTP-ANALYZER]     {key}: {value}");
                    }
                }

                // Verificar si se ha enviado la response
                try
                {
                    var responseType = response.GetType();
                    var sentField = responseType.GetField("_responseState", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (sentField != null)
                    {
                        var state = sentField.GetValue(response);
                        _logger.LogInformation($"[HTTP-ANALYZER]   Response State (interno): {state}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[HTTP-ANALYZER]   No se pudo obtener estado interno: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HTTP-ANALYZER] Error analizando response: {ex}");
            }
        }

        private async Task AnalyzeConnectionStateAsync(HttpListenerContext context, string contextId)
        {
            _logger.LogInformation($"[HTTP-ANALYZER] 🔌 CONNECTION STATE ANALYSIS - Context: {contextId}");
            
            try
            {
                // Analizar el contexto usando reflexión
                var contextType = context.GetType();
                _logger.LogInformation($"[HTTP-ANALYZER]   Context Type: {contextType.FullName}");
                
                // Buscar campos relacionados con conexión
                var fields = contextType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    if (field.Name.ToLowerInvariant().Contains("connection") || 
                        field.Name.ToLowerInvariant().Contains("socket") ||
                        field.Name.ToLowerInvariant().Contains("client"))
                    {
                        try
                        {
                            var value = field.GetValue(context);
                            _logger.LogInformation($"[HTTP-ANALYZER]   {field.Name}: {value?.GetType().Name ?? "null"}");
                            
                            if (value != null && field.Name.ToLowerInvariant().Contains("socket"))
                            {
                                await AnalyzeSocketState(value, contextId);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[HTTP-ANALYZER]   Error accessing {field.Name}: {ex.Message}");
                        }
                    }
                }
                
                // Verificar si el contexto sigue activo
                try
                {
                    var requestActive = context.Request != null;
                    var responseActive = context.Response != null;
                    _logger.LogInformation($"[HTTP-ANALYZER]   Request Active: {requestActive}");
                    _logger.LogInformation($"[HTTP-ANALYZER]   Response Active: {responseActive}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[HTTP-ANALYZER]   Error verificando actividad: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HTTP-ANALYZER] Error analizando estado conexión: {ex}");
            }
        }

        private async Task AnalyzeSocketState(object socketObject, string contextId)
        {
            try
            {
                var socketType = socketObject.GetType();
                _logger.LogInformation($"[HTTP-ANALYZER]   Socket Type: {socketType.Name}");
                
                // Buscar propiedades relevantes del socket
                var properties = new[] { "Connected", "Available", "LocalEndPoint", "RemoteEndPoint" };
                
                foreach (var propName in properties)
                {
                    try
                    {
                        var prop = socketType.GetProperty(propName);
                        if (prop != null)
                        {
                            var value = prop.GetValue(socketObject);
                            _logger.LogInformation($"[HTTP-ANALYZER]     {propName}: {value}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[HTTP-ANALYZER]     Error accediendo a {propName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HTTP-ANALYZER] Error analizando socket: {ex}");
            }
        }

        private string AnalyzeStreamWithReflection(Stream stream, string streamName)
        {
            try
            {
                var streamType = stream.GetType();
                var analysis = new StringBuilder();
                
                analysis.Append($"Type={streamType.Name}, ");
                analysis.Append($"CanRead={stream.CanRead}, ");
                analysis.Append($"CanWrite={stream.CanWrite}, ");
                analysis.Append($"CanSeek={stream.CanSeek}, ");
                
                try
                {
                    analysis.Append($"Position={stream.Position}, ");
                    analysis.Append($"Length={stream.Length}");
                }
                catch (NotSupportedException)
                {
                    analysis.Append("Position/Length=NotSupported");
                }
                catch (ObjectDisposedException)
                {
                    analysis.Append("Position/Length=DISPOSED");
                }
                
                // Buscar campos internos importantes
                var fields = streamType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields.Take(3)) // Solo los primeros 3
                {
                    try
                    {
                        var value = field.GetValue(stream);
                        analysis.Append($", {field.Name}={value?.ToString() ?? "null"}");
                    }
                    catch
                    {
                        // Ignorar errores de acceso
                    }
                }
                
                return analysis.ToString();
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }

        private string GetContextId(HttpListenerContext context)
        {
            try
            {
                return $"{context.Request.RemoteEndPoint}_{DateTime.Now.Ticks}";
            }
            catch
            {
                return $"UNKNOWN_{DateTime.Now.Ticks}";
            }
        }

        public async Task LogFlushAttemptAsync(HttpListenerResponse response, string operation, string contextId)
        {
            _logger.LogInformation($"[HTTP-ANALYZER] 🔄 FLUSH ATTEMPT - Operation: {operation} - Context: {contextId}");
            
            try
            {
                var canWrite = response.OutputStream.CanWrite;
                var contentLength = response.ContentLength64;
                
                _logger.LogInformation($"[HTTP-ANALYZER]   Before {operation}:");
                _logger.LogInformation($"[HTTP-ANALYZER]     CanWrite: {canWrite}");
                _logger.LogInformation($"[HTTP-ANALYZER]     ContentLength: {contentLength}");
                _logger.LogInformation($"[HTTP-ANALYZER]     StatusCode: {response.StatusCode}");
                
                // Intentar flush
                try
                {
                    response.OutputStream.Flush();
                    _logger.LogInformation($"[HTTP-ANALYZER]     Flush: SUCCESS");
                }
                catch (Exception flushEx)
                {
                    _logger.LogError($"[HTTP-ANALYZER]     Flush: ERROR - {flushEx.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HTTP-ANALYZER] Error en flush attempt: {ex}");
            }
        }

        public async Task LogResponseCloseAsync(HttpListenerResponse response, string contextId)
        {
            _logger.LogInformation($"[HTTP-ANALYZER] 🔚 RESPONSE CLOSE - Context: {contextId}");
            
            try
            {
                var statusCode = response.StatusCode;
                var contentLength = response.ContentLength64;
                
                _logger.LogInformation($"[HTTP-ANALYZER]   Final State:");
                _logger.LogInformation($"[HTTP-ANALYZER]     StatusCode: {statusCode}");
                _logger.LogInformation($"[HTTP-ANALYZER]     ContentLength: {contentLength}");
                
                try
                {
                    var canWrite = response.OutputStream.CanWrite;
                    _logger.LogInformation($"[HTTP-ANALYZER]     OutputStream.CanWrite: {canWrite}");
                }
                catch (ObjectDisposedException)
                {
                    _logger.LogInformation($"[HTTP-ANALYZER]     OutputStream: ALREADY DISPOSED");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HTTP-ANALYZER] Error en response close: {ex}");
            }
        }
    }
}