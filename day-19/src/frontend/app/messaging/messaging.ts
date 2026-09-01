import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { forkJoin, interval, startWith, switchMap } from 'rxjs';
import { AppError } from '../http/app-error';
import {
  ActivityOutcomeName,
  ConsumerActivityEntry,
  DeadLetterMessageSummary,
  SubscriptionTopology
} from '../messaging';
import { MessagingService } from '../messaging.service';

const MAX_EVENT_TYPE_LENGTH = 100;
const MAX_PAYLOAD_LENGTH = 2000;
const POLL_INTERVAL_MS = 1500;

@Component({
  selector: 'app-messaging',
  imports: [FormsModule, DatePipe],
  templateUrl: './messaging.html',
  styleUrl: './messaging.css'
})
export class Messaging {
  private readonly messagingService = inject(MessagingService);

  protected readonly maxEventTypeLength = MAX_EVENT_TYPE_LENGTH;
  protected readonly maxPayloadLength = MAX_PAYLOAD_LENGTH;
  protected readonly subscriptions = ['sub-audit', 'sub-notifications'];

  protected readonly eventType = signal('quote.created');
  protected readonly payload = signal('demo quote payload');
  protected readonly idempotencyKey = signal('');
  protected readonly poison = signal(false);

  protected readonly publishing = signal(false);
  protected readonly publishError = signal<AppError | null>(null);
  protected readonly lastPublished = signal<{ messageId: string; eventType: string; poison: boolean } | null>(null);

  protected readonly topology = signal<SubscriptionTopology[]>([]);
  protected readonly topologyLoaded = signal(false);

  protected readonly activity = signal<ConsumerActivityEntry[]>([]);
  protected readonly activityLoaded = signal(false);

  protected readonly selectedDlqSubscription = signal(this.subscriptions[0]);
  protected readonly deadLetters = signal<DeadLetterMessageSummary[]>([]);
  protected readonly dlqLoading = signal(false);
  protected readonly dlqLoaded = signal(false);
  protected readonly dlqError = signal<AppError | null>(null);

  protected readonly fieldInvalid = computed(
    () => this.eventType().length > MAX_EVENT_TYPE_LENGTH || this.payload().length > MAX_PAYLOAD_LENGTH
  );

  constructor() {
    interval(POLL_INTERVAL_MS)
      .pipe(
        startWith(0),
        switchMap(() => forkJoin([this.messagingService.getTopology(), this.messagingService.getActivity(50)])),
        takeUntilDestroyed()
      )
      .subscribe({
        next: ([topologyResult, activityResult]) => {
          this.topology.set(topologyResult);
          this.topologyLoaded.set(true);
          this.activity.set(activityResult);
          this.activityLoaded.set(true);
        }
      });
  }

  protected publish(): void {
    if (this.publishing() || this.fieldInvalid()) {
      return;
    }

    this.publishing.set(true);
    this.publishError.set(null);

    this.messagingService
      .publish({
        eventType: this.eventType().trim() || undefined,
        payload: this.payload(),
        idempotencyKey: this.idempotencyKey().trim() || undefined,
        poison: this.poison()
      })
      .subscribe({
        next: (published) => {
          this.publishing.set(false);
          this.lastPublished.set({
            messageId: published.messageId,
            eventType: published.eventType,
            poison: published.poison
          });
        },
        error: (err: unknown) => {
          this.publishing.set(false);
          this.publishError.set(this.asAppError(err));
        }
      });
  }

  protected replayLastMessage(): void {
    const last = this.lastPublished();

    if (!last) {
      return;
    }

    this.idempotencyKey.set(last.messageId);
    this.publish();
  }

  protected peekDeadLetters(): void {
    this.dlqLoading.set(true);
    this.dlqError.set(null);

    this.messagingService.getDeadLetters(this.selectedDlqSubscription()).subscribe({
      next: (result) => {
        this.deadLetters.set(result);
        this.dlqLoading.set(false);
        this.dlqLoaded.set(true);
      },
      error: (err: unknown) => {
        this.dlqLoading.set(false);
        this.dlqError.set(this.asAppError(err));
      }
    });
  }

  protected outcomeClass(outcome: ActivityOutcomeName): string {
    switch (outcome) {
      case 'Received':
        return 'status-badge status-received';
      case 'Processed':
        return 'status-badge status-processed';
      case 'Duplicate':
        return 'status-badge status-duplicate';
      case 'PoisonFailed':
        return 'status-badge status-poison';
    }
  }

  private asAppError(err: unknown): AppError {
    return err instanceof AppError
      ? err
      : new AppError('unknown', 'Could not reach the server. Check your connection and try again.', 0);
  }
}
