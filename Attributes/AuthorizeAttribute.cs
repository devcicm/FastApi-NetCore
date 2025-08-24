using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Attributes
{
    public enum AuthorizationType
    {
        None,
        JWT,
        IP
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class AuthorizeAttribute : Attribute
    {
        public AuthorizationType Type { get; set; } = AuthorizationType.JWT;
        public string Roles { get; set; } = string.Empty;
        public string Policies { get; set; } = string.Empty;
    }
}
