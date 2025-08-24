//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net;
//using System.Text;
//using System.Threading.Tasks;

//namespace FastApi_NetCore
//{
//    public class HttpResponseHandler : IHttpResponseHandler
//    {
//        public async Task SendAsync(HttpListenerContext context, object data, bool closeConnection)
//        {
//            var res = context.Response;
//            try
//            {
//                var json = System.Text.Json.JsonSerializer.Serialize(data);
//                var buf = Encoding.UTF8.GetBytes(json);
//                res.ContentType = "application/json; charset=utf-8";
//                res.ContentLength64 = buf.Length;
//                await res.OutputStream.WriteAsync(buf, 0, buf.Length).ConfigureAwait(false);
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Error al enviar respuesta HTTP: {ex.Message}");
//            }
//            finally
//            {
//                try { res.OutputStream.Close(); } catch { }
//                if (closeConnection)
//                {
//                    try { res.Close(); } catch { }
//                }
//            }
//        }
//    }
//}