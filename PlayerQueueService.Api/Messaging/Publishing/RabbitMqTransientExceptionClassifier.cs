using System.IO;
using RabbitMQ.Client.Exceptions;

namespace PlayerQueueService.Api.Messaging.Publishing;

internal static class RabbitMqTransientExceptionClassifier
{
    public static bool IsTransient(Exception exception) =>
        exception is AlreadyClosedException
            or BrokerUnreachableException
            or OperationInterruptedException
            or RabbitMQClientException
            or IOException
            or TimeoutException;
}
