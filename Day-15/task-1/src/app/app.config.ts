import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { routes } from './app.routes';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { errorMappingInterceptor } from './http/error-mapping.interceptor';
import { retryInterceptor } from './http/retry.interceptor';

// Dev-only convenience: logs in as QuotesApi's seeded test user
// (day-1/QuotesApi/Data/DbSeeder.cs) before the app starts, so the Create
// form's POST /api/quotes has a bearer token instead of always getting a
// 401. This is not a real auth flow - there's no login UI, and this
// account exists only because the backend seeds it for local development.
const DEV_EMAIL = 'ayush.test@example.com';
const DEV_PASSWORD = 'TestPassword123!';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // Order matters: interceptors run outer-to-inner on the request, and
    // inner-to-outer on the response/error. authInterceptor (outermost) just
    // adds a header either way. retryInterceptor is innermost so it sees the
    // raw HttpErrorResponse from the backend and can retry transient GET
    // failures; only once it gives up does errorMappingInterceptor turn what's
    // left into a typed AppError for the app to consume.
    provideHttpClient(withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])),
    provideRouter(routes),
    provideAppInitializer(() => {
      const auth = inject(AuthService);
      return firstValueFrom(auth.login(DEV_EMAIL, DEV_PASSWORD)).catch(() => undefined);
    })
  ]
};
