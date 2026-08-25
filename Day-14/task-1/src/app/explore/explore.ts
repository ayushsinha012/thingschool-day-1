import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { EMPTY, Subscription, catchError, debounceTime, distinctUntilChanged, switchMap, tap } from 'rxjs';
import { Quote, QuoteDetail } from '../quote';
import { QuotesService } from '../quotes.service';

interface DetailRequest {
  readonly id: number;
}

@Component({
  selector: 'app-explore',
  imports: [],
  templateUrl: './explore.html',
  styleUrl: './explore.css'
})
export class Explore {
  private readonly quotesService = inject(QuotesService);

  protected readonly page = signal(1);
  protected readonly size = signal(10);
  protected readonly quotes = signal<Quote[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.total() / this.size()))
  );
  protected readonly hasPrevious = computed(() => this.page() > 1);
  protected readonly hasNext = computed(() => this.page() < this.totalPages());

  // filter is the raw input value (updates every keystroke, for the box
  // itself); searchTerm is debounced and is what actually drives the query,
  // so the API isn't hit on every keystroke. Search runs server-side
  // (QuotesService.getQuotes) against the whole dataset, not just the
  // currently loaded page - a client-side filter over one page's items
  // would miss a match sitting on a different page.
  protected readonly filter = signal('');
  private readonly searchTerm = toSignal(
    toObservable(this.filter).pipe(debounceTime(300), distinctUntilChanged()),
    { initialValue: '' }
  );

  protected readonly viewState = computed<'loading' | 'error' | 'empty' | 'success'>(() => {
    if (this.loading()) {
      return 'loading';
    }

    if (this.error()) {
      return 'error';
    }

    if (this.quotes().length === 0) {
      return 'empty';
    }

    return 'success';
  });

  protected readonly selectedId = signal<number | null>(null);
  protected readonly quoteDetail = signal<QuoteDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailError = signal<string | null>(null);

  // Set on every select/close/retry, always as a fresh object so a retry on the
  // same id still emits (a plain number wouldn't change and toObservable would
  // stay silent). switchMap below cancels whatever request was in flight the
  // moment a new one starts.
  private readonly detailRequest = signal<DetailRequest | null>(null);

  protected readonly detailViewState = computed<'idle' | 'loading' | 'error' | 'success'>(() => {
    if (this.selectedId() === null) {
      return 'idle';
    }

    if (this.detailLoading()) {
      return 'loading';
    }

    if (this.detailError()) {
      return 'error';
    }

    return 'success';
  });

  constructor() {
    // A new search term always starts back at page 1 - the filtered result
    // set is a different size than the unfiltered one, so whatever page the
    // user was on may no longer exist (or may now show unrelated results).
    effect(() => {
      this.searchTerm();
      this.page.set(1);
    });

    effect(() => {
      const page = this.page();
      const size = this.size();
      const search = this.searchTerm();

      this.loadQuotes(page, size, search);
    });

    // switchMap unsubscribes the previous inner request as soon as a new
    // detailRequest value arrives, so a slow response for a quote the user
    // has since clicked away from can never land after (and overwrite) the
    // detail currently on screen.
    toObservable(this.detailRequest)
      .pipe(
        switchMap((request) => {
          if (request === null) {
            this.detailLoading.set(false);
            this.detailError.set(null);
            this.quoteDetail.set(null);
            return EMPTY;
          }

          this.detailLoading.set(true);
          this.detailError.set(null);

          return this.quotesService.getQuoteById(request.id).pipe(
            tap((detail) => {
              this.quoteDetail.set(detail);
              this.detailLoading.set(false);
            }),
            catchError((err: unknown) => {
              this.quoteDetail.set(null);
              this.detailError.set(
                this.describeError(err, 'Quote not found.', 'Failed to load quote.')
              );
              this.detailLoading.set(false);
              return EMPTY;
            })
          );
        }),
        takeUntilDestroyed()
      )
      .subscribe();
  }

  // Cancels whatever list request was in flight before starting a new one,
  // the same way switchMap does for the detail request below - otherwise a
  // slow response for a page the user has since paged away from could land
  // after (and overwrite) the page currently on screen.
  private quotesSubscription?: Subscription;

  private loadQuotes(page: number, size: number, search: string): void {
    this.quotesSubscription?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);

    this.quotesSubscription = this.quotesService.getQuotes(page, size, search).subscribe({
      next: (result) => {
        this.quotes.set(result.items);
        this.total.set(result.total);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(this.describeError(err, 'No quotes found.', 'Failed to load quotes.'));
        this.loading.set(false);
      }
    });
  }

  private describeError(err: unknown, notFoundMessage: string, fallbackMessage: string): string {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 404) {
        return notFoundMessage;
      }

      if (err.status === 0) {
        return 'Unable to reach the server. Check your connection and try again.';
      }
    }

    return fallbackMessage;
  }

  protected retry(): void {
    this.loadQuotes(this.page(), this.size(), this.searchTerm());
  }

  protected retryDetail(): void {
    const id = this.selectedId();

    if (id !== null) {
      this.detailRequest.set({ id });
    }
  }

  protected previousPage(): void {
    if (this.hasPrevious()) {
      this.page.update((page) => page - 1);
    }
  }

  protected nextPage(): void {
    if (this.hasNext()) {
      this.page.update((page) => page + 1);
    }
  }

  protected selectQuote(id: number): void {
    const next = id === this.selectedId() ? null : id;
    this.selectedId.set(next);
    this.detailRequest.set(next === null ? null : { id: next });
  }

  protected closeDetail(): void {
    this.selectedId.set(null);
    this.detailRequest.set(null);
  }

  protected wordCount(text: string): number {
    return text.trim().split(/\s+/).filter(Boolean).length;
  }
}
