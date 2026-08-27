import { Component, computed, inject, signal } from '@angular/core';
import { Quote, QuoteDetail } from '../quote';
import { QuotesService } from '../quotes.service';
import { AppError } from '../http/app-error';
import { RetryStatusService } from '../http/retry-status.service';

// Demonstrates the Day 15 interceptor chain (auth -> errorMapping -> retry,
// see app.config.ts) against the same GET /api/quotes and GET /api/quotes/{id}
// endpoints Explore uses, but surfaces the typed AppError instead of parsing
// HttpErrorResponse locally, and the live retry indicator from
// RetryStatusService.
@Component({
  selector: 'app-http-lab',
  imports: [],
  templateUrl: './http-lab.html',
  styleUrl: './http-lab.css'
})
export class HttpLab {
  private readonly quotesService = inject(QuotesService);

  protected readonly retryStatus = inject(RetryStatusService).status;

  protected readonly page = signal(1);
  protected readonly size = signal(5);
  protected readonly quotes = signal<Quote[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly hasLoaded = signal(false);
  protected readonly appError = signal<AppError | null>(null);

  protected readonly viewState = computed<'idle' | 'loading' | 'error' | 'empty' | 'success'>(() => {
    if (this.loading()) {
      return 'loading';
    }

    if (this.appError()) {
      return 'error';
    }

    if (!this.hasLoaded()) {
      return 'idle';
    }

    if (this.quotes().length === 0) {
      return 'empty';
    }

    return 'success';
  });

  protected readonly detailLoading = signal(false);
  protected readonly detailError = signal<AppError | null>(null);
  protected readonly detail = signal<QuoteDetail | null>(null);

  protected readonly objectEntries = Object.entries;

  protected load(): void {
    this.loading.set(true);
    this.appError.set(null);
    this.hasLoaded.set(true);

    this.quotesService.getQuotes(this.page(), this.size()).subscribe({
      next: (result) => {
        this.quotes.set(result.items);
        this.total.set(result.total);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.quotes.set([]);
        this.appError.set(this.asAppError(err));
        this.loading.set(false);
      }
    });
  }

  protected triggerInvalidPagination(): void {
    this.page.set(0);
    this.load();
  }

  protected resetAndLoad(): void {
    this.page.set(1);
    this.load();
  }

  protected triggerMissingQuote(): void {
    this.detailLoading.set(true);
    this.detailError.set(null);
    this.detail.set(null);

    this.quotesService.getQuoteById(999999).subscribe({
      next: (result) => {
        this.detail.set(result);
        this.detailLoading.set(false);
      },
      error: (err: unknown) => {
        this.detailError.set(this.asAppError(err));
        this.detailLoading.set(false);
      }
    });
  }

  private asAppError(err: unknown): AppError {
    return err instanceof AppError
      ? err
      : new AppError('unknown', 'Something went wrong. Please try again.', 0);
  }
}
