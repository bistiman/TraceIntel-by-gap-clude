using System.Net.NetworkInformation;

namespace TraceIntel.Core.Services;

public class PingService
{
    public async Task<PingResult> PingAsync(string hostNameOrAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            using (var ping = new Ping())
            {
                var reply = await ping.SendPingAsync(hostNameOrAddress, 5000);

                if (reply.Status == IPStatus.Success)
                {
                    return new PingResult
                    {
                        IsSuccess = true,
                        RoundtripTime = reply.RoundtripTime,
                        StatusMessage = $"Reply from {hostNameOrAddress}: bytes={reply.Buffer.Length} time={reply.RoundtripTime}ms TTL={reply.Options?.Ttl ?? 64}"
                    };
                }
                else
                {
                    return new PingResult
                    {
                        IsSuccess = false,
                        RoundtripTime = 0,
                        StatusMessage = $"Ping to {hostNameOrAddress} failed: {reply.Status}"
                    };
                }
            }
        }
        catch (PingException ex)
        {
            return new PingResult
            {
                IsSuccess = false,
                RoundtripTime = 0,
                StatusMessage = $"Ping to {hostNameOrAddress} failed: {ex.Message}"
            };
        }
        catch (ArgumentException ex)
        {
            return new PingResult
            {
                IsSuccess = false,
                RoundtripTime = 0,
                StatusMessage = $"Invalid host: {hostNameOrAddress} - {ex.Message}"
            };
        }
    }
}

public class PingResult
{
    public bool IsSuccess { get; set; }
    public long RoundtripTime { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}
