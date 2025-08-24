
using FastApi_NetCore.Attributes;
using FastApi_NetCore.Extensions;
using System.Net;
using System.Text;
using System.Text.Json;




namespace FastApi_NetCore.EndPoints
{
    public class AdvancedHttpHandlers
    {
        [RouteConfigurationAttribute("/users", HttpMethodType.POST)]
        [Authorize(Type = AuthorizationType.JWT, Roles = "Admin")]
        [IpRange("192.168.1.0/24", "10.0.0.1-10.0.0.100")]
        public async Task CreateUser(HttpListenerContext context)
        {
            try
            {
                // Desconstruir el contexto en un modelo
                var userModel = await context.BindToModelAsync<UserModel>();

                // Imprimir el objeto en consola
                PrintUserModelToConsole(userModel);

                // Lógica de negocio aquí...

                // Respuesta con formato según Accept header
                var responseHandler = context.GetService<IHttpResponseHandler>();
                await responseHandler.SendAsync(context, new { Success = true, Message = "Usuario creado" }, true);
            }
            catch (ArgumentException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(ex.Message));
                context.Response.Close();
            }
        }

        // Endpoint solo accesible desde IPs específicas
        [RouteConfigurationAttribute("/internal", HttpMethodType.GET)]
        [Authorize(Type = AuthorizationType.IP)]
        [IpRange("10.0.0.0/8", "192.168.0.0/16")]
        public async Task InternalEndpoint(HttpListenerContext context)
        {
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, new { Message = "Este es un endpoint interno" }, true);
        }

        // Endpoint con múltiples formas de autorización
        [RouteConfigurationAttribute("/secure", HttpMethodType.GET)]
        [Authorize(Type = AuthorizationType.JWT, Roles = "Admin,Manager")]
        [IpRange("172.16.0.0/12", "192.168.1.100")]
        public async Task SecureEndpoint(HttpListenerContext context)
        {
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, new { Message = "Endpoint seguro accesible por IPs específicas y roles específicos" }, true);
        }

        // Método para imprimir UserModel en consola
        private void PrintUserModelToConsole(UserModel userModel)
        {
            Console.WriteLine("=== UserModel Data ===");
            Console.WriteLine($"Name: {userModel.Name ?? "null"}");
            Console.WriteLine($"Email: {userModel.Email ?? "null"}");
            Console.WriteLine($"Age: {userModel.Age}");
            Console.WriteLine("======================");

            // Opcional: también puedes serializar a JSON para una vista más completa
            var json = JsonSerializer.Serialize(userModel, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine("JSON Representation:");
            Console.WriteLine(json);
            Console.WriteLine("======================");
        }
    }

    public class UserModel
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public int Age { get; set; }
    }
}