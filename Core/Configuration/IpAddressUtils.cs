using System.Net;

namespace FastApi_NetCore.Core.Configuration
{
    public enum IpMode
    {
        IPv4,
        IPv6,
        Mixed
    }
    
    public class IpValidationResult
    {
        public bool IsAllowed { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string ClientIpType { get; set; } = string.Empty;
        public string MatchedRange { get; set; } = string.Empty;
        public string ValidationMethod { get; set; } = string.Empty;
    }
    
    public static class IpAddressUtils
    {
        public static bool IsIpInRange(IPAddress ipAddress, string range)
        {
            var result = ValidateIpWithDetails(ipAddress, range);
            return result.IsAllowed;
        }
        
        public static IpValidationResult ValidateIpWithDetails(IPAddress ipAddress, string range)
        {
            var result = new IpValidationResult
            {
                ClientIpType = ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "IPv4" : "IPv6"
            };

            try
            {
                if (range.Contains("/"))
                {
                    // Es un rango CIDR
                    var parts = range.Split('/');
                    var networkIp = IPAddress.Parse(parts[0]);
                    var prefixLength = int.Parse(parts[1]);
                    
                    result.ValidationMethod = "CIDR Range";
                    var cidrResult = IsInCidrRangeWithDetails(ipAddress, networkIp, prefixLength);
                    result.IsAllowed = cidrResult.IsAllowed;
                    result.Reason = cidrResult.Reason;
                    result.MatchedRange = cidrResult.IsAllowed ? range : "";
                    
                    return result;
                }
                else if (range.Contains("-"))
                {
                    // Es un rango con guión (ej: 192.168.1.1-192.168.1.100)
                    var parts = range.Split('-');
                    var startIp = IPAddress.Parse(parts[0]);
                    var endIp = IPAddress.Parse(parts[1]);
                    
                    result.ValidationMethod = "IP Range";
                    var rangeResult = IsInRangeWithDetails(ipAddress, startIp, endIp);
                    result.IsAllowed = rangeResult.IsAllowed;
                    result.Reason = rangeResult.Reason;
                    result.MatchedRange = rangeResult.IsAllowed ? range : "";
                    
                    return result;
                }
                else
                {
                    // Es una IP individual
                    var singleIp = IPAddress.Parse(range);
                    result.ValidationMethod = "Single IP";
                    
                    // Direct comparison first
                    if (ipAddress.Equals(singleIp))
                    {
                        result.IsAllowed = true;
                        result.Reason = "Exact IP match";
                        result.MatchedRange = range;
                        return result;
                    }
                        
                    // Check for localhost equivalence (IPv4 ↔ IPv6)
                    if (IsLocalhostEquivalent(ipAddress, singleIp))
                    {
                        result.IsAllowed = true;
                        result.Reason = "Localhost equivalence (IPv4 ↔ IPv6)";
                        result.MatchedRange = range;
                        return result;
                    }
                    
                    result.IsAllowed = false;
                    result.Reason = $"IP {ipAddress} does not match {range}";
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.IsAllowed = false;
                result.Reason = $"Validation error: {ex.Message}";
                return result;
            }
        }

        private static IpValidationResult IsInCidrRangeWithDetails(IPAddress ipAddress, IPAddress networkIp, int prefixLength)
        {
            var result = new IpValidationResult();
            
            var ipBytes = ipAddress.GetAddressBytes();
            var networkBytes = networkIp.GetAddressBytes();

            // Handle IPv4/IPv6 localhost mapping
            if (ipBytes.Length != networkBytes.Length)
            {
                // Check for IPv4-mapped IPv6 addresses or localhost equivalents
                if (IsLocalhostEquivalent(ipAddress, networkIp))
                {
                    result.IsAllowed = true;
                    result.Reason = "Localhost equivalence across IP versions";
                    return result;
                }
                
                result.IsAllowed = false;
                result.Reason = $"IP version mismatch: Client {(ipBytes.Length == 4 ? "IPv4" : "IPv6")} vs Range {(networkBytes.Length == 4 ? "IPv4" : "IPv6")}";
                return result;
            }

            var length = prefixLength / 8;
            var remainder = prefixLength % 8;

            for (var i = 0; i < length; i++)
            {
                if (ipBytes[i] != networkBytes[i])
                {
                    result.IsAllowed = false;
                    result.Reason = $"CIDR byte mismatch at position {i}";
                    return result;
                }
            }

            if (remainder > 0)
            {
                var mask = (byte)~(255 >> remainder);
                if ((ipBytes[length] & mask) != (networkBytes[length] & mask))
                {
                    result.IsAllowed = false;
                    result.Reason = "CIDR subnet mask mismatch";
                    return result;
                }
            }

            result.IsAllowed = true;
            result.Reason = "IP matches CIDR range";
            return result;
        }

        private static bool IsInCidrRange(IPAddress ipAddress, IPAddress networkIp, int prefixLength)
        {
            var ipBytes = ipAddress.GetAddressBytes();
            var networkBytes = networkIp.GetAddressBytes();

            // Handle IPv4/IPv6 localhost mapping
            if (ipBytes.Length != networkBytes.Length)
            {
                // Check for IPv4-mapped IPv6 addresses or localhost equivalents
                if (IsLocalhostEquivalent(ipAddress, networkIp))
                    return true;
                    
                return false;
            }

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

        private static IpValidationResult IsInRangeWithDetails(IPAddress ipAddress, IPAddress startIp, IPAddress endIp)
        {
            var result = new IpValidationResult();
            
            var ipBytes = ipAddress.GetAddressBytes();
            var startBytes = startIp.GetAddressBytes();
            var endBytes = endIp.GetAddressBytes();

            if (ipBytes.Length != startBytes.Length || ipBytes.Length != endBytes.Length)
            {
                result.IsAllowed = false;
                result.Reason = "IP version mismatch in range comparison";
                return result;
            }

            for (int i = 0; i < ipBytes.Length; i++)
            {
                if (ipBytes[i] < startBytes[i] || ipBytes[i] > endBytes[i])
                {
                    result.IsAllowed = false;
                    result.Reason = $"IP {ipAddress} is outside range {startIp}-{endIp}";
                    return result;
                }
            }

            result.IsAllowed = true;
            result.Reason = "IP within specified range";
            return result;
        }

        private static bool IsInRange(IPAddress ipAddress, IPAddress startIp, IPAddress endIp)
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
        
        private static bool IsLocalhostEquivalent(IPAddress ip1, IPAddress ip2)
        {
            // Check if both addresses are localhost equivalents (IPv4 127.0.0.1 and IPv6 ::1)
            var isIp1Localhost = IPAddress.IsLoopback(ip1);
            var isIp2Localhost = IPAddress.IsLoopback(ip2);
            
            return isIp1Localhost && isIp2Localhost;
        }
    }
}