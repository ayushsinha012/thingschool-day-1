import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

const QUOTES_API_BASE_URL = 'http://localhost:5062/api';

// Attaches the bearer token (once AuthService has logged in) to requests
// against QuotesApi. Requests to other origins pass through untouched.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).token();

  if (!token || !req.url.startsWith(QUOTES_API_BASE_URL)) {
    return next(req);
  }

  return next(
    req.clone({
      setHeaders: { Authorization: `Bearer ${token}` }
    })
  );
};
