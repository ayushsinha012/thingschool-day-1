import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

interface LoginResponse {
  access_token: string;
  refresh_token: string;
  expires_in: number;
}

// Holds the bearer token used to authorize POST /api/quotes. Kept in memory
// only (not localStorage/sessionStorage) - a page refresh means logging in
// again, which is fine for this dev-only flow (see app.config.ts).
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = 'http://localhost:5062/api/auth';

  private readonly accessToken = signal<string | null>(null);

  readonly token = this.accessToken.asReadonly();

  login(email: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.baseUrl}/login`, { email, password }).pipe(
      tap((response) => this.accessToken.set(response.access_token))
    );
  }
}
