import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { toAppError } from './problem-details';

// Placed before retryInterceptor in app.config's interceptor array, so it is
// further from the backend and only maps the error the retry cascade gives up
// on (retryInterceptor needs the raw HttpErrorResponse.status to decide
// whether a failure is transient).
export const errorMappingInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse) {
        return throwError(() => toAppError(err));
      }

      return throwError(() => err);
    })
  );
