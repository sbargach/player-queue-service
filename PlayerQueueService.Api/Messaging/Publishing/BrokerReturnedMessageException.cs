using System.Runtime.Serialization;

namespace PlayerQueueService.Api.Messaging.Publishing;

[Serializable]
public sealed class BrokerReturnedMessageException : Exception
{
    public BrokerReturnedMessageException()
    {
    }

    public BrokerReturnedMessageException(string message)
        : base(message)
    {
    }

    public BrokerReturnedMessageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    private BrokerReturnedMessageException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
