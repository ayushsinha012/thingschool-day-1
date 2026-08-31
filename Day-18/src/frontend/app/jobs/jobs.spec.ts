import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { authInterceptor } from '../auth.interceptor';
import { errorMappingInterceptor } from '../http/error-mapping.interceptor';
import { retryInterceptor } from '../http/retry.interceptor';
import { Jobs } from './jobs';

const JOBS_URL = 'http://localhost:5062/api/jobs';

// Drives the real component + template (jsdom, no live backend) to verify
// the status transitions a click-through against QuotesApi would show -
// enqueue returning before the job finishes, then the polled table picking
// up Running/Completed/Failed. This app uses zoneless change detection, so
// timing is driven with vitest's fake timers rather than
// fakeAsync/tick (which need zone.js/testing).
describe('Jobs', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Jobs],
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])),
        provideHttpClientTesting()
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('polls GET /api/jobs on load and renders the recent jobs table', () => {
    const fixture = TestBed.createComponent(Jobs);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && r.url === JOBS_URL).flush([
      {
        id: 'a1',
        label: 'demo digest',
        status: 'Completed',
        createdAt: '2026-08-31T00:00:00Z',
        startedAt: '2026-08-31T00:00:01Z',
        completedAt: '2026-08-31T00:00:05Z',
        error: null
      }
    ]);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('demo digest');
    expect(text).toContain('Completed');
  });

  it('shows the request timing after enqueueing, before the job completes', () => {
    const fixture = TestBed.createComponent(Jobs);
    fixture.detectChanges();

    // Initial poll on load.
    httpMock.expectOne((r) => r.method === 'GET' && r.url === JOBS_URL).flush([]);
    fixture.detectChanges();

    const enqueueButton = fixture.nativeElement.querySelector('.primary-button') as HTMLButtonElement;
    enqueueButton.click();

    const postReq = httpMock.expectOne((r) => r.method === 'POST' && r.url === JOBS_URL);
    postReq.flush({
      id: 'b2',
      label: 'demo digest',
      status: 'Queued',
      createdAt: '2026-08-31T00:00:00Z',
      startedAt: null,
      completedAt: null,
      error: null
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toMatch(/returned in \d+ms/);
    // The job just enqueued is Queued, not Completed - the request finished
    // first.
    expect(text).toContain('Queued');
  });

  it('shows the friendly error message when enqueue is rejected', () => {
    const fixture = TestBed.createComponent(Jobs);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && r.url === JOBS_URL).flush([]);
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.primary-button') as HTMLButtonElement).click();

    httpMock.expectOne((r) => r.method === 'POST' && r.url === JOBS_URL).flush(
      { title: 'Invalid job request', status: 400, detail: 'DurationSeconds must be between 1 and 20.' },
      { status: 400, statusText: 'Bad Request' }
    );
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('DurationSeconds must be between 1 and 20.');
  });

  it('keeps polling on an interval while the component is alive', async () => {
    vi.useFakeTimers();

    const fixture = TestBed.createComponent(Jobs);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && r.url === JOBS_URL).flush([
      {
        id: 'a1',
        label: 'demo',
        status: 'Running',
        createdAt: '2026-08-31T00:00:00Z',
        startedAt: '2026-08-31T00:00:01Z',
        completedAt: null,
        error: null
      }
    ]);
    fixture.detectChanges();

    await vi.advanceTimersByTimeAsync(1200);

    httpMock.expectOne((r) => r.method === 'GET' && r.url === JOBS_URL).flush([
      {
        id: 'a1',
        label: 'demo',
        status: 'Completed',
        createdAt: '2026-08-31T00:00:00Z',
        startedAt: '2026-08-31T00:00:01Z',
        completedAt: '2026-08-31T00:00:02Z',
        error: null
      }
    ]);
    fixture.detectChanges();

    expect((fixture.nativeElement.textContent as string)).toContain('Completed');
  });
});
