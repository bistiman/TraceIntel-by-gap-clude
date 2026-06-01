// Models/HopNode.cs
using System.Collections.Generic;

namespace TraceIntel.Core.Models
{
    public class HopNode
    {
        public int HopNumber { get; set; }
        public string? IP { get; set; }
        public int? LatencyMs { get; set; }
        public List<string> Domains { get; set; } = new();
        public bool IsTimeout => string.IsNullOrEmpty(IP) || IP == "*";
        public bool IsDestination { get; set; } // ✅ اضافه شد
    }
}
