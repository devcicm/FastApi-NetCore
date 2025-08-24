using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace FastApi_NetCore.Services
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
            await response.OutputStream.WriteAsync(buf, 0, buf.Length);
        }
    }

    public class XmlResponseSerializer : IResponseSerializer
    {
        public bool CanHandle(string acceptHeader)
        {
            return acceptHeader.Contains("application/xml") ||
                   acceptHeader.Contains("text/xml");
        }

        public async Task SerializeAsync(HttpListenerResponse response, object data)
        {
            var serializer = new XmlSerializer(data.GetType());
            using var writer = new StringWriter();
            serializer.Serialize(writer, data);
            var xml = writer.ToString();
            var buf = Encoding.UTF8.GetBytes(xml);
            response.ContentType = "application/xml; charset=utf-8";
            response.ContentLength64 = buf.Length;
            await response.OutputStream.WriteAsync(buf, 0, buf.Length);
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
                var serializer = _serializers.FirstOrDefault(s => s.CanHandle(acceptHeader))
                                ?? _serializers.First(); // Default to first (JSON)

                await serializer.SerializeAsync(response, data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al serializar respuesta: {ex.Message}");
                response.StatusCode = 500;
            }
            finally
            {
                try { response.OutputStream.Close(); } catch { }
                if (closeConnection)
                {
                    try { response.Close(); } catch { }
                }
            }
        }
    }
}
