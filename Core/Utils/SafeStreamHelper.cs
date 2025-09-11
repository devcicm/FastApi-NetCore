using FastApi_NetCore.Core.Interfaces;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Utils
{
    /// <summary>
    /// Helper para manejo seguro de streams sin cerrarlos prematuramente
    /// </summary>
    public static class SafeStreamHelper
    {
        /// <summary>
        /// Escribe datos a un stream de respuesta de forma segura
        /// </summary>
        public static async Task<bool> SafeWriteToResponseAsync(
            HttpListenerResponse response, 
            byte[] data, 
            string contentType = "application/json",
            ILoggerService logger = null)
        {
            if (response == null || data == null) return false;

            try
            {
                // Verificar múltiples condiciones de disponibilidad
                if (!IsResponseWritable(response))
                {
                    logger?.LogWarning("[SAFE-STREAM] Response stream is not writable");
                    return false;
                }

                // Configurar headers antes de escribir
                response.ContentType = contentType;
                response.ContentLength64 = data.Length;
                
                // Escribir de forma segura con timeout
                using var timeoutCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
                
                await response.OutputStream.WriteAsync(data, 0, data.Length, timeoutCts.Token);
                await response.OutputStream.FlushAsync(timeoutCts.Token);
                
                logger?.LogDebug($"[SAFE-STREAM] Successfully wrote {data.Length} bytes to response");
                return true;
            }
            catch (ObjectDisposedException)
            {
                logger?.LogDebug("[SAFE-STREAM] Response already disposed");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                logger?.LogWarning($"[SAFE-STREAM] Invalid operation: {ex.Message}");
                return false;
            }
            catch (HttpListenerException ex)
            {
                logger?.LogWarning($"[SAFE-STREAM] HTTP error: {ex.Message}");
                return false;
            }
            catch (OperationCanceledException)
            {
                logger?.LogWarning("[SAFE-STREAM] Write operation timed out");
                return false;
            }
            catch (Exception ex)
            {
                logger?.LogError($"[SAFE-STREAM] Unexpected error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Verifica si la respuesta es escribible sin tirar excepciones
        /// </summary>
        public static bool IsResponseWritable(HttpListenerResponse response)
        {
            if (response == null) return false;

            try
            {
                // Verificar si el stream de salida existe y puede escribir
                var stream = response.OutputStream;
                if (stream == null) return false;
                
                // Verificar si el stream puede escribir
                if (!stream.CanWrite) return false;
                
                // Verificar si la respuesta no fue enviada ya
                // Esta es una verificación indirecta - si podemos acceder a estas propiedades
                // significa que la respuesta aún está activa
                _ = response.StatusCode;
                _ = response.ContentType;
                
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Cierra la respuesta de forma segura solo si es necesario
        /// </summary>
        public static void SafeCloseResponse(HttpListenerResponse response, bool forceClose = false, ILoggerService logger = null)
        {
            if (response == null) return;

            try
            {
                if (forceClose || !IsResponseWritable(response))
                {
                    return; // No cerrar si está siendo usado
                }

                // Solo cerrar si explícitamente se solicita Y la respuesta está completa
                if (forceClose)
                {
                    response.OutputStream?.Close();
                    response.Close();
                    logger?.LogDebug("[SAFE-STREAM] Response closed safely");
                }
            }
            catch (ObjectDisposedException)
            {
                // Ya está cerrado, ignorar
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"[SAFE-STREAM] Error closing response: {ex.Message}");
            }
        }

        /// <summary>
        /// Copia datos entre streams de forma segura con pools
        /// </summary>
        public static async Task<long> SafeCopyStreamAsync(
            Stream source, 
            Stream destination, 
            MemoryStreamPool memoryPool = null,
            ILoggerService logger = null)
        {
            if (source == null || destination == null) return 0;
            if (!source.CanRead || !destination.CanWrite) return 0;

            try
            {
                long totalBytes = 0;
                byte[] buffer = new byte[8192]; // 8KB buffer

                // Si hay pool disponible, usar buffer del pool
                PooledObjectWrapper<MemoryStream> pooledBuffer = null;
                if (memoryPool != null)
                {
                    try
                    {
                        pooledBuffer = memoryPool.Get();
                        if (pooledBuffer?.Object != null)
                        {
                            // Usar buffer más grande del pool si está disponible
                            buffer = new byte[Math.Max(8192, (int)pooledBuffer.Object.Capacity)];
                        }
                    }
                    catch
                    {
                        // Si falla el pool, usar buffer normal
                    }
                }

                using (pooledBuffer) // Se devuelve automáticamente al pool
                {
                    int bytesRead;
                    while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await destination.WriteAsync(buffer, 0, bytesRead);
                        totalBytes += bytesRead;
                    }

                    await destination.FlushAsync();
                }

                logger?.LogDebug($"[SAFE-STREAM] Copied {totalBytes} bytes between streams");
                return totalBytes;
            }
            catch (Exception ex)
            {
                logger?.LogError($"[SAFE-STREAM] Error copying streams: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Wrapper para ejecutar operaciones en streams de forma segura
        /// </summary>
        public static async Task<T> SafeStreamOperation<T>(
            Func<Task<T>> operation,
            T defaultValue = default(T),
            ILoggerService logger = null)
        {
            try
            {
                return await operation();
            }
            catch (ObjectDisposedException)
            {
                logger?.LogDebug("[SAFE-STREAM] Stream was disposed during operation");
                return defaultValue;
            }
            catch (InvalidOperationException ex)
            {
                logger?.LogWarning($"[SAFE-STREAM] Invalid stream operation: {ex.Message}");
                return defaultValue;
            }
            catch (IOException ex)
            {
                logger?.LogWarning($"[SAFE-STREAM] IO error: {ex.Message}");
                return defaultValue;
            }
            catch (Exception ex)
            {
                logger?.LogError($"[SAFE-STREAM] Stream operation failed: {ex.Message}");
                return defaultValue;
            }
        }
    }
}