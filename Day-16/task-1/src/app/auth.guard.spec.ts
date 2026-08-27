import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

// authGuard doesn't read anything off the route/state snapshots it's given -
// only AuthService.token() - so an empty stand-in is enough for both.
const NOOP_SNAPSHOT = {} as never;

describe('authGuard', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  function runGuard(): boolean | UrlTree {
    return TestBed.runInInjectionContext(() => authGuard(NOOP_SNAPSHOT, NOOP_SNAPSHOT)) as boolean | UrlTree;
  }

  it('redirects to /explore when there is no auth token', () => {
    const result = runGuard();

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/explore');
  });

  it('allows activation once AuthService holds a token', () => {
    // Drive AuthService the same way app.config.ts's provideAppInitializer
    // does - through the real login() call - rather than reaching into its
    // private token state.
    TestBed.inject(AuthService).login('ayush.test@example.com', 'TestPassword123!').subscribe();

    httpMock
      .expectOne((r) => r.url === 'http://localhost:5062/api/auth/login')
      .flush({ access_token: 'fake-token', refresh_token: 'fake-refresh', expires_in: 3600 });

    expect(runGuard()).toBe(true);
  });
});
