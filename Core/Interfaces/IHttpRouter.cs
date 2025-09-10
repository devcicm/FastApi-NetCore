 
using System.Net;
using FastApi_NetCore.Features.Routing;
using FastApi_NetCore.Core.Attributes;

namespace FastApi_NetCore
{
    public interface IHttpRouter
    {
        void RegisterRoute(HttpMethodType method, string path, Func<HttpListenerContext, Task> handler);
        Task<bool> TryHandleAsync(string path, HttpListenerContext context);
    }
}