using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RouteConfigurationAttribute : Attribute
    {
        public string Path { get; }
        public HttpMethodType Method { get; }

        public RouteConfigurationAttribute(string path, HttpMethodType method)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path no puede ser vacío.", nameof(path));
            Path = path;
            Method = method;
        }
    }

    public enum HttpMethodType { GET, POST, PUT, DELETE, OPTIONS }
}
