import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { Quote, QuoteDetail, QuotesPage } from './quote';

@Injectable({ providedIn: 'root' })
export class QuotesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = 'http://localhost:5062/api/quotes';

  getQuotes(page: number, size: number): Observable<QuotesPage> {
    return this.http.get<QuotesPage>(this.baseUrl, {
      params: { page, size }
    });
  }

  // GET /api/quotes/{id} returns a plain Quote (id, author, text, isDeleted) -
  // display/characterCount are derived here rather than assumed from the
  // API response, which never included them.
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
