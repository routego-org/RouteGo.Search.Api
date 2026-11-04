#!/bin/bash
# LocalStack init (LOCAL ONLY): runs inside the LocalStack container when it is ready.
# Creates the two producer topics (if this LocalStack does not have them yet), the search queue with
# its dead-letter queue, and the subscriptions that deliver Fleet's and Seat Inventory's events to it.
# Every command is idempotent, so it is safe against a LocalStack other services already set up.
# Real AWS queues and subscriptions are created once per account by infra, not by this script.
set -euo pipefail

REGION="ap-south-1"
MAX_RECEIVE_COUNT=5   # after 5 attempts a message is parked on search-dlq

# The producers' topics: Fleet's "fleet events stream" and Seat Inventory's "inventory events stream".
fleet_topic=$(awslocal sns create-topic --region "$REGION" --name routego-fleet-events --query TopicArn --output text)
inventory_topic=$(awslocal sns create-topic --region "$REGION" --name routego-inventory-events --query TopicArn --output text)

awslocal sqs create-queue --region "$REGION" --queue-name search-dlq >/dev/null
dlq_url=$(awslocal sqs get-queue-url --region "$REGION" --queue-name search-dlq --query QueueUrl --output text)
dlq_arn=$(awslocal sqs get-queue-attributes --region "$REGION" --queue-url "$dlq_url" \
  --attribute-names QueueArn --query Attributes.QueueArn --output text)

awslocal sqs create-queue --region "$REGION" --queue-name search-q \
  --attributes "{\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"${dlq_arn}\\\",\\\"maxReceiveCount\\\":\\\"${MAX_RECEIVE_COUNT}\\\"}\"}" \
  >/dev/null
queue_url=$(awslocal sqs get-queue-url --region "$REGION" --queue-name search-q --query QueueUrl --output text)
queue_arn=$(awslocal sqs get-queue-attributes --region "$REGION" --queue-url "$queue_url" \
  --attribute-names QueueArn --query Attributes.QueueArn --output text)

# Raw delivery: the queue message body is the RouteGo envelope itself, not an SNS wrapper.
# The filter keeps out every event type Search does not apply.
subscribe() {
  local topic_arn="$1"
  local filter="$2"
  awslocal sns subscribe --region "$REGION" --topic-arn "$topic_arn" --protocol sqs \
    --notification-endpoint "$queue_arn" \
    --attributes "{\"RawMessageDelivery\":\"true\",\"FilterPolicy\":\"${filter}\"}" >/dev/null
}

subscribe "$fleet_topic" '{\"eventType\":[\"TripScheduled\",\"FareChanged\",\"TripCancelled\"]}'
subscribe "$inventory_topic" '{\"eventType\":[\"SeatHeld\",\"SeatBooked\",\"SeatReleased\",\"SeatBlocked\",\"SeatUnblocked\",\"SeatChanged\"]}'

echo "created search-q (DLQ: search-dlq), subscribed to routego-fleet-events and routego-inventory-events"
