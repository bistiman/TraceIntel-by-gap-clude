namespace TraceIntel.Core.Models
{
    public class DnsRecord
    {
        public string RecordType { get; set; } = string.Empty; // A, AAAA, MX, NS, CNAME, TXT, SOA
        public string Name { get; set; } = string.Empty;       // The queried name (e.g. google.com)
        public string Value { get; set; } = string.Empty;      // The record value (e.g. IP, server host, or text)
        public string Details { get; set; } = string.Empty;    // Detailed parameters (e.g. MX preferences or full SOA blocks)
    }
}
