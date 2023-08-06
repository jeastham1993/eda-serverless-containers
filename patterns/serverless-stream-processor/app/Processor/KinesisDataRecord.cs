using Amazon.Kinesis.Model;

/// <summary>
///     A custom class is required for the <see cref="Record" /> input from the Kinesis event.
/// </summary>
public class KinesisDataRecord<T>
{
    public string SequenceNumber { get; set; }
    
    public string EventID { get; set; }
    
    public T Data { get; set; }

    public string PartitionKey { get; set; }
}