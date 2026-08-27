import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize, retry, throwError, timer } from 'rxjs';
import { RetryStatusService } from './retry-status.service';

const MAX_RETRIES = 3;
const BASE_DELAY_MS = 300;

function isTransient(err: unknown): boolean {
  return err instanceof HttpErrorResponse && (err.status === 0 || err.status >= 500);
}

// Retries idempotent GETs on transient failures (network drop, 5xx) with
// exponential backoff (300ms, 600ms, 1200ms). A 4xx means the server correctly
// rejected the request (bad pagination, not found, validation) - retrying that
// would never succeed, so it passes straight through, unretried, to
// errorMappingInterceptor. Non-GET requests (POST/DELETE) are never retried
// here since they are not idempotent.
export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  const retryStatus = inject(RetryStatusService);

  return next(req).pipe(
    retry({
      count: MAX_RETRIES,
      delay: (error: unknown, retryCount: number) => {
        if (!isTransient(error)) {
          return throwError(() => error);
        }

        retryStatus.report({ url: req.url, attempt: retryCount, maxAttempts: MAX_RETRIES });
        return timer(BASE_DELAY_MS * 2 ** (retryCount - 1));
      }
    }),
    finalize(() => retryStatus.clear())
  );
};
