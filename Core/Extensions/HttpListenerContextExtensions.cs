using FastApi_NetCore.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Extensions
{
    public static class HttpListenerContextExtensions
    {
        private static readonly ConditionalWeakTable<HttpListenerContext, Dictionary<string, object>> ContextItems =
            new ConditionalWeakTable<HttpListenerContext, Dictionary<string, object>>();

        public static Dictionary<string, object> GetItems(this HttpListenerContext context)
        {
            if (!ContextItems.TryGetValue(context, out var items))
            {
                items = new Dictionary<string, object>();
                ContextItems.Add(context, items);
            }
            return items;
        }

        public static void SetUserPrincipal(this HttpListenerContext context, ClaimsPrincipal principal)
        {
            context.GetItems()["UserPrincipal"] = principal;
        }

        public static ClaimsPrincipal GetUserPrincipal(this HttpListenerContext context)
        {
            return context.GetItems().TryGetValue("UserPrincipal", out var principal)
                ? principal as ClaimsPrincipal
                : null;
        }

        public static T GetFeature<T>(this HttpListenerContext context) where T : class
        {
            return context.GetItems().TryGetValue(typeof(T).FullName, out var feature)
                ? feature as T
                : null;
        }

        public static bool HasFeature<T>(this HttpListenerContext context) where T : class
        {
            return context.GetItems().ContainsKey(typeof(T).FullName);
        }

        public static void SetFeature<T>(this HttpListenerContext context, T feature) where T : class
        {
            context.GetItems()[typeof(T).FullName] = feature;
        }

        public static IServiceProvider GetServiceProvider(this HttpListenerContext context)
        {
            return context.GetItems().TryGetValue("ServiceProvider", out var provider)
                ? provider as IServiceProvider
                : null;
        }

        public static void SetServiceProvider(this HttpListenerContext context, IServiceProvider provider)
        {
            context.GetItems()["ServiceProvider"] = provider;
        }

        public static T GetService<T>(this HttpListenerContext context) where T : class
        {
            return context.GetServiceProvider()?.GetService<T>();
        }

        public static async Task<T> BindToModelAsync<T>(this HttpListenerContext context) where T : new()
        {
            var model = new T();
            var properties = typeof(T).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            // Bind from query string
            foreach (var property in properties)
            {
                var value = context.Request.QueryString[property.Name];
                if (!string.IsNullOrEmpty(value))
                {
                    SetPropertyValue(model, property, value);
                }
            }

            // Bind from JSON body (if present)
            if (context.Request.HasEntityBody &&
                context.Request.ContentType?.StartsWith("application/json") == true)
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                string body = await reader.ReadToEndAsync();

                if (!string.IsNullOrEmpty(body))
                {
                    var jsonDocument = JsonDocument.Parse(body);
                    var root = jsonDocument.RootElement;

                    foreach (var property in properties)
                    {
                        if (root.TryGetProperty(property.Name, out JsonElement value))
                        {
                            SetPropertyValueFromJson(model, property, value);
                        }
                    }
                }
            }

            return model;
        }

        private static void SetPropertyValue(object model, System.Reflection.PropertyInfo property, string value)
        {
            try
            {
                var convertedValue = Convert.ChangeType(value, property.PropertyType);
                property.SetValue(model, convertedValue);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Error al convertir el valor para {property.Name}: {ex.Message}");
            }
        }

        private static void SetPropertyValueFromJson(object model, System.Reflection.PropertyInfo property, JsonElement value)
        {
            try
            {
                object convertedValue;

                if (property.PropertyType == typeof(string))
                    convertedValue = value.GetString();
                else if (property.PropertyType == typeof(int))
                    convertedValue = value.GetInt32();
                else if (property.PropertyType == typeof(bool))
                    convertedValue = value.GetBoolean();
                else if (property.PropertyType == typeof(decimal))
                    convertedValue = value.GetDecimal();
                else if (property.PropertyType == typeof(DateTime))
                    convertedValue = value.GetDateTime();
                else
                    convertedValue = JsonSerializer.Deserialize(value.GetRawText(), property.PropertyType);

                property.SetValue(model, convertedValue);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Error al convertir el valor JSON para {property.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Establece un parámetro de ruta en el contexto
        /// </summary>
        public static void SetRouteParameter(this HttpListenerContext context, string key, string value)
        {
            if (!context.HasFeature<Dictionary<string, string>>())
            {
                context.SetFeature(new Dictionary<string, string>());
            }
            
            var parameters = context.GetFeature<Dictionary<string, string>>();
            parameters![key] = value;
        }

        /// <summary>
        /// Obtiene un parámetro de ruta del contexto
        /// </summary>
        public static string? GetRouteParameter(this HttpListenerContext context, string key)
        {
            var parameters = context.GetFeature<Dictionary<string, string>>();
            return parameters?.TryGetValue(key, out var value) == true ? value : null;
        }

        /// <summary>
        /// Obtiene todos los parámetros de ruta del contexto
        /// </summary>
        public static Dictionary<string, string> GetRouteParameters(this HttpListenerContext context)
        {
            return context.GetFeature<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Deserializa el cuerpo de la request a un objeto del tipo especificado
        /// </summary>
        public static async Task<T?> GetModelAsync<T>(this HttpListenerContext context) where T : class, new()
        {
            try
            {
                if (context.Request.HasEntityBody && 
                    context.Request.ContentType?.StartsWith("application/json") == true)
                {
                    using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                    string body = await reader.ReadToEndAsync();
                    
                    if (!string.IsNullOrEmpty(body))
                    {
                        return JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Métodos de extensión para IHttpResponseHandler
    /// </summary>
    public static class HttpResponseHandlerExtensions
    {
        /// <summary>
        /// Envía una respuesta de error HTTP
        /// </summary>
        public static async Task SendErrorAsync(this IHttpResponseHandler handler, HttpListenerContext context, string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        {
            context.Response.StatusCode = (int)statusCode;
            
            var errorResponse = new 
            { 
                error = true,
                message = message,
                statusCode = (int)statusCode,
                timestamp = DateTime.UtcNow
            };
            
            await handler.SendAsync(context, errorResponse, true);
        }
    }
}
