using FastApi_NetCore.Core.Extensions;
using System;
using System.Net;

namespace FastApi_NetCore.Core.Helpers
{
    /// <summary>
    /// Centralized route parameter parsing and validation
    /// Eliminates repetitive parsing code across handlers
    /// </summary>
    public static class RouteParameterHelper
    {
        /// <summary>
        /// Generic parameter parser with validation
        /// Replaces manual parsing in every handler
        /// </summary>
        public static T GetParameter<T>(this HttpListenerContext context, string key, 
            T defaultValue = default, Func<string, (bool success, T value)> customParser = null)
        {
            var paramValue = context.GetRouteParameter(key);
            if (string.IsNullOrEmpty(paramValue)) 
                return defaultValue;

            if (customParser != null)
            {
                var (success, value) = customParser(paramValue);
                return success ? value : defaultValue;
            }

            // Built-in parsers for common types
            return typeof(T) switch
            {
                Type t when t == typeof(int) => 
                    int.TryParse(paramValue, out var intVal) ? (T)(object)intVal : defaultValue,
                Type t when t == typeof(long) => 
                    long.TryParse(paramValue, out var longVal) ? (T)(object)longVal : defaultValue,
                Type t when t == typeof(double) => 
                    double.TryParse(paramValue, out var doubleVal) ? (T)(object)doubleVal : defaultValue,
                Type t when t == typeof(bool) => 
                    bool.TryParse(paramValue, out var boolVal) ? (T)(object)boolVal : defaultValue,
                Type t when t == typeof(string) => (T)(object)paramValue,
                Type t when t == typeof(Guid) => 
                    Guid.TryParse(paramValue, out var guidVal) ? (T)(object)guidVal : defaultValue,
                _ => defaultValue
            };
        }

        /// <summary>
        /// Specialized int parser with range validation
        /// Perfect for delay seconds, status codes, etc.
        /// </summary>
        public static int GetIntParameter(this HttpListenerContext context, string key, 
            int defaultValue, int minValue = int.MinValue, int maxValue = int.MaxValue)
        {
            var value = context.GetParameter<int>(key, defaultValue);
            
            if (value < minValue || value > maxValue)
                return defaultValue;
                
            return value;
        }

        /// <summary>
        /// Specialized string parser with validation
        /// </summary>
        public static string GetStringParameter(this HttpListenerContext context, string key, 
            string defaultValue = "", bool allowEmpty = true, int maxLength = int.MaxValue)
        {
            var value = context.GetParameter<string>(key, defaultValue);
            
            if (!allowEmpty && string.IsNullOrWhiteSpace(value))
                return defaultValue;
                
            if (value.Length > maxLength)
                return defaultValue;
                
            return value;
        }

    }
}