using System.Net;

namespace FastApi_NetCore
{
    public interface IHttpResponseHandler { Task SendAsync(HttpListenerContext context, object data, bool closeConnection); }
}