import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

// Guards routes that need the same bearer token QuotesApi itself requires
// (POST /api/quotes is .RequireAuthorization(PermissionClaims.CanEditQuotes) -
// see day-1/QuotesApi/Endpoints/QuoteEndpoints.cs). Reuses AuthService's
// existing in-memory token signal - no second auth system, no new state.
//
// There is no dedicated /login route in this project: the only "auth flow"
// that exists is the dev-only auto-login in app.config.ts's
// provideAppInitializer, which runs once before the router is even active
// (see result.md for why that makes this guard hard to observe redirecting
// in normal use). With no login screen to send an unauthenticated user to,
// this redirects back to /explore - the app's one real entry point - rather
// than inventing a /login page that doesn't otherwise exist.
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.token()) {
    return true;
  }

  return router.createUrlTree(['/explore']);
};
