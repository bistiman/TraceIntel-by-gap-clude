// Services/RouteGroupingService.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TraceIntel.Core.Models;

namespace TraceIntel.Core.Services
{
    public class RouteGroupingService
    {
        public ObservableCollection<RouteGroup> GroupByRoute(
            List<DomainTrace> traces,
            int groupByHopCount = 3)
        {
            var groups = new Dictionary<string, RouteGroup>();

            foreach (var trace in traces.Where(t => t.Hops.Any()))
            {
                // Take first N hops for grouping
                var routeKey = string.Join(" → ", trace.Hops.Take(groupByHopCount));

                if (!groups.ContainsKey(routeKey))
                {
                    groups[routeKey] = new RouteGroup
                    {
                        RoutePath = routeKey,
                        Domains = new ObservableCollection<string>()
                    };
                }

                groups[routeKey].Domains.Add(trace.Domain);
                groups[routeKey].Count = groups[routeKey].Domains.Count;
            }

            return new ObservableCollection<RouteGroup>(
                groups.Values.OrderByDescending(g => g.Count));
        }
    }
}
