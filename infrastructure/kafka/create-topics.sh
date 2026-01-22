#!/bin/bash
# Create Kafka topics for EOD Burst system
# Run this after Kafka is healthy

KAFKA_BOOTSTRAP=${KAFKA_BOOTSTRAP:-localhost:9092}

echo "Creating Kafka topics..."

# Main trades topic - 12 partitions for parallel processing
kafka-topics --bootstrap-server $KAFKA_BOOTSTRAP \
    --create \
    --if-not-exists \
    --topic trades.raw \
    --partitions 12 \
    --replication-factor 1 \
    --config retention.ms=604800000 \
    --config cleanup.policy=delete

# Dead letter queue for failed messages
kafka-topics --bootstrap-server $KAFKA_BOOTSTRAP \
    --create \
    --if-not-exists \
    --topic trades.dlq \
    --partitions 3 \
    --replication-factor 1 \
    --config retention.ms=2592000000

# Price updates topic
kafka-topics --bootstrap-server $KAFKA_BOOTSTRAP \
    --create \
    --if-not-exists \
    --topic prices.updates \
    --partitions 6 \
    --replication-factor 1 \
    --config retention.ms=86400000

echo "Listing created topics:"
kafka-topics --bootstrap-server $KAFKA_BOOTSTRAP --list

echo "Topic details:"
kafka-topics --bootstrap-server $KAFKA_BOOTSTRAP --describe --topic trades.raw
