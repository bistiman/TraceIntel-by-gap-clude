// Services/ParallelTraceEngine.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TraceIntel.Core.Models;

namespace TraceIntel.Core.Services
{
    public class ParallelTraceEngine
    {
        public int MaxHops { get; private set; }
        private readonly SemaphoreSlim _semaphore = new(10); // Max 10 concurrent traces
        private CancellationTokenSource? _cts;

        public ParallelTraceEngine(int maxHops = 30)
        {
            MaxHops = maxHops;
        }

        public async Task StartTraceAsync(
            List<string> domains,
            Action<string, List<HopNode>> onDomainCompleted,
            Action<string, HopNode> onHopReceived,
            CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var tasks = domains.Select(domain => Task.Run(async () =>
            {
                await _semaphore.WaitAsync(_cts.Token);
                try
                {
                    await TraceSingleDomainAsync(domain, onDomainCompleted, onHopReceived, _cts.Token);
                }
                finally
                {
                    _semaphore.Release();
                }
            }, _cts.Token));

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation bubbling from per-domain tasks.
            }
        }

        private async Task TraceSingleDomainAsync(
            string domain,
            Action<string, List<HopNode>> onDomainCompleted,
            Action<string, HopNode> onHopReceived,
            CancellationToken cancellationToken)
        {
            var hops = new List<HopNode>();
            Process? process = null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "tracert",
                    Arguments = $"-d -h {MaxHops} -w 1000 {domain}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process = new Process { StartInfo = psi };
                process.Start();

                var hopRegex = new Regex(@"^\s*(\d+)\s+(.*)$", RegexOptions.Compiled);
                var targetHeaderRegex = new Regex(@"Tracing route to\s+(.*?)\s+\[(\d{1,3}(?:\.\d{1,3}){3})\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                var emittedTarget = false;
                var lastProgressAt = DateTime.UtcNow;
                var stallTimeout = TimeSpan.FromSeconds(8);

                while (!process.StandardOutput.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    // Avoid "stuck" perception: break if tracert stops producing output for too long.
                    var readTask = process.StandardOutput.ReadLineAsync();
                    var completed = await Task.WhenAny(readTask, Task.Delay(1000, cancellationToken));
                    if (completed != readTask)
                    {
                        if (DateTime.UtcNow - lastProgressAt > stallTimeout)
                        {
                            break;
                        }
                        continue;
                    }

                    var line = await readTask;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (!emittedTarget)
                    {
                        // Prefer the IP that tracert itself resolves to (more accurate than DNS in some cases)
                        var targetMatch = targetHeaderRegex.Match(line);
                        if (targetMatch.Success)
                        {
                            emittedTarget = true;
                            var resolvedHost = targetMatch.Groups[1].Value?.Trim();
                            var ip = targetMatch.Groups[2].Value;
                            onHopReceived?.Invoke(domain, new HopNode { HopNumber = 0, IP = $"{resolvedHost}|{ip}", Domains = new List<string> { domain } });
                            lastProgressAt = DateTime.UtcNow;
                        }
                        else if (IPAddress.TryParse(domain, out _))
                        {
                            // If user passed an IP directly, tracert may not print it in brackets.
                            emittedTarget = true;
                            onHopReceived?.Invoke(domain, new HopNode { HopNumber = 0, IP = $"|{domain}", Domains = new List<string> { domain } });
                            lastProgressAt = DateTime.UtcNow;
                        }
                    }

                    var match = hopRegex.Match(line);
                    if (match.Success)
                    {
                        var hopNumber = int.Parse(match.Groups[1].Value);
                        var rest = match.Groups[2].Value;

                        // Extract IP (or timeout)
                        var ipMatch = Regex.Match(rest, @"(\d{1,3}(?:\.\d{1,3}){3})");
                        var isTimeout = rest.Contains("*");
                        var ip = isTimeout ? null : (ipMatch.Success ? ipMatch.Groups[1].Value : null);

                        // Extract latency values: " <1 ms  10 ms  * " -> take min numeric
                        int? latencyMs = null;
                        var msMatches = Regex.Matches(rest, @"(<\d+|\d+)\s*ms", RegexOptions.IgnoreCase);
                        foreach (Match m in msMatches)
                        {
                            var raw = m.Groups[1].Value;
                            if (raw.StartsWith("<"))
                            {
                                latencyMs = latencyMs is null ? 1 : Math.Min(latencyMs.Value, 1);
                                continue;
                            }
                            if (int.TryParse(raw, out var v))
                            {
                                latencyMs = latencyMs is null ? v : Math.Min(latencyMs.Value, v);
                            }
                        }

                        var hop = new HopNode
                        {
                            HopNumber = hopNumber,
                            IP = ip ?? "*",
                            LatencyMs = latencyMs,
                            Domains = new List<string> { domain }
                        };

                        hops.Add(hop);
                        onHopReceived?.Invoke(domain, hop);
                        lastProgressAt = DateTime.UtcNow;
                    }
                }

                await process.WaitForExitAsync(cancellationToken);
                onDomainCompleted?.Invoke(domain, hops);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                onDomainCompleted?.Invoke(domain, hops);
            }
            finally
            {
                // Ensure tracert doesn't keep running after Stop/Cancel
                try
                {
                    if (process != null && !process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch { }
                finally
                {
                    process?.Dispose();
                }
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
        }
    }
}
