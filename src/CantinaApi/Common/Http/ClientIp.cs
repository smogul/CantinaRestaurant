namespace CantinaApi.Common.Http;

public static class ClientIp
{
    public const string Unknown = "unknown";

    // Reads the connection address, which forwarded headers only rewrite for a configured proxy, so the value stays bounded and trustworthy.
    public static string Get(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return Unknown;
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }
}
