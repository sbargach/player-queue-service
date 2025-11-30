namespace PlayerQueueService.Api.Messaging.Publishing;

public sealed class BrokerReturnedMessageException : Exception
{
    public BrokerReturnedMessageException(string message) : base(message)
    {
    }
}
