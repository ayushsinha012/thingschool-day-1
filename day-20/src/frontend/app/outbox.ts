export interface OutboxMessageSummary {
  id: number;
  messageId: string;
  messageType: string;
  createdAt: string;
  sentAt: string | null;
  attemptCount: number;
  lastError: string | null;
}

export interface OutboxRelaySnapshot {
  lastRunAtUtc: string | null;
  lastPublishedCount: number;
  lastError: string | null;
}
