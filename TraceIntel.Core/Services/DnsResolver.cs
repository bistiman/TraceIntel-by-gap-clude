using System.Net;

namespace TraceIntel.Core.Services;

public static class DnsResolver
{
    public static async Task<string> ResolveHostnameAsync(string ip)
    {
        try
        {
            // اگر IP باشد resolve می‌شود
            var entry = await Dns.GetHostEntryAsync(ip);
            return entry.HostName;
        }
        catch
        {
            return "";
        }
    }
}
