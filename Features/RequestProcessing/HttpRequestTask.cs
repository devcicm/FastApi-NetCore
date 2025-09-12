namespace FastApi_NetCore.Features.RequestProcessing
{
    /// <summary>
    /// Represents a unit of work for an incoming HTTP request.
    /// </summary>
    internal class HttpRequestTask
    {
        public System.Net.HttpListenerContext Context { get; set; } = null!;
        public System.Func<System.Net.HttpListenerContext, System.Threading.Tasks.Task> Handler { get; set; } = null!;
        public System.DateTime EnqueuedAt { get; set; }
        public string RequestId { get; set; } = "";
    }
}
