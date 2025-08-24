using FastApi_NetCore.Configuration;
using FastApi_NetCore.Interfaces;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FastApi_NetCore.Services
{
    public class RateLimitService : IRateLimitService
    {
        private readonly RateLimitConfig _config;
        private readonly ConcurrentDictionary<string, ClientRateLimitInfo> _clientRequests = new();

        public RateLimitService(IOptions<RateLimitConfig> config)
        {
            _config = config.Value;
        }

        public bool IsRequestAllowed(string clientId, string endpoint)
        {
            var rule = GetRateLimitRule(clientId, endpoint);
            var now = DateTime.UtcNow;

            if (!_clientRequests.TryGetValue(clientId, out var clientInfo))
            {
                clientInfo = new ClientRateLimitInfo();
                _clientRequests[clientId] = clientInfo;
            }

            // Clean up old entries
            clientInfo.RequestTimestamps.RemoveAll(timestamp => now - timestamp > rule.TimeWindow);

            if (clientInfo.RequestTimestamps.Count >= rule.RequestLimit)
            {
                return false;
            }

            clientInfo.RequestTimestamps.Add(now);
            return true;
        }

        public int GetRetryAfter(string clientId, string endpoint)
        {
            var rule = GetRateLimitRule(clientId, endpoint);
            var now = DateTime.UtcNow;

            if (_clientRequests.TryGetValue(clientId, out var clientInfo) && clientInfo.RequestTimestamps.Count > 0)
            {
                var oldestTimestamp = clientInfo.RequestTimestamps[0];
                var timePassed = now - oldestTimestamp;
                return (int)(rule.TimeWindow - timePassed).TotalSeconds;
            }

            return (int)rule.TimeWindow.TotalSeconds;
        }

        private RateLimitRule GetRateLimitRule(string clientId, string endpoint)
        {
            // Check for client-specific rules
            if (_config.ClientSpecificRules.TryGetValue(clientId, out var clientRule))
            {
                return clientRule;
            }

            // Check for endpoint-specific rules
            if (_config.EndpointSpecificRules.TryGetValue(endpoint, out var endpointRule))
            {
                return endpointRule;
            }

            // Return default rule
            return new RateLimitRule
            {
                RequestLimit = _config.DefaultRequestLimit,
                TimeWindow = _config.DefaultTimeWindow
            };
        }

        private class ClientRateLimitInfo
        {
            public List<DateTime> RequestTimestamps { get; set; } = new List<DateTime>();
        }
    }
}