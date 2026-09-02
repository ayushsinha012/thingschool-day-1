import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { OutboxMessageSummary, OutboxRelaySnapshot } from './outbox';

@Injectable({ providedIn: 'root' })
export class OutboxService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/outbox`;

  getMessages(take = 50): Observable<OutboxMessageSummary[]> {
    return this.http.get<OutboxMessageSummary[]>(this.baseUrl, { params: { take } });
  }

  getStatus(): Observable<OutboxRelaySnapshot> {
    return this.http.get<OutboxRelaySnapshot>(`${this.baseUrl}/status`);
  }
}
