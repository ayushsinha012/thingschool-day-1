import { Injectable, signal } from '@angular/core';

export interface RetryStatus {
  readonly url: string;
  readonly attempt: number;
  readonly maxAttempts: number;
}

// Lets retryInterceptor surface in-progress retry attempts to the UI (see
// http-lab component) without components reaching into the interceptor chain.
@Injectable({ providedIn: 'root' })
export class RetryStatusService {
  private readonly _status = signal<RetryStatus | null>(null);

  readonly status = this._status.asReadonly();

  report(status: RetryStatus): void {
    this._status.set(status);
  }

  clear(): void {
    this._status.set(null);
  }
}
