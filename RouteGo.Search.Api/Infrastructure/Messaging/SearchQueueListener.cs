using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace RouteGo.Search.Api.Infrastructure.Messaging;

/// <summary>
/// Reads the search queue for as long as the process runs and hands each Fleet or Seat Inventory
/// event to its trip-listing slice, one message at a time. A message whose event is applied (or is
/// a harmless repeat) is deleted; one that must be tried again is left, so the queue delivers it
/// again after its visibility timeout and, once its receive limit is reached, parks it on the
/// dead-letter queue; one that can never be applied is moved to the dead-letter queue at once.
/// </summary>
public sealed class SearchQueueListener : BackgroundService
{
    private const int MaxMessagesPerReceive = 10;
    private const int LongPollSeconds = 20;
    private const string ReceiveCountAttribute = "ApproximateReceiveCount";
    private const string RedrivePolicyAttribute = "RedrivePolicy";

    // How long to wait before trying the queue again after it could not be reached, so an outage
    // is retried steadily instead of in a tight loop.
    private static readonly TimeSpan _queueUnreachableDelay = TimeSpan.FromSeconds(10);

    private readonly IAmazonSQS _sqs;
    private readonly string _queueUrl;
    private readonly string _deadLetterQueueUrl;
    private readonly TripListingEventDispatcher _dispatcher;
    private readonly ILogger<SearchQueueListener> _logger;

    public SearchQueueListener(
        IAmazonSQS sqs,
        IConfiguration configuration,
        TripListingEventDispatcher dispatcher,
        ILogger<SearchQueueListener> logger)
    {
        _sqs = sqs;
        _queueUrl = configuration[ConfigKeys.SearchQueueUrl]!;
        _deadLetterQueueUrl = configuration[ConfigKeys.SearchDeadLetterQueueUrl]!;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int? maxReceiveCount = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                maxReceiveCount ??= await ReadMaxReceiveCountAsync(stoppingToken);
                await ReceiveAndHandleAsync(maxReceiveCount.Value, stoppingToken);
            }
            catch (AmazonSQSException exception)
            {
                _logger.LogError(exception, "Search queue {QueueUrl} could not be read; retrying", _queueUrl);
                await Task.Delay(_queueUnreachableDelay, stoppingToken);
            }
            catch (AmazonClientException exception)
            {
                _logger.LogError(exception, "Search queue {QueueUrl} could not be reached; retrying", _queueUrl);
                await Task.Delay(_queueUnreachableDelay, stoppingToken);
            }
            catch (HttpRequestException exception)
            {
                _logger.LogError(exception, "Search queue {QueueUrl} could not be reached; retrying", _queueUrl);
                await Task.Delay(_queueUnreachableDelay, stoppingToken);
            }
        }
    }

    // The receive limit is read from the queue's own redrive policy, so the last attempt this
    // process acts on is always the one the queue enforces.
    private async Task<int> ReadMaxReceiveCountAsync(CancellationToken cancellationToken)
    {
        var request = new GetQueueAttributesRequest
        {
            QueueUrl = _queueUrl,
            AttributeNames = new List<string> { RedrivePolicyAttribute },
        };
        GetQueueAttributesResponse response = await _sqs.GetQueueAttributesAsync(request, cancellationToken);
        string? policy = null;
        if (response.Attributes != null)
        {
            response.Attributes.TryGetValue(RedrivePolicyAttribute, out policy);
        }

        int? maxReceiveCount = RedrivePolicy.ReadMaxReceiveCount(policy);
        if (maxReceiveCount is null)
        {
            throw new InvalidOperationException(
                $"Queue {_queueUrl} has no dead-letter redrive policy with a maxReceiveCount; "
                + "without it an event that keeps failing is retried forever and never parked.");
        }

        return maxReceiveCount.Value;
    }

    private async Task ReceiveAndHandleAsync(int maxReceiveCount, CancellationToken cancellationToken)
    {
        var request = new ReceiveMessageRequest
        {
            QueueUrl = _queueUrl,
            MaxNumberOfMessages = MaxMessagesPerReceive,
            WaitTimeSeconds = LongPollSeconds,
            MessageSystemAttributeNames = new List<string> { ReceiveCountAttribute },
        };
        ReceiveMessageResponse response = await _sqs.ReceiveMessageAsync(request, cancellationToken);
        if (response.Messages is null)
        {
            return;
        }

        foreach (Message message in response.Messages)
        {
            await HandleMessageAsync(message, maxReceiveCount, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(Message message, int maxReceiveCount, CancellationToken cancellationToken)
    {
        bool isLastAttempt = ReadReceiveCount(message) >= maxReceiveCount;
        EventEnvelopeReading reading = EventEnvelopeReader.Read(message.Body, isLastAttempt);
        if (reading.Event is null)
        {
            _logger.LogError(
                "Queue message {MessageId} cannot be used ({Problem}); set aside on the dead-letter queue",
                message.MessageId, reading.Problem);
            await SetAsideAsync(message, cancellationToken);
            return;
        }

        EventHandlingOutcome? outcome = await DispatchSafelyAsync(message, reading.Event, cancellationToken);
        if (outcome == EventHandlingOutcome.Consumed)
        {
            await _sqs.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, cancellationToken);
            return;
        }

        if (outcome is null || outcome == EventHandlingOutcome.SetAside)
        {
            await SetAsideAsync(message, cancellationToken);
        }
    }

    // A failure inside one event (the database briefly down, for instance) must not stop the
    // listener: the message stays on the queue and is delivered again.
    private async Task<EventHandlingOutcome?> DispatchSafelyAsync(
        Message message, ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        try
        {
            EventHandlingOutcome? outcome = await _dispatcher.DispatchAsync(receivedEvent, cancellationToken);
            if (outcome is null)
            {
                _logger.LogError(
                    "Queue message {MessageId} carries event type {EventType}, which Search does not apply; set aside on the dead-letter queue",
                    message.MessageId, receivedEvent.EventType);
            }

            return outcome;
        }
        catch (DbException exception)
        {
            ReportTemporaryFailure(receivedEvent, exception);
            return EventHandlingOutcome.RetryLater;
        }
    }

    private void ReportTemporaryFailure(ReceivedEvent receivedEvent, DbException exception)
    {
        if (!receivedEvent.IsLastAttempt)
        {
            _logger.LogWarning(
                exception, "{EventType} event {EventId} (correlation {CorrelationId}) could not be applied yet; it will be delivered again",
                receivedEvent.EventType, receivedEvent.EventId, receivedEvent.CorrelationId);
            return;
        }

        ProblemType problemType = TripListingEventDispatcher.NotAppliedProblemFor(receivedEvent.EventType);
        EventAlerts.LogNotApplied(
            _logger, problemType, receivedEvent, NotAppliedReason.RepeatedFailure, exception.Message);
    }

    // The message goes to the dead-letter queue unchanged, so the platform engineer can replay it
    // onto the search queue after the fix.
    private async Task SetAsideAsync(Message message, CancellationToken cancellationToken)
    {
        await _sqs.SendMessageAsync(_deadLetterQueueUrl, message.Body, cancellationToken);
        await _sqs.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, cancellationToken);
    }

    private static int ReadReceiveCount(Message message)
    {
        string? text = null;
        if (message.Attributes != null)
        {
            message.Attributes.TryGetValue(ReceiveCountAttribute, out text);
        }

        bool isNumber = int.TryParse(text, out int receiveCount);
        if (!isNumber)
        {
            return 1;
        }

        return receiveCount;
    }
}
