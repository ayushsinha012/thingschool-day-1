export type ActivityOutcomeName = 'Received' | 'Processed' | 'Duplicate' | 'PoisonFailed';

export interface PublishEventRequest {
  eventType?: string;
  payload?: string;
  idempotencyKey?: string;
  poison: boolean;
}

export interface PublishedEvent {
  messageId: string;
  eventType: string;
  topicName: string;
  publishedAtUtc: string;
  poison: boolean;
}

export interface SubscriptionTopology {
  name: string;
  activeMessageCount: number;
  deadLetterMessageCount: number;
  totalMessageCount: number;
}

export interface ConsumerActivityEntry {
  timestampUtc: string;
  subscriptionName: string;
  workerSlot: string;
  messageId: string;
  eventType: string;
  outcome: ActivityOutcomeName;
  deliveryCount: number;
  detail: string | null;
}

export interface DeadLetterMessageSummary {
  messageId: string;
  eventType: string;
  subscriptionName: string;
  deliveryCount: number;
  deadLetterReason: string | null;
  deadLetterErrorDescription: string | null;
  enqueuedTimeUtc: string;
  body: string;
}
