export type AppErrorKind = 'validation' | 'not-found' | 'unauthorized' | 'server' | 'network' | 'unknown';

// Typed error errorMappingInterceptor throws in place of the raw HttpErrorResponse,
// so components can branch on `kind` and show `friendlyMessage` instead of parsing
// ProblemDetails/ValidationProblemDetails bodies themselves.
export class AppError extends Error {
  constructor(
    readonly kind: AppErrorKind,
    readonly friendlyMessage: string,
    readonly status: number,
    readonly fieldErrors?: Record<string, string[]>,
    readonly detail?: string
  ) {
    super(friendlyMessage);
    this.name = 'AppError';
  }
}
