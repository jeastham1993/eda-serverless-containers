using System.Diagnostics;

namespace StreamPublisher;

public class MessageWrapper<T>
{
    public MessageWrapper(T data)
    {
        this.Metadata = Metadata.Create();
        this.Data = data;
    }
    public Metadata Metadata { get; set; }
    
    public T Data { get; set; }
}

public class Metadata
{
    public Metadata()
    {
    }

    public static Metadata Create()
    {
        var metadata = new Metadata();
        
        metadata.TraceId = Activity.Current.TraceId.ToString();
        metadata.SpanId = Activity.Current.SpanId.ToString();
        metadata.MessageIdentifier = Guid.NewGuid().ToString();
        metadata.PublishDate = DateTime.Now;

        return metadata;
    }
    
    public string TraceId { get; set; }
    
    public string SpanId { get; set; }
    
    public string MessageIdentifier { get; set; }
    
    public DateTime PublishDate { get; set; }
}