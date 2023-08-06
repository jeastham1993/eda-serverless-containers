using System.Diagnostics;

namespace Processor;

public class MessageWrapper<T>
{
    public Metadata Metadata { get; set; }
    
    public T Data { get; set; }
}

public class Metadata
{
    public Metadata()
    {
    }

    public Metadata(Activity activity)
    {
        this.TraceId = activity.TraceId.ToString();
        this.SpanId = activity.SpanId.ToString();
        this.MessageIdentifier = Guid.NewGuid().ToString();
        this.PublishDate = DateTime.Now;
    }
    
    public string TraceId { get; set; }
    
    public string SpanId { get; set; }
    
    public string MessageIdentifier { get; set; }
    
    public DateTime PublishDate { get; set; }

    public ActivityContext LoadActivityContext() => new ActivityContext(
        ActivityTraceId.CreateFromString(this.TraceId),
        ActivitySpanId.CreateFromString(SpanId),
        ActivityTraceFlags.None
    );
}