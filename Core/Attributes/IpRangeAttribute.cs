using FastApi_NetCore.Core.Utils;
using FastApi_NetCore.Core.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class IpRangeAttribute : Attribute
    {
        public string[] AllowedRanges { get; }
        public string[] GlobalTags { get; }
        public string[] IndividualTags { get; }

        public IpRangeAttribute(string[] allowedRanges, string[]? globalTags = null, string[]? individualTags = null)
        {
            AllowedRanges = allowedRanges;
            GlobalTags = globalTags ?? Array.Empty<string>();
            IndividualTags = individualTags ?? Array.Empty<string>();
        }

        public bool IsIpAllowed(IPAddress ipAddress)
        {
            var result = ValidateIpWithDetails(ipAddress);
            return result.IsAllowed;
        }
        
        public IpValidationResult ValidateIpWithDetails(IPAddress ipAddress)
        {
            var overallResult = new IpValidationResult
            {
                IsAllowed = false,
                Reason = "No matching ranges found",
                ClientIpType = ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "IPv4" : "IPv6"
            };
            
            foreach (var range in AllowedRanges)
            {
                var result = IpAddressUtils.ValidateIpWithDetails(ipAddress, range);
                if (result.IsAllowed)
                {
                    return result; // Return first match
                }
            }
            
            return overallResult;
        }

    }
}
