using System.Diagnostics.Metrics;

namespace RabbitEars.Api;

/// <summary>Low-cardinality rejection counts; no addresses, room ids or filenames are recorded.</summary>
internal static class DemoMetrics
{
    public const string Name = "RabbitEars.Protection";
    private static readonly Meter Meter = new(Name);
    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>("rabbitears.rejections");
    public static void Reject(string reason) => Rejections.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
