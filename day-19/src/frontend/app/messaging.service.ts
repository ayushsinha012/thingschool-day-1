import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import {
  ConsumerActivityEntry,
  DeadLetterMessageSummary,
  PublishEventRequest,
  PublishedEvent,
  SubscriptionTopology
} from './messaging';

@Injectable({ providedIn: 'root' })
export class MessagingService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/messaging`;

  publish(request: PublishEventRequest): Observable<PublishedEvent> {
    return this.http.post<PublishedEvent>(`${this.baseUrl}/publish`, request);
  }

  getTopology(): Observable<SubscriptionTopology[]> {
    return this.http.get<SubscriptionTopology[]>(`${this.baseUrl}/topology`);
  }

  getActivity(take = 50): Observable<ConsumerActivityEntry[]> {
    return this.http.get<ConsumerActivityEntry[]>(`${this.baseUrl}/activity`, {
      params: { take }
    });
  }

  getDeadLetters(subscription: string): Observable<DeadLetterMessageSummary[]> {
    return this.http.get<DeadLetterMessageSummary[]>(`${this.baseUrl}/dead-letters/${subscription}`);
  }
}
