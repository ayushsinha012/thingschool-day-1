import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { CreateQuoteRequest, Quote, QuoteDetail, QuotesPage } from './quote';

@Injectable({ providedIn: 'root' })
export class QuotesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = 'http://localhost:5062/api/quotes';

  // search is applied server-side (GET /api/quotes?...&search=) so results
  // come from the whole dataset, not just the currently loaded page.
  getQuotes(page: number, size: number, search?: string): Observable<QuotesPage> {
    const params: Record<string, string | number> = { page, size };

    if (search) {
      params['search'] = search;
    }

    return this.http.get<QuotesPage>(this.baseUrl, { params });
  }

  createQuote(request: CreateQuoteRequest): Observable<Quote> {
    return this.http.post<Quote>(this.baseUrl, request);
  }

  // GET /api/quotes/{id} returns a plain Quote - display/characterCount are
  // derived here rather than assumed from the API response.
  getQuoteById(id: number): Observable<QuoteDetail> {
    return this.http.get<Quote>(`${this.baseUrl}/${id}`).pipe(
      map((quote) => ({
        id: quote.id,
        author: quote.author,
        text: quote.text,
        display: `“${quote.text}” — ${quote.author}`,
        characterCount: quote.text.length
      }))
    );
  }
}
