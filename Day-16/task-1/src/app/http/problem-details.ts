import { HttpErrorResponse } from '@angular/common/http';
import { AppError } from './app-error';

// Shapes QuoteEndpoints actually returns (day-1/QuotesApi/Endpoints/QuoteEndpoints.cs,
// Validation/ValidationExtensions.cs), confirmed against the running API:
//
//   GET /api/quotes?page=0            -> 400 ProblemDetails
//     { type, title: "Invalid pagination", status: 400, detail: "Page must be..." }
//
//   GET /api/quotes/{id}  (not found) -> 404 ProblemDetails
//     { type, title: "Quote not found", status: 404, detail: "No quote exists with ID {id}." }
//
//   POST /api/quotes  (bad body)      -> 400 ValidationProblemDetails
//     { type, title: "One or more validation errors occurred.", status: 400,
//       errors: { Author: ["..."], Text: ["..."] }, traceId }
interface ProblemDetailsBody {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly traceId?: string;
}

interface ValidationProblemDetailsBody extends ProblemDetailsBody {
  readonly errors: Record<string, string[]>;
}

function isValidationProblem(body: unknown): body is ValidationProblemDetailsBody {
  return (
    !!body &&
    typeof body === 'object' &&
    'errors' in body &&
    typeof (body as { errors: unknown }).errors === 'object'
  );
}

export function toAppError(err: HttpErrorResponse): AppError {
  if (err.status === 0) {
    return new AppError('network', 'Unable to reach the server. Check your connection and try again.', 0);
  }

  const body = err.error as ProblemDetailsBody | ValidationProblemDetailsBody | null;

  if (err.status === 400 && isValidationProblem(body)) {
    const messages = Object.values(body.errors).flat();

    return new AppError(
      'validation',
      messages.length > 0 ? messages.join(' ') : 'Some fields need attention.',
      400,
      body.errors,
      body.detail
    );
  }

  if (err.status === 400) {
    return new AppError(
      'validation',
      body?.detail ?? body?.title ?? 'The request was invalid.',
      400,
      undefined,
      body?.detail
    );
  }

  if (err.status === 404) {
    return new AppError('not-found', body?.detail ?? 'The requested item was not found.', 404, undefined, body?.detail);
  }

  if (err.status === 401 || err.status === 403) {
    return new AppError('unauthorized', 'You are not authorized to do that.', err.status);
  }

  if (err.status >= 500) {
    return new AppError('server', 'Something went wrong on the server. Please try again.', err.status);
  }

  return new AppError('unknown', `Request failed (${err.status}).`, err.status);
}
