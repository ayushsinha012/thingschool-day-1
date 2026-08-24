import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { QuotesPage } from './quote';

@Injectable({ providedIn: 'root' })
export class QuotesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = 'http://localhost:5062/api/quotes';

  getQuotes(page: number, size: number): Observable<QuotesPage> {
    return this.http.get<QuotesPage>(this.baseUrl, {
      params: { page, size }
    });
  }
}
