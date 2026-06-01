using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TraceIntel.Core.Services
{
    public class WhoisService
    {
        public async Task<string> LookupAsync(string query, CancellationToken ct)
        {
            query = query?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
                return "Invalid domain or IP address.";

            try
            {
                // Determine if it is an IP address
                bool isIp = System.Net.IPAddress.TryParse(query, out _);

                if (isIp)
                {
                    // Start with ARIN for IP lookup
                    string arinResponse = await QueryWhoisServerAsync("whois.arin.net", query, ct);
                    
                    // Check if referred to another RIR (RIPE, APNIC, LACNIC, AFRINIC)
                    if (arinResponse.Contains("whois.ripe.net", StringComparison.OrdinalIgnoreCase))
                        return await QueryWhoisServerAsync("whois.ripe.net", query, ct);
                    if (arinResponse.Contains("whois.apnic.net", StringComparison.OrdinalIgnoreCase))
                        return await QueryWhoisServerAsync("whois.apnic.net", query, ct);
                    if (arinResponse.Contains("whois.lacnic.net", StringComparison.OrdinalIgnoreCase))
                        return await QueryWhoisServerAsync("whois.lacnic.net", query, ct);
                    if (arinResponse.Contains("whois.afrinic.net", StringComparison.OrdinalIgnoreCase))
                        return await QueryWhoisServerAsync("whois.afrinic.net", query, ct);

                    return arinResponse;
                }
                else
                {
                    // Start with IANA for domain lookup
                    string ianaResponse = await QueryWhoisServerAsync("whois.iana.org", query, ct);
                    string referralServer = ExtractReferralServer(ianaResponse);

                    if (string.IsNullOrWhiteSpace(referralServer))
                    {
                        // Fallback: try common WHOIS servers if no referral
                        if (query.EndsWith(".com", StringComparison.OrdinalIgnoreCase) || query.EndsWith(".net", StringComparison.OrdinalIgnoreCase))
                            referralServer = "whois.verisign-grs.com";
                        else if (query.EndsWith(".org", StringComparison.OrdinalIgnoreCase))
                            referralServer = "whois.pir.org";
                        else
                            return ianaResponse;
                    }

                    // Query the referral server
                    string detailedResponse = await QueryWhoisServerAsync(referralServer, query, ct);
                    return detailedResponse;
                }
            }
            catch (Exception ex)
            {
                return $"WHOIS Lookup Failed: {ex.Message}";
            }
        }

        private async Task<string> QueryWhoisServerAsync(string server, string query, CancellationToken ct)
        {
            try
            {
                using var client = new TcpClient();
                
                // Set short connection timeout
                var connectTask = client.ConnectAsync(server, 43);
                var completedTask = await Task.WhenAny(connectTask, Task.Delay(5000, ct));
                
                if (completedTask != connectTask)
                {
                    throw new TimeoutException($"Connection to WHOIS server '{server}' timed out.");
                }

                ct.ThrowIfCancellationRequested();

                using var stream = client.GetStream();
                
                // Some WHOIS servers (like ARIN) need specific query flags for clean output
                string formattedQuery = query;
                if (server.Equals("whois.arin.net", StringComparison.OrdinalIgnoreCase))
                {
                    formattedQuery = "n + " + query; // simple query formatting for ARIN
                }

                byte[] queryBytes = Encoding.UTF8.GetBytes(formattedQuery + "\r\n");
                await stream.WriteAsync(queryBytes, 0, queryBytes.Length, ct);

                using var reader = new StreamReader(stream, Encoding.UTF8);
                return await reader.ReadToEndAsync(ct);
            }
            catch (Exception ex)
            {
                return $"[Error querying {server}]: {ex.Message}";
            }
        }

        private string ExtractReferralServer(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return string.Empty;

            var lines = response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("whois:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split(new[] { ':' }, 2);
                    if (parts.Length > 1)
                    {
                        return parts[1].Trim();
                    }
                }
                else if (trimmed.StartsWith("refer:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split(new[] { ':' }, 2);
                    if (parts.Length > 1)
                    {
                        return parts[1].Trim();
                    }
                }
            }

            return string.Empty;
        }
    }
}
