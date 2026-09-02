import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { authInterceptor } from '../auth.interceptor';
import { errorMappingInterceptor } from '../http/error-mapping.interceptor';
import { retryInterceptor } from '../http/retry.interceptor';
import { Outbox } from './outbox';

const MESSAGES_URL = 'http://localhost:5062/api/outbox';
const STATUS_URL = 'http://localhost:5062/api/outbox/status';

describe('Outbox', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Outbox],
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])),
        provideHttpClientTesting()
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows a loading state before the first poll resolves', () => {
    const fixture = TestBed.createComponent(Outbox);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Loading');

    httpMock.expectOne((r) => r.method === 'GET' && r.url === MESSAGES_URL).flush([]);
    httpMock.expectOne((r) => r.method === 'GET' && r.url === STATUS_URL).flush({
      lastRunAtUtc: null,
      lastPublishedCount: 0,
      lastError: null
    });
  });

  it('shows the empty state when there are no outbox messages yet', () => {
    const fixture = TestBed.createComponent(Outbox);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && r.url === MESSAGES_URL).flush([]);
    httpMock.expectOne((r) => r.method === 'GET' && r.url === STATUS_URL).flush({
      lastRunAtUtc: null,
      lastPublishedCount: 0,
      lastError: null
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No outbox messages yet');
  });

  it('renders a pending and a sent outbox message with the relay status', () => {
    const fixture = TestBed.createComponent(Outbox);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && r.url === MESSAGES_URL).flush([
      {
        id: 2,
        messageId: 'quote-created-2',
        messageType: 'quote.created',
        createdAt: '2026-09-02T00:00:01Z',
        sentAt: null,
        attemptCount: 0,
        lastError: null
      },
      {
        id: 1,
        messageId: 'quote-created-1',
        messageType: 'quote.created',
        createdAt: '2026-09-02T00:00:00Z',
        sentAt: '2026-09-02T00:00:02Z',
        attemptCount: 1,
        lastError: null
      }
    ]);
    httpMock.expectOne((r) => r.method === 'GET' && r.url === STATUS_URL).flush({
      lastRunAtUtc: '2026-09-02T00:00:02Z',
      lastPublishedCount: 1,
      lastError: null
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('quote-created-1');
    expect(text).toContain('quote-created-2');
    expect(text).toContain('Pending');
    expect(text).toContain('Sent');
  });

  it('shows the error state when the outbox request fails', () => {
    const fixture = TestBed.createComponent(Outbox);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && r.url === STATUS_URL).flush({
      lastRunAtUtc: null,
      lastPublishedCount: 0,
      lastError: null
    });
    httpMock.expectOne((r) => r.method === 'GET' && r.url === MESSAGES_URL).flush(
      { title: 'Not found' },
      { status: 404, statusText: 'Not Found' }
    );
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Could not load outbox messages');
  });
});
