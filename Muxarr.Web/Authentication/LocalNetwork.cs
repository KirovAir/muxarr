using System.Net;
using System.Net.Sockets;

namespace Muxarr.Web.Authentication;

public static class LocalNetwork
{
    // Matches loopback and private/LAN ranges. Fails closed on a null address so an
    // unknown origin still needs to log in.
    public static bool IsLocalAddress(IPAddress? address)
    {
        if (address == null)
        {
            return false;
        }

        // Unwrap IPv4-mapped IPv6 (e.g. ::ffff:192.168.1.10) so the checks below apply.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                10 => true, // 10.0.0.0/8
                172 => bytes[1] >= 16 && bytes[1] <= 31, // 172.16.0.0/12
                192 => bytes[1] == 168, // 192.168.0.0/16
                169 => bytes[1] == 254, // 169.254.0.0/16 link-local
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fe80::/10 link-local or fc00::/7 unique local.
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;
        }

        return false;
    }
}
