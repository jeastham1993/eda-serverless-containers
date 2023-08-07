namespace CreateCustomerHandler;

public class CustomerCreatedEvent
{
    public string CustomerId { get; set; }
    
    public string FirstName { get; set; }
}