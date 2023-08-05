using Amazon.Kinesis.Model;

/// <summary>
///     A custom class is required for the <see cref="Record" /> input from the Kinesis event.
/// </summary>
public class KinesisDataRecord : Record
{
    public new string Data { get; set; }

    public new decimal ApproximateArrivalTimestamp { get; set; }
}