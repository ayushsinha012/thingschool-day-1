import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { authInterceptor } from '../auth.interceptor';
import { AuthService } from '../auth.service';
import { QuotesPage } from '../quote';
import { QuotesService } from '../quotes.service';
import { errorMappingInterceptor } from './error-mapping.interceptor';
import { retryInterceptor } from './retry.interceptor';

// Characterization tests pinning the real Week-1 API contract as returned by
// day-1/QuotesApi (Endpoints/QuoteEndpoints.cs, Validation/ValidationExtensions.cs),
// confirmed against the running backend on 2026-08-26 - see the verification
// note in this folder for the raw curl output these fixtures are copied from.
const QUOTES_URL = 'http://localhost:5062/api/quotes';
const LOGIN_URL = 'http://localhost:5062/api/auth/login';

function setUpTestBed(): { httpMock: HttpTestingController; quotesService: QuotesService } {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])),
      provideHttpClientTesting()
    ]
  });

  return {
    httpMock: TestBed.inject(HttpTestingController),
    quotesService: TestBed.inject(QuotesService)
  };
}

describe('QuotesApi contract: GET /api/quotes?page=N&size=N', () => {
  let httpMock: HttpTestingController;
  let quotesService: QuotesService;

  beforeEach(() => {
    ({ httpMock, quotesService } = setUpTestBed());
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('pins the success shape {page,size,total,items:[{id,author,text,isDeleted}]}', async () => {
    const responseBody: QuotesPage = {
      page: 1,
      size: 2,
      total: 14,
      items: [
        {
          id: 1,
          author: 'Albert Einstein',
          text: 'Imagination is more important than knowledge.',
          isDeleted: false
        },
        {
          id: 2,
          author: 'Maya Angelou',
          text: 'There is no greater agony than bearing an untold story inside you.',
          isDeleted: false
        }
      ]
    };

    const resultPromise = firstValueFrom(quotesService.getQuotes(1, 2));

    const req = httpMock.expectOne((r) => r.url === QUOTES_URL);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('size')).toBe('2');
    req.flush(responseBody);

    await expect(resultPromise).resolves.toEqual(responseBody);
  });

  it('maps a real 400 ProblemDetails (invalid pagination) to a friendly validation AppError', async () => {
    const resultPromise = firstValueFrom(quotesService.getQuotes(0, 10));

    const req = httpMock.expectOne((r) => r.url === QUOTES_URL);
    req.flush(
      {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'Invalid pagination',
        status: 400,
        detail: 'Page must be at least 1 and size must be between 1 and 100.'
      },
      { status: 400, statusText: 'Bad Request' }
    );

    await expect(resultPromise).rejects.toMatchObject({
      kind: 'validation',
      status: 400,
      friendlyMessage: 'Page must be at least 1 and size must be between 1 and 100.'
    });
  });

  it('maps a real 404 ProblemDetails (missing quote) to a friendly not-found AppError', async () => {
    const resultPromise = firstValueFrom(quotesService.getQuoteById(999999));

    const req = httpMock.expectOne((r) => r.url === `${QUOTES_URL}/999999`);
    req.flush(
      {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
        title: 'Quote not found',
        status: 404,
        detail: 'No quote exists with ID 999999.'
      },
      { status: 404, statusText: 'Not Found' }
    );

    await expect(resultPromise).rejects.toMatchObject({
      kind: 'not-found',
      status: 404,
      friendlyMessage: 'No quote exists with ID 999999.'
    });
  });

  it('attaches the bearer token and maps a real 400 ValidationProblemDetails (bad POST body) to field errors', async () => {
    const auth = TestBed.inject(AuthService);
    const loginPromise = firstValueFrom(auth.login('ayush.test@example.com', 'TestPassword123!'));
    httpMock.expectOne((r) => r.url === LOGIN_URL).flush({
      access_token: 'fake-token',
      refresh_token: 'fake-refresh',
      expires_in: 3600
    });
    await loginPromise;

    const resultPromise = firstValueFrom(quotesService.createQuote({ author: '', text: '' }));

    const req = httpMock.expectOne((r) => r.url === QUOTES_URL && r.method === 'POST');
    expect(req.request.headers.get('Authorization')).toBe('Bearer fake-token');
    req.flush(
      {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: {
          Author: [
            'The Author field is required.',
            'The field Author must be a string with a minimum length of 1 and a maximum length of 200.'
          ],
          Text: [
            'The Text field is required.',
            'The field Text must be a string with a minimum length of 1 and a maximum length of 1000.'
          ]
        },
        traceId: '00-fake-trace-01'
      },
      { status: 400, statusText: 'Bad Request' }
    );

    await expect(resultPromise).rejects.toMatchObject({
      kind: 'validation',
      status: 400,
      fieldErrors: {
        Author: expect.arrayContaining([expect.stringContaining('required')]),
        Text: expect.arrayContaining([expect.stringContaining('required')])
      }
    });
  });
});

describe('retryInterceptor: backoff on idempotent GETs', () => {
  let httpMock: HttpTestingController;
  let quotesService: QuotesService;

  beforeEach(() => {
    vi.useFakeTimers();
    ({ httpMock, quotesService } = setUpTestBed());
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('retries a transient 503 with exponential backoff, then succeeds on the 3rd attempt', async () => {
    const successBody: QuotesPage = { page: 1, size: 10, total: 0, items: [] };
    const resultPromise = firstValueFrom(quotesService.getQuotes(1, 10));

    httpMock
      .expectOne((r) => r.url === QUOTES_URL)
      .flush('Service unavailable', { status: 503, statusText: 'Service Unavailable' });
    await vi.advanceTimersByTimeAsync(300);

    httpMock
      .expectOne((r) => r.url === QUOTES_URL)
      .flush('Service unavailable', { status: 503, statusText: 'Service Unavailable' });
    await vi.advanceTimersByTimeAsync(600);

    httpMock.expectOne((r) => r.url === QUOTES_URL).flush(successBody);

    await expect(resultPromise).resolves.toEqual(successBody);
  });

  it('does not retry a real 400 (invalid pagination is a permanent, not transient, failure)', async () => {
    const resultPromise = firstValueFrom(quotesService.getQuotes(0, 10));

    httpMock.expectOne((r) => r.url === QUOTES_URL).flush(
      {
        title: 'Invalid pagination',
        status: 400,
        detail: 'Page must be at least 1 and size must be between 1 and 100.'
      },
      { status: 400, statusText: 'Bad Request' }
    );

    // No timers to advance and no second request should ever be issued -
    // httpMock.verify() in afterEach fails the test if one was.
    await expect(resultPromise).rejects.toMatchObject({ kind: 'validation', status: 400 });
  });

  it('gives up after 3 retries against a GET that never recovers', async () => {
    const resultPromise = firstValueFrom(quotesService.getQuotes(1, 10));

    for (const delayMs of [300, 600, 1200]) {
      httpMock
        .expectOne((r) => r.url === QUOTES_URL)
        .flush('Service unavailable', { status: 503, statusText: 'Service Unavailable' });
      await vi.advanceTimersByTimeAsync(delayMs);
    }

    // 4th and final attempt also fails - retry({count: 3}) means 1 initial + 3 retries = 4 total.
    httpMock
      .expectOne((r) => r.url === QUOTES_URL)
      .flush('Service unavailable', { status: 503, statusText: 'Service Unavailable' });

    await expect(resultPromise).rejects.toMatchObject({ kind: 'server', status: 503 });
  });
});
