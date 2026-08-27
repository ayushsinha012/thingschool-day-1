import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { authInterceptor } from '../auth.interceptor';
import { errorMappingInterceptor } from '../http/error-mapping.interceptor';
import { retryInterceptor } from '../http/retry.interceptor';
import { HttpLab } from './http-lab';

const QUOTES_URL = 'http://localhost:5062/api/quotes';

// Drives the actual component + template (jsdom, no live backend) to verify
// the loading/error/empty/success states a real click-through would show.
describe('HttpLab', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpLab],
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

  it('shows the success state with quotes after Load quotes is clicked', () => {
    const fixture = TestBed.createComponent(HttpLab);
    fixture.detectChanges();

    const loadButton = fixture.nativeElement.querySelector('.primary-button') as HTMLButtonElement;
    loadButton.click();

    httpMock.expectOne((r) => r.url === QUOTES_URL).flush({
      page: 1,
      size: 5,
      total: 1,
      items: [{ id: 1, author: 'Mark Twain', text: 'Get busy living.', isDeleted: false }]
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Get busy living.');
    expect(text).toContain('Mark Twain');
  });

  it('shows the empty state when the page has no items', () => {
    const fixture = TestBed.createComponent(HttpLab);
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.primary-button') as HTMLButtonElement).click();
    httpMock.expectOne((r) => r.url === QUOTES_URL).flush({ page: 1, size: 5, total: 0, items: [] });
    fixture.detectChanges();

    expect((fixture.nativeElement.textContent as string)).toContain('No quotes on this page.');
  });

  it('shows the friendly 400 message when Trigger 400 is clicked', () => {
    const fixture = TestBed.createComponent(HttpLab);
    fixture.detectChanges();

    const demoButtons = fixture.nativeElement.querySelectorAll('.demo-button');
    (demoButtons[0] as HTMLButtonElement).click();

    httpMock.expectOne((r) => r.url === QUOTES_URL).flush(
      {
        title: 'Invalid pagination',
        status: 400,
        detail: 'Page must be at least 1 and size must be between 1 and 100.'
      },
      { status: 400, statusText: 'Bad Request' }
    );
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Page must be at least 1 and size must be between 1 and 100.');
    expect(text).toContain('kind: validation');
    expect(text).toContain('status: 400');
  });

  it('shows the friendly 404 message when Trigger 404 is clicked', () => {
    const fixture = TestBed.createComponent(HttpLab);
    fixture.detectChanges();

    const demoButtons = fixture.nativeElement.querySelectorAll('.demo-button');
    (demoButtons[1] as HTMLButtonElement).click();

    httpMock.expectOne((r) => r.url === `${QUOTES_URL}/999999`).flush(
      { title: 'Quote not found', status: 404, detail: 'No quote exists with ID 999999.' },
      { status: 404, statusText: 'Not Found' }
    );
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No quote exists with ID 999999.');
    expect(text).toContain('kind: not-found');
  });
});
