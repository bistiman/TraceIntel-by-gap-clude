using System.Diagnostics;
using System.Text.RegularExpressions;
using TraceIntel.Core.Models;

namespace TraceIntel.Core.Services
{
    public class TraceEngine
    {
        public async Task<TraceResult> TraceAsync(string target, int maxHops, CancellationToken ct)
        {
            var result = new TraceResult { Domain = target };

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "tracert",
                    Arguments = $"-h {maxHops} -w 2000 {target}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return result;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync(ct);

                // Extract target IP
                var targetMatch = Regex.Match(output, @"\[(\d+\.\d+\.\d+\.\d+)\]");
                if (targetMatch.Success)
                    result.TargetIP = targetMatch.Groups[1].Value;

                // Parse hops
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"^\s*(\d+)\s+(.+)");
                    if (!match.Success) continue;

                    int hopNum = int.Parse(match.Groups[1].Value);
                    string rest = match.Groups[2].Value;

                    var ipMatch = Regex.Match(rest, @"(\d+\.\d+\.\d+\.\d+)");

                    result.Hops.Add(new HopNode
                    {
                        HopNumber = hopNum,
                        IP = ipMatch.Success ? ipMatch.Value : "*"
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch { }

            return result;
        }
    }
}
