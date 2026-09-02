import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, forkJoin, interval, map, of, startWith, switchMap } from 'rxjs';
import { AppError } from '../http/app-error';
import { OutboxMessageSummary, OutboxRelaySnapshot } from '../outbox';
import { OutboxService } from '../outbox.service';

const POLL_INTERVAL_MS = 2000;

@Component({
  selector: 'app-outbox',
  imports: [DatePipe],
  templateUrl: './outbox.html',
  styleUrl: './outbox.css'
})
export class Outbox {
  private readonly outboxService = inject(OutboxService);

  protected readonly messages = signal<OutboxMessageSummary[]>([]);
  protected readonly status = signal<OutboxRelaySnapshot | null>(null);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<AppError | null>(null);

  constructor() {
    interval(POLL_INTERVAL_MS)
      .pipe(
        startWith(0),
        switchMap(() =>
          forkJoin([this.outboxService.getMessages(50), this.outboxService.getStatus()]).pipe(
            map(([messages, status]) => ({ messages, status, error: null as AppError | null })),
            catchError((err: unknown) =>
              of({ messages: null, status: null, error: this.asAppError(err) })
            )
          )
        ),
        takeUntilDestroyed()
      )
      .subscribe(({ messages, status, error }) => {
        this.loaded.set(true);

        if (error) {
          this.loadError.set(error);
          return;
        }

        this.loadError.set(null);
        this.messages.set(messages ?? []);
        this.status.set(status);
      });
  }

  protected stateClass(message: OutboxMessageSummary): string {
    if (message.sentAt) {
      return 'status-badge status-processed';
    }

    return message.attemptCount > 0 ? 'status-badge status-duplicate' : 'status-badge status-received';
  }

  private asAppError(err: unknown): AppError {
    return err instanceof AppError
      ? err
      : new AppError('unknown', 'Could not reach the server. Check your connection and try again.', 0);
  }
}
