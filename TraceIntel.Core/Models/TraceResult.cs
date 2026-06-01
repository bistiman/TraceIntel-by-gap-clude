using System.Collections.Generic;

namespace TraceIntel.Core.Models
{
    public class TraceResult
    {
        public string Domain { get; set; } = string.Empty;

        public string TargetIP { get; set; } = string.Empty;

        public List<HopNode> Hops { get; set; } = new List<HopNode>();

        public bool IsComplete { get; set; }
    }
}