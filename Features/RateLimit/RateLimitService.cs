using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Core.Utils;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace FastApi_NetCore.Features.RateLimit
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
            var nowTicks = DateTime.UtcNow.Ticks;
            var cutoffTicks = nowTicks - rule.TimeWindow.Ticks;

            var clientInfo = _clientRequests.GetOrAdd(clientId, _ => new ClientRateLimitInfo());

            // Clean up old entries using lock-free operations
            clientInfo.CleanOldTimestamps(cutoffTicks);

            if (clientInfo.GetCurrentCount() >= rule.RequestLimit)
            {
                return false;
            }

            clientInfo.AddTimestamp(nowTicks);
            return true;
        }

        public int GetRetryAfter(string clientId, string endpoint)
        {
            var rule = GetRateLimitRule(clientId, endpoint);
            var nowTicks = DateTime.UtcNow.Ticks;

            if (_clientRequests.TryGetValue(clientId, out var clientInfo) && 
                clientInfo.TryGetOldestTimestamp(out var oldestTimestampTicks))
            {
                var timePassedTicks = nowTicks - oldestTimestampTicks;
                var remainingTicks = rule.TimeWindow.Ticks - timePassedTicks;
                return (int)Math.Max(0, TimeSpan.FromTicks(remainingTicks).TotalSeconds);
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
            private readonly ConcurrentQueue<long> _requestTimestamps = new();
            private readonly LockFreeCounters.AtomicCounter _requestCount = new();

            public void AddTimestamp(long timestampTicks)
            {
                _requestTimestamps.Enqueue(timestampTicks);
                _requestCount.Increment();
            }

            public void CleanOldTimestamps(long cutoffTicks)
            {
                while (_requestTimestamps.TryPeek(out var timestamp) && timestamp < cutoffTicks)
                {
                    if (_requestTimestamps.TryDequeue(out _))
                    {
                        _requestCount.Decrement();
                    }
                }
            }

            public long GetCurrentCount() => _requestCount.Value;

            public bool TryGetOldestTimestamp(out long timestamp)
            {
                return _requestTimestamps.TryPeek(out timestamp);
            }
        }
    }
}