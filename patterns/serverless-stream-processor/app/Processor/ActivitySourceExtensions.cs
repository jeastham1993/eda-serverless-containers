using System.Diagnostics;

namespace Processor;

public static class ActivitySourceExtensions
{
    internal static Activity? StartActivityWithLink(this ActivitySource source, Metadata messageMetadata)
    {
            var linkedContext = new ActivityContext(
            ActivityTraceId.CreateFromString(messageMetadata.TraceId),
            ActivitySpanId.CreateFromString(messageMetadata.SpanId),
            ActivityTraceFlags.None);

        return source.StartActivity("Consume Message",
            ActivityKind.Consumer, default(ActivityContext),
            links: new ActivityLink[1]{new ActivityLink(linkedContext)});
    }
}