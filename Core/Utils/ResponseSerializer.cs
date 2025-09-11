using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.IO;

namespace FastApi_NetCore.Core.Utils
{
    public interface IResponseSerializer
    {
        bool CanHandle(string acceptHeader);
        Task SerializeAsync(HttpListenerResponse response, object data);
    }


    public class JsonResponseSerializer : IResponseSerializer
    {
        public bool CanHandle(string acceptHeader)
        {
            return string.IsNullOrEmpty(acceptHeader) ||
                   acceptHeader.Contains("application/json") ||
                   acceptHeader.Contains("*/*");
        }

        public async Task SerializeAsync(HttpListenerResponse response, object data)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            var buf = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = buf.Length;

            if (response.OutputStream.CanWrite)
            {
                await response.OutputStream.WriteAsync(buf, 0, buf.Length);
                await response.OutputStream.FlushAsync();
            }
        }
    }

    public class XmlResponseSerializer : IResponseSerializer
    {
        private static readonly StringBuilderPool _stringBuilderPool = new StringBuilderPool(10);

        public bool CanHandle(string acceptHeader)
        {
            return acceptHeader.Contains("application/xml") ||
                   acceptHeader.Contains("text/xml");
        }

        public async Task SerializeAsync(HttpListenerResponse response, object data)
        {
            var serializer = new XmlSerializer(data.GetType());
            
            // Usar pool de StringBuilder para evitar allocaciones
            using var sbWrapper = _stringBuilderPool.Get();
            using var writer = new StringWriter(sbWrapper.Object);
            
            serializer.Serialize(writer, data);
            var xml = writer.ToString();
            var buf = Encoding.UTF8.GetBytes(xml);
            response.ContentType = "application/xml; charset=utf-8";
            response.ContentLength64 = buf.Length;

            if (response.OutputStream.CanWrite)
            {
                await response.OutputStream.WriteAsync(buf, 0, buf.Length);
                await response.OutputStream.FlushAsync();
            }
        }
    }
    public class ResponseSerializer : IHttpResponseHandler
    {
        private readonly IResponseSerializer[] _serializers;

        public ResponseSerializer()
        {
            _serializers = new IResponseSerializer[]
            {
                new JsonResponseSerializer(),
                new XmlResponseSerializer()
            };
        }

        public async Task SendAsync(HttpListenerContext context, object data, bool closeConnection)
        {
            var response = context.Response;
            var acceptHeader = context.Request.Headers["Accept"] ?? "";

            try
            {
                // Verificar si la respuesta ya está cerrada
                if (!response.OutputStream.CanWrite)
                    return;

                var serializer = _serializers.FirstOrDefault(s => s.CanHandle(acceptHeader))
                                ?? _serializers.First();

                await serializer.SerializeAsync(response, data);
                
                // Flush inmediatamente después de escribir
                if (response.OutputStream.CanWrite)
                {
                    await response.OutputStream.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error serializing response: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    if (response.OutputStream.CanWrite)
                    {
                        var errorMsg = Encoding.UTF8.GetBytes("Internal Server Error");
                        await response.OutputStream.WriteAsync(errorMsg, 0, errorMsg.Length);
                        await response.OutputStream.FlushAsync();
                    }
                }
                catch
                {
                    // Ignore secondary errors
                }
            }
            finally
            {
                try
                {
                    if (closeConnection)
                    {
                        response.OutputStream.Close();
                        response.Close();
                    }
                }
                catch
                {
                    // Ignore errors when closing
                }
            }
        }
    }
}
