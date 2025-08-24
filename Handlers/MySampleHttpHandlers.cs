
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using FastApi_NetCore.Routing;
using FastApi_NetCore.Attributes;

namespace FastApi_NetCore.EndPoints
{
    // ==================== HANDLERS DE PRUEBA ====================
    // Clase donde puedes usar tus metodos como llamadas de api
    public class MySampleHttpHandlers
    {
        [RouteConfiguration("/ping", HttpMethodType.GET)]
        public async Task Ping(HttpListenerContext context)
        {
            var buffer = Encoding.UTF8.GetBytes("pong");
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.Close();
        }


        [RouteConfiguration("/datos", HttpMethodType.POST)]
        public async Task PostDatos(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream);
            string content = await reader.ReadToEndAsync();
            var response = context.Response;
            var buffer = Encoding.UTF8.GetBytes($"Datos recibidos: {content}");
            response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }
    }
}
