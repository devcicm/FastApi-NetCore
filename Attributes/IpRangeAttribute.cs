using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class IpRangeAttribute : Attribute
    {
        public string[] AllowedRanges { get; }

        public IpRangeAttribute(params string[] allowedRanges)
        {
            AllowedRanges = allowedRanges;
        }

        public bool IsIpAllowed(IPAddress ipAddress)
        {
            foreach (var range in AllowedRanges)
            {
                if (IsIpInRange(ipAddress, range))
                    return true;
            }
            return false;
        }

        private bool IsIpInRange(IPAddress ipAddress, string range)
        {
            try
            {
                if (range.Contains("/"))
                {
                    // Es un rango CIDR
                    var parts = range.Split('/');
                    var networkIp = IPAddress.Parse(parts[0]);
                    var prefixLength = int.Parse(parts[1]);

                    return IsInCidrRange(ipAddress, networkIp, prefixLength);
                }
                else if (range.Contains("-"))
                {
                    // Es un rango con guión (ej: 192.168.1.1-192.168.1.100)
                    var parts = range.Split('-');
                    var startIp = IPAddress.Parse(parts[0]);
                    var endIp = IPAddress.Parse(parts[1]);

                    return IsInRange(ipAddress, startIp, endIp);
                }
                else
                {
                    // Es una IP individual
                    var singleIp = IPAddress.Parse(range);
                    return ipAddress.Equals(singleIp);
                }
            }
            catch
            {
                return false;
            }
        }

        private bool IsInCidrRange(IPAddress ipAddress, IPAddress networkIp, int prefixLength)
        {
            var ipBytes = ipAddress.GetAddressBytes();
            var networkBytes = networkIp.GetAddressBytes();

            if (ipBytes.Length != networkBytes.Length)
                return false;

            var length = prefixLength / 8;
            var remainder = prefixLength % 8;

            for (var i = 0; i < length; i++)
            {
                if (ipBytes[i] != networkBytes[i])
                    return false;
            }

            if (remainder > 0)
            {
                var mask = (byte)~(255 >> remainder);
                return (ipBytes[length] & mask) == (networkBytes[length] & mask);
            }

            return true;
        }

        private bool IsInRange(IPAddress ipAddress, IPAddress startIp, IPAddress endIp)
        {
            var ipBytes = ipAddress.GetAddressBytes();
            var startBytes = startIp.GetAddressBytes();
            var endBytes = endIp.GetAddressBytes();

            if (ipBytes.Length != startBytes.Length || ipBytes.Length != endBytes.Length)
                return false;

            for (int i = 0; i < ipBytes.Length; i++)
            {
                if (ipBytes[i] < startBytes[i] || ipBytes[i] > endBytes[i])
                    return false;
            }

            return true;
        }
    }
}
