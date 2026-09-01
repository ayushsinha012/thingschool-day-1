import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { authInterceptor } from '../auth.interceptor';
import { errorMappingInterceptor } from '../http/error-mapping.interceptor';
import { retryInterceptor } from '../http/retry.interceptor';
import { Messaging } from './messaging';

const TOPOLOGY_URL = 'http://localhost:5062/api/messaging/topology';
const ACTIVITY_URL = 'http://localhost:5062/api/messaging/activity';
const PUBLISH_URL = 'http://localhost:5062/api/messaging/publish';

describe('Messaging', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Messaging],
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

  it('polls topology and activity on load and renders both subscriptions', () => {
    const fixture = TestBed.createComponent(Messaging);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && r.url === TOPOLOGY_URL).flush([
      { name: 'sub-audit', activeMessageCount: 0, deadLetterMessageCount: 0, totalMessageCount: 3 },
      { name: 'sub-notifications', activeMessageCount: 0, deadLetterMessageCount: 1, totalMessageCount: 3 }
    ]);
    httpMock.expectOne((r) => r.method === 'GET' && r.url === ACTIVITY_URL).flush([]);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('sub-audit');
    expect(text).toContain('sub-notifications');
  });

  it('publishes and shows the returned MessageId', () => {
    const fixture = TestBed.createComponent(Messaging);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && r.url === TOPOLOGY_URL).flush([]);
    httpMock.expectOne((r) => r.method === 'GET' && r.url === ACTIVITY_URL).flush([]);
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.primary-button') as HTMLButtonElement).click();

    httpMock.expectOne((r) => r.method === 'POST' && r.url === PUBLISH_URL).flush({
      messageId: 'demo-order-1',
      eventType: 'quote.created',
      topicName: 'quote-events',
      publishedAtUtc: '2026-09-01T00:00:00Z',
      poison: false
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('demo-order-1');
  });

  it('renders a Duplicate outcome badge from polled activity', () => {
    const fixture = TestBed.createComponent(Messaging);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && r.url === TOPOLOGY_URL).flush([]);
    httpMock.expectOne((r) => r.method === 'GET' && r.url === ACTIVITY_URL).flush([
      {
        timestampUtc: '2026-09-01T00:00:01Z',
        subscriptionName: 'sub-audit',
        workerSlot: 'A2',
        messageId: 'demo-order-1',
        eventType: 'quote.created',
        outcome: 'Duplicate',
        deliveryCount: 1,
        detail: 'MessageId already processed on this subscription'
      }
    ]);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Duplicate');
    expect(text).toContain('A2');
  });

  it('shows the friendly error message when publish is rejected', () => {
    const fixture = TestBed.createComponent(Messaging);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && r.url === TOPOLOGY_URL).flush([]);
    httpMock.expectOne((r) => r.method === 'GET' && r.url === ACTIVITY_URL).flush([]);
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.primary-button') as HTMLButtonElement).click();

    httpMock.expectOne((r) => r.method === 'POST' && r.url === PUBLISH_URL).flush(
      { title: 'Invalid publish request', status: 400, detail: 'Payload must be 2000 characters or fewer.' },
      { status: 400, statusText: 'Bad Request' }
    );
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Payload must be 2000 characters or fewer.');
  });

  it('peeks dead letters for the selected subscription', () => {
    const fixture = TestBed.createComponent(Messaging);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.method === 'GET' && r.url === TOPOLOGY_URL).flush([]);
    httpMock.expectOne((r) => r.method === 'GET' && r.url === ACTIVITY_URL).flush([]);
    fixture.detectChanges();

    const buttons = fixture.nativeElement.querySelectorAll('.secondary-button') as NodeListOf<HTMLButtonElement>;
    const peekButton = Array.from(buttons).find((b) => b.textContent?.includes('Peek dead letters'));
    peekButton?.click();

    httpMock
      .expectOne((r) => r.method === 'GET' && r.url === 'http://localhost:5062/api/messaging/dead-letters/sub-audit')
      .flush([
        {
          messageId: 'poison-demo-1',
          eventType: 'quote.poison-test',
          subscriptionName: 'sub-audit',
          deliveryCount: 3,
          deadLetterReason: 'MaxDeliveryCountExceeded',
          deadLetterErrorDescription: 'Message could not be consumed after 3 delivery attempts.',
          enqueuedTimeUtc: '2026-09-01T00:00:00Z',
          body: 'this message always fails'
        }
      ]);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('MaxDeliveryCountExceeded');
    expect(text).toContain('poison-demo-1');
  });
});
