using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using TraceIntel.Core.Models;

namespace TraceIntel.Core.Services
{
    public class DnsReconService
    {
        // Common subdomains for quick DNS brute-forcing
        private static readonly string[] CommonSubdomains = new[]
        {
            "www", "mail", "remote", "vpn", "api", "dev", "stage", "blog",
            "server", "secure", "admin", "ftp", "portal", "mx", "support",
            "shop", "status", "test", "ns1", "ns2", "ns3", "ns4", "cloud",
            "app", "db", "m", "webmail", "direct", "cpanel", "autodiscover",
            "sip", "smtp", "pop", "imap", "dns", "exchange", "gw", "backup"
        };

        private LookupClient CreateLookupClient(string? customDnsServer)
        {
            if (!string.IsNullOrWhiteSpace(customDnsServer) && IPAddress.TryParse(customDnsServer.Trim(), out var ip))
            {
                return new LookupClient(new LookupClientOptions(new[] { ip })
                {
                    UseCache = true,
                    Timeout = TimeSpan.FromSeconds(3),
                    Retries = 1
                });
            }

            return new LookupClient(new LookupClientOptions
            {
                UseCache = true,
                Timeout = TimeSpan.FromSeconds(4),
                Retries = 2
            });
        }

        public async Task<List<DnsRecord>> PerformAdvancedDnsReconAsync(
            string domain,
            List<string> selectedTypes,
            string? customDnsServer,
            bool bruteForceSubdomains,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return new List<DnsRecord>();

            var records = new List<DnsRecord>();
            var client = CreateLookupClient(customDnsServer);

            try
            {
                // Parse standard selected query types
                var standardTypes = new List<QueryType>();
                bool doAxfr = false;

                foreach (var type in selectedTypes)
                {
                    var upperType = type.ToUpperInvariant();
                    if (upperType == "AXFR" || upperType == "ZONE TRANSFER")
                    {
                        doAxfr = true;
                        continue;
                    }

                    if (Enum.TryParse<QueryType>(upperType, out var queryType))
                    {
                        standardTypes.Add(queryType);
                    }
                }

                // 1. Run Standard DNS Queries in Parallel
                if (standardTypes.Count > 0)
                {
                    var tasks = standardTypes.Select(type => SafeQueryAsync(client, domain, type, ct));
                    var results = await Task.WhenAll(tasks);
                    foreach (var res in results)
                    {
                        records.AddRange(res);
                    }
                }

                // 2. Run Zone Transfer AXFR Attempt if Selected
                if (doAxfr)
                {
                    var axfrRecords = await AttemptZoneTransferAsync(client, domain, ct);
                    records.AddRange(axfrRecords);
                }

                // 3. Run Subdomain Brute-forcing if Selected
                if (bruteForceSubdomains)
                {
                    var bruteRecords = await PerformSubdomainBruteForceAsync(client, domain, ct);
                    records.AddRange(bruteRecords);
                }
            }
            catch (Exception ex)
            {
                records.Add(new DnsRecord
                {
                    RecordType = "SYSTEM ERROR",
                    Name = domain,
                    Value = "Scan encountered an exception",
                    Details = ex.Message
                });
            }

            return records;
        }

        private async Task<List<DnsRecord>> SafeQueryAsync(LookupClient client, string domain, QueryType type, CancellationToken ct)
        {
            var resultList = new List<DnsRecord>();
            try
            {
                var response = await client.QueryAsync(domain, type, QueryClass.IN, ct);
                if (response == null || response.HasError) return resultList;

                foreach (var answer in response.Answers)
                {
                    var record = MapRecord(answer);
                    if (record != null)
                    {
                        resultList.Add(record);
                    }
                }
            }
            catch (Exception)
            {
                // Graceful failure per query type
            }
            return resultList;
        }

        private async Task<List<DnsRecord>> AttemptZoneTransferAsync(LookupClient client, string domain, CancellationToken ct)
        {
            var resultList = new List<DnsRecord>();
            try
            {
                // Zone transfer usually requires querying the authoritative name server directly
                // So first query NS servers for the domain
                var nsResponse = await client.QueryAsync(domain, QueryType.NS, QueryClass.IN, ct);
                var nsServers = nsResponse?.Answers.OfType<NsRecord>().Select(r => r.NSDName.Value).ToList() ?? new List<string>();

                if (!nsServers.Any())
                {
                    nsServers.Add(domain); // fallback to domain itself
                }

                foreach (var ns in nsServers)
                {
                    ct.ThrowIfCancellationRequested();

                    IPAddress? nsIp = null;
                    try
                    {
                        var ipResponse = await client.QueryAsync(ns, QueryType.A, QueryClass.IN, ct);
                        nsIp = ipResponse?.Answers.OfType<ARecord>().FirstOrDefault()?.Address;
                    }
                    catch { /* ignore */ }

                    if (nsIp == null) continue;

                    // Query AXFR directly to this NS
                    var axfrClient = new LookupClient(new LookupClientOptions(new[] { nsIp }) { Timeout = TimeSpan.FromSeconds(4) });
                    
                    resultList.Add(new DnsRecord
                    {
                        RecordType = "AXFR",
                        Name = domain,
                        Value = $"Attempting Zone Transfer",
                        Details = $"Targeting NS: {ns} ({nsIp})"
                    });

                    try
                    {
                        var axfrResponse = await axfrClient.QueryAsync(domain, QueryType.AXFR, QueryClass.IN, ct);
                        if (axfrResponse != null && !axfrResponse.HasError && axfrResponse.Answers.Any())
                        {
                            resultList.Add(new DnsRecord
                            {
                                RecordType = "AXFR SUCCESS",
                                Name = domain,
                                Value = $"Zone Transfer Succeeded!",
                                Details = $"Discovered {axfrResponse.Answers.Count} records from {ns}!"
                            });

                            foreach (var answer in axfrResponse.Answers)
                            {
                                var record = MapRecord(answer);
                                if (record != null) resultList.Add(record);
                            }
                            break; // Stop querying other NS if one succeeds
                        }
                        else
                        {
                            resultList.Add(new DnsRecord
                            {
                                RecordType = "AXFR FAIL",
                                Name = domain,
                                Value = $"Transfer Refused / Closed",
                                Details = $"NS {ns} returned: {axfrResponse?.ErrorMessage ?? "Access Denied"}"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        resultList.Add(new DnsRecord
                        {
                            RecordType = "AXFR FAIL",
                            Name = domain,
                            Value = $"Query failed to {ns}",
                            Details = ex.Message
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                resultList.Add(new DnsRecord
                {
                    RecordType = "AXFR ERROR",
                    Name = domain,
                    Value = "Zone Transfer Failure",
                    Details = ex.Message
                });
            }
            return resultList;
        }

        private async Task<List<DnsRecord>> PerformSubdomainBruteForceAsync(LookupClient client, string domain, CancellationToken ct)
        {
            var resultList = new ConcurrentBag<DnsRecord>();
            
            // Limit concurrency during brute force
            using var sem = new SemaphoreSlim(15);
            
            var tasks = CommonSubdomains.Select(async sub =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var target = $"{sub}.{domain}";
                    var response = await client.QueryAsync(target, QueryType.A, QueryClass.IN, ct);
                    if (response != null && !response.HasError && response.Answers.Any())
                    {
                        foreach (var answer in response.Answers)
                        {
                            if (answer is ARecord aRec)
                            {
                                resultList.Add(new DnsRecord
                                {
                                    RecordType = "BRUTE",
                                    Name = target,
                                    Value = aRec.Address.ToString(),
                                    Details = $"Subdomain Discovered (A Record)"
                                });
                            }
                            else if (answer is CNameRecord cNameRec)
                            {
                                resultList.Add(new DnsRecord
                                {
                                    RecordType = "BRUTE",
                                    Name = target,
                                    Value = cNameRec.CanonicalName.Value,
                                    Details = $"Subdomain Discovered (CNAME mapping)"
                                });
                            }
                        }
                    }
                }
                catch { /* ignore individual brute failures */ }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
            return resultList.ToList();
        }

        private DnsRecord? MapRecord(DnsResourceRecord answer)
        {
            switch (answer)
            {
                case ARecord aRecord:
                    return new DnsRecord { RecordType = "A", Name = aRecord.DomainName.Value, Value = aRecord.Address.ToString(), Details = $"TTL: {aRecord.InitialTimeToLive}s | IPv4 address mapping" };
                case AaaaRecord aaaaRecord:
                    return new DnsRecord { RecordType = "AAAA", Name = aaaaRecord.DomainName.Value, Value = aaaaRecord.Address.ToString(), Details = $"TTL: {aaaaRecord.InitialTimeToLive}s | IPv6 address mapping" };
                case MxRecord mxRecord:
                    return new DnsRecord { RecordType = "MX", Name = mxRecord.DomainName.Value, Value = mxRecord.Exchange.Value, Details = $"Priority Preference: {mxRecord.Preference} | TTL: {mxRecord.InitialTimeToLive}s" };
                case NsRecord nsRecord:
                    return new DnsRecord { RecordType = "NS", Name = nsRecord.DomainName.Value, Value = nsRecord.NSDName.Value, Details = $"Authoritative Name Server | TTL: {nsRecord.InitialTimeToLive}s" };
                case CNameRecord cnameRecord:
                    return new DnsRecord { RecordType = "CNAME", Name = cnameRecord.DomainName.Value, Value = cnameRecord.CanonicalName.Value, Details = $"Alias mapping -> Canonical host | TTL: {cnameRecord.InitialTimeToLive}s" };
                case TxtRecord txtRecord:
                    var textValue = string.Join(" ", txtRecord.Text);
                    return new DnsRecord { RecordType = "TXT", Name = txtRecord.DomainName.Value, Value = textValue, Details = $"Text attributes (SPF/DKIM/DMARC metadata) | TTL: {txtRecord.InitialTimeToLive}s" };
                case SoaRecord soaRecord:
                    return new DnsRecord { RecordType = "SOA", Name = soaRecord.DomainName.Value, Value = soaRecord.MName.Value, Details = $"Primary NS | Admin: {soaRecord.RName.Value} | Serial: {soaRecord.Serial} | Retry: {soaRecord.Retry}s | Expire: {soaRecord.Expire}s | Default TTL: {soaRecord.Minimum}s" };
                case SrvRecord srvRecord:
                    return new DnsRecord { RecordType = "SRV", Name = srvRecord.DomainName.Value, Value = $"{srvRecord.Target.Value}:{srvRecord.Port}", Details = $"Service record | Priority: {srvRecord.Priority} | Weight: {srvRecord.Weight} | TTL: {srvRecord.InitialTimeToLive}s" };
                case CaaRecord caaRecord:
                    return new DnsRecord { RecordType = "CAA", Name = caaRecord.DomainName.Value, Value = $"{caaRecord.Tag} {caaRecord.Value}", Details = $"Certification Authority Authorization | Flags: {caaRecord.Flags}" };
                case PtrRecord ptrRecord:
                    return new DnsRecord { RecordType = "PTR", Name = ptrRecord.DomainName.Value, Value = ptrRecord.PtrDomainName.Value, Details = $"Reverse DNS pointer map" };
                default:
                    return new DnsRecord { RecordType = answer.RecordType.ToString(), Name = answer.DomainName.Value, Value = answer.ToString(), Details = $"Raw record details" };
            }
        }
    }
}
