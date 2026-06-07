using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using TraceIntel.Core.Models;

namespace TraceIntel.Core.Services
{
    public class DnsReconService
    {
        private static readonly string[] CommonSubdomains =
        {
            "www", "mail", "remote", "vpn", "api", "dev", "stage", "blog",
            "server", "secure", "admin", "ftp", "portal", "mx", "support",
            "shop", "status", "test", "ns1", "ns2", "ns3", "ns4", "cloud",
            "app", "db", "m", "webmail", "direct", "cpanel", "autodiscover",
            "sip", "smtp", "pop", "imap", "dns", "exchange", "gw", "backup"
        };

        private static readonly string[] RecordTypeOrder =
        {
            "A", "AAAA", "CNAME", "MX", "NS", "TXT", "SOA", "SRV", "CAA", "PTR",
            "BRUTE", "AXFR", "AXFR SUCCESS", "AXFR FAIL", "AXFR ERROR"
        };

        public async Task<LookupClient> CreateLookupClientAsync(string? customDnsServer, int timeoutMs, CancellationToken ct)
        {
            var resolverIp = await ResolveCustomDnsServerAsync(customDnsServer, ct);
            if (resolverIp != null)
            {
                return new LookupClient(new LookupClientOptions(new[] { resolverIp })
                {
                    UseCache = true,
                    Timeout = TimeSpan.FromMilliseconds(timeoutMs),
                    Retries = 1
                });
            }

            return new LookupClient(new LookupClientOptions
            {
                UseCache = true,
                Timeout = TimeSpan.FromMilliseconds(timeoutMs),
                Retries = 2
            });
        }

        public async Task<List<DnsRecord>> PerformAdvancedDnsReconAsync(
            string domain,
            List<string> selectedTypes,
            string? customDnsServer,
            bool bruteForceSubdomains,
            int timeoutMs,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                return new List<DnsRecord>();
            }

            var records = new List<DnsRecord>();
            var client = await CreateLookupClientAsync(customDnsServer, timeoutMs, ct);

            try
            {
                if (IPAddress.TryParse(domain, out var ipAddress))
                {
                    records.AddRange(await PerformReverseDnsAsync(client, ipAddress, ct));
                    return FinalizeRecords(records);
                }

                var standardTypes = new List<QueryType>();
                var doAxfr = false;

                foreach (var type in selectedTypes)
                {
                    var upperType = type.ToUpperInvariant();
                    if (upperType is "AXFR" or "ZONE TRANSFER")
                    {
                        doAxfr = true;
                        continue;
                    }

                    if (Enum.TryParse<QueryType>(upperType, out var queryType) && queryType != QueryType.ANY)
                    {
                        standardTypes.Add(queryType);
                    }
                }

                if (standardTypes.Count == 0 && !doAxfr && !bruteForceSubdomains)
                {
                    records.Add(new DnsRecord
                    {
                        RecordType = "INFO",
                        Name = domain,
                        Value = "No record types selected",
                        Details = "Choose at least one record type or enable zone transfer / brute force."
                    });
                    return FinalizeRecords(records);
                }

                List<string>? nameServers = null;
                var needsNs = standardTypes.Contains(QueryType.NS) || doAxfr;
                if (needsNs)
                {
                    var nsResult = await QueryNsAsync(client, domain, ct);
                    records.AddRange(nsResult.Records);
                    nameServers = nsResult.NameServers;
                    standardTypes.Remove(QueryType.NS);
                }

                if (standardTypes.Count > 0)
                {
                    var tasks = standardTypes.Select(type => SafeQueryAsync(client, domain, type, ct));
                    var results = await Task.WhenAll(tasks);
                    foreach (var res in results)
                    {
                        records.AddRange(res);
                    }
                }

                if (doAxfr)
                {
                    records.AddRange(await AttemptZoneTransferAsync(client, domain, nameServers, ct));
                }

                if (bruteForceSubdomains)
                {
                    var existingNames = new HashSet<string>(
                        records.Select(r => r.Name),
                        StringComparer.OrdinalIgnoreCase);
                    records.AddRange(await PerformSubdomainBruteForceAsync(client, domain, existingNames, ct));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
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

            return FinalizeRecords(records);
        }

        private static async Task<IPAddress?> ResolveCustomDnsServerAsync(string? customDnsServer, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(customDnsServer))
            {
                return null;
            }

            var trimmed = customDnsServer.Trim();
            if (IPAddress.TryParse(trimmed, out var ip))
            {
                return ip;
            }

            try
            {
                var addresses = await Dns.GetHostAddressesAsync(trimmed, ct);
                return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                       ?? addresses.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private async Task<List<DnsRecord>> PerformReverseDnsAsync(LookupClient client, IPAddress ip, CancellationToken ct)
        {
            var records = new List<DnsRecord>();
            var reverseName = BuildReverseLookupName(ip);
            if (string.IsNullOrWhiteSpace(reverseName))
            {
                records.Add(new DnsRecord
                {
                    RecordType = "INFO",
                    Name = ip.ToString(),
                    Value = "Reverse lookup not supported for this address family",
                    Details = "Only IPv4 reverse lookups are supported in this build."
                });
                return records;
            }

            var response = await client.QueryAsync(reverseName, QueryType.PTR, QueryClass.IN, ct);
            if (response == null || response.HasError || !response.Answers.Any())
            {
                records.Add(new DnsRecord
                {
                    RecordType = "PTR",
                    Name = ip.ToString(),
                    Value = "No PTR record found",
                    Details = response?.ErrorMessage ?? "No reverse DNS mapping available."
                });
                return records;
            }

            foreach (var answer in response.Answers)
            {
                var record = MapRecord(answer);
                if (record != null)
                {
                    record.Name = ip.ToString();
                    records.Add(record);
                }
            }

            return records;
        }

        private static string? BuildReverseLookupName(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork)
            {
                return null;
            }

            var bytes = ip.GetAddressBytes();
            return $"{bytes[3]}.{bytes[2]}.{bytes[1]}.{bytes[0]}.in-addr.arpa";
        }

        private async Task<(List<DnsRecord> Records, List<string> NameServers)> QueryNsAsync(
            LookupClient client,
            string domain,
            CancellationToken ct)
        {
            var records = await SafeQueryAsync(client, domain, QueryType.NS, ct);
            var nameServers = records
                .Where(r => string.Equals(r.RecordType, "NS", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Value.TrimEnd('.'))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return (records, nameServers);
        }

        private async Task<List<DnsRecord>> SafeQueryAsync(LookupClient client, string domain, QueryType type, CancellationToken ct)
        {
            var resultList = new List<DnsRecord>();
            try
            {
                var response = await client.QueryAsync(domain, type, QueryClass.IN, ct);
                if (response == null)
                {
                    resultList.Add(BuildQueryStatusRecord(domain, type, "No response", "The resolver did not return a response."));
                    return resultList;
                }

                if (response.HasError)
                {
                    resultList.Add(BuildQueryStatusRecord(domain, type, "Query failed", response.ErrorMessage ?? "Resolver returned an error."));
                    return resultList;
                }

                foreach (var answer in response.Answers)
                {
                    var record = MapRecord(answer);
                    if (record != null)
                    {
                        resultList.Add(record);
                    }
                }

                if (resultList.Count == 0)
                {
                    resultList.Add(BuildQueryStatusRecord(domain, type, "No records", "The query succeeded but returned no answers."));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                resultList.Add(BuildQueryStatusRecord(domain, type, "Query error", ex.Message));
            }

            return resultList;
        }

        private static DnsRecord BuildQueryStatusRecord(string domain, QueryType type, string value, string details)
        {
            return new DnsRecord
            {
                RecordType = type.ToString(),
                Name = domain,
                Value = value,
                Details = details
            };
        }

        private async Task<List<DnsRecord>> AttemptZoneTransferAsync(
            LookupClient client,
            string domain,
            IReadOnlyList<string>? knownNameServers,
            CancellationToken ct)
        {
            var resultList = new List<DnsRecord>();
            try
            {
                var nsServers = knownNameServers?.ToList() ?? new List<string>();
                if (nsServers.Count == 0)
                {
                    var nsResult = await QueryNsAsync(client, domain, ct);
                    nsServers = nsResult.NameServers;
                }

                if (nsServers.Count == 0)
                {
                    resultList.Add(new DnsRecord
                    {
                        RecordType = "AXFR ERROR",
                        Name = domain,
                        Value = "No nameservers found",
                        Details = "Zone transfer requires at least one authoritative NS record."
                    });
                    return resultList;
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
                    catch
                    {
                        // ignore resolution failure for this NS
                    }

                    if (nsIp == null)
                    {
                        continue;
                    }

                    var axfrClient = new LookupClient(new LookupClientOptions(new[] { nsIp })
                    {
                        Timeout = TimeSpan.FromSeconds(4)
                    });

                    resultList.Add(new DnsRecord
                    {
                        RecordType = "AXFR",
                        Name = domain,
                        Value = "Attempting zone transfer",
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
                                Value = "Zone transfer succeeded",
                                Details = $"Discovered {axfrResponse.Answers.Count} records from {ns}."
                            });

                            foreach (var answer in axfrResponse.Answers)
                            {
                                var record = MapRecord(answer);
                                if (record != null)
                                {
                                    resultList.Add(record);
                                }
                            }

                            break;
                        }

                        resultList.Add(new DnsRecord
                        {
                            RecordType = "AXFR FAIL",
                            Name = domain,
                            Value = "Transfer refused",
                            Details = $"NS {ns} returned: {axfrResponse?.ErrorMessage ?? "Access denied"}"
                        });
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                resultList.Add(new DnsRecord
                {
                    RecordType = "AXFR ERROR",
                    Name = domain,
                    Value = "Zone transfer failure",
                    Details = ex.Message
                });
            }

            return resultList;
        }

        private async Task<List<DnsRecord>> PerformSubdomainBruteForceAsync(
            LookupClient client,
            string domain,
            HashSet<string> existingNames,
            CancellationToken ct)
        {
            var resultList = new ConcurrentBag<DnsRecord>();
            using var sem = new SemaphoreSlim(15);

            var tasks = CommonSubdomains.Select(async sub =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var target = $"{sub}.{domain}";
                    if (existingNames.Contains(target))
                    {
                        return;
                    }

                    var response = await client.QueryAsync(target, QueryType.A, QueryClass.IN, ct);
                    if (response == null || response.HasError || !response.Answers.Any())
                    {
                        return;
                    }

                    foreach (var answer in response.Answers)
                    {
                        if (answer is ARecord aRec)
                        {
                            resultList.Add(new DnsRecord
                            {
                                RecordType = "BRUTE",
                                Name = target,
                                Value = aRec.Address.ToString(),
                                Details = "Subdomain discovered (A record)"
                            });
                        }
                        else if (answer is CNameRecord cNameRec)
                        {
                            resultList.Add(new DnsRecord
                            {
                                RecordType = "BRUTE",
                                Name = target,
                                Value = cNameRec.CanonicalName.Value,
                                Details = "Subdomain discovered (CNAME mapping)"
                            });
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // ignore individual brute failures
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
            return resultList.ToList();
        }

        private static List<DnsRecord> FinalizeRecords(List<DnsRecord> records)
        {
            return records
                .GroupBy(r => (r.RecordType, r.Name, r.Value, r.Details))
                .Select(g => g.First())
                .OrderBy(r => Array.IndexOf(RecordTypeOrder, r.RecordType) is var idx && idx >= 0 ? idx : 99)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static DnsRecord? MapRecord(DnsResourceRecord answer)
        {
            switch (answer)
            {
                case ARecord aRecord:
                    return new DnsRecord { RecordType = "A", Name = aRecord.DomainName.Value, Value = aRecord.Address.ToString(), Details = $"TTL: {aRecord.InitialTimeToLive}s | IPv4 address mapping" };
                case AaaaRecord aaaaRecord:
                    return new DnsRecord { RecordType = "AAAA", Name = aaaaRecord.DomainName.Value, Value = aaaaRecord.Address.ToString(), Details = $"TTL: {aaaaRecord.InitialTimeToLive}s | IPv6 address mapping" };
                case MxRecord mxRecord:
                    return new DnsRecord { RecordType = "MX", Name = mxRecord.DomainName.Value, Value = mxRecord.Exchange.Value, Details = $"Priority: {mxRecord.Preference} | TTL: {mxRecord.InitialTimeToLive}s" };
                case NsRecord nsRecord:
                    return new DnsRecord { RecordType = "NS", Name = nsRecord.DomainName.Value, Value = nsRecord.NSDName.Value, Details = $"Authoritative name server | TTL: {nsRecord.InitialTimeToLive}s" };
                case CNameRecord cnameRecord:
                    return new DnsRecord { RecordType = "CNAME", Name = cnameRecord.DomainName.Value, Value = cnameRecord.CanonicalName.Value, Details = $"Alias mapping | TTL: {cnameRecord.InitialTimeToLive}s" };
                case TxtRecord txtRecord:
                    var textValue = string.Join(" ", txtRecord.Text);
                    return new DnsRecord { RecordType = "TXT", Name = txtRecord.DomainName.Value, Value = textValue, Details = $"Text record | TTL: {txtRecord.InitialTimeToLive}s" };
                case SoaRecord soaRecord:
                    return new DnsRecord { RecordType = "SOA", Name = soaRecord.DomainName.Value, Value = soaRecord.MName.Value, Details = $"Primary NS | Admin: {soaRecord.RName.Value} | Serial: {soaRecord.Serial} | Retry: {soaRecord.Retry}s | Expire: {soaRecord.Expire}s | Min TTL: {soaRecord.Minimum}s" };
                case SrvRecord srvRecord:
                    return new DnsRecord { RecordType = "SRV", Name = srvRecord.DomainName.Value, Value = $"{srvRecord.Target.Value}:{srvRecord.Port}", Details = $"Priority: {srvRecord.Priority} | Weight: {srvRecord.Weight} | TTL: {srvRecord.InitialTimeToLive}s" };
                case CaaRecord caaRecord:
                    return new DnsRecord { RecordType = "CAA", Name = caaRecord.DomainName.Value, Value = $"{caaRecord.Tag} {caaRecord.Value}", Details = $"CAA | Flags: {caaRecord.Flags}" };
                case PtrRecord ptrRecord:
                    return new DnsRecord { RecordType = "PTR", Name = ptrRecord.DomainName.Value, Value = ptrRecord.PtrDomainName.Value, Details = "Reverse DNS pointer" };
                default:
                    return new DnsRecord { RecordType = answer.RecordType.ToString(), Name = answer.DomainName.Value, Value = answer.ToString() ?? string.Empty, Details = "Raw record details" };
            }
        }
    }
}
