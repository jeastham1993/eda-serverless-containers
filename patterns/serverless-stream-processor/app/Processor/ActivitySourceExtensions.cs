using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Processor;

public static class ActivitySourceExtensions
{
    internal static Activity StartActivityWithLink(this ActivitySource source, KinesisDataMessage message)
    {
        var linkedContext = new ActivityContext(
            ActivityTraceId.CreateFromString(message.TraceId),
            ActivitySpanId.CreateFromString(message.SpanId),
            ActivityTraceFlags.None);

        return source.StartActivity(ActivityKind.Internal, links: new List<ActivityLink>(1)
        {
            new(linkedContext)
        }, name: "Processing message");
    }
    internal static Activity? StartActivityWithContext(this ActivitySource source, KinesisDataMessage message)
    {
        var linkedContext = new ActivityContext(
            ActivityTraceId.CreateFromString(message.TraceId),
            ActivitySpanId.CreateFromString(message.SpanId),
            ActivityTraceFlags.None);

        return source.StartActivity(ActivityKind.Internal, parentContext: linkedContext, name: "Processing message");
    }
}