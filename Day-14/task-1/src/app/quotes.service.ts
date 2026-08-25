import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateQuoteRequest, Quote } from './quote';

@Injectable({ providedIn: 'root' })
export class QuotesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = 'http://localhost:5062/api/quotes';

  createQuote(request: CreateQuoteRequest): Observable<Quote> {
    return this.http.post<Quote>(this.baseUrl, request);
  }
}
