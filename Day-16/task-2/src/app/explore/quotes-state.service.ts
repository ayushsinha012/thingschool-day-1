import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { AppError } from '../http/app-error';
import { Quote } from '../quote';
import { QuotesService } from '../quotes.service';

// Small, local, signal-based feature state for the quotes list. Owns exactly
// what Explore needs to render a page of quotes - the fetching itself still
// goes through the existing QuotesService/HttpClient (see quotes.service.ts);
// this service only owns state and the load/paginate/search operations that
// mutate it. Deliberately plain signals + a service, not NgRx/signal-store -
// the state is small, local to one feature, and doesn't need to be shared
// beyond Explore.
@Injectable({ providedIn: 'root' })
export class QuotesStateService {
  private readonly quotesService = inject(QuotesService);

  private readonly _quotes = signal<Quote[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _page = signal(1);
  private readonly _size = signal(10);
  private readonly _total = signal(0);

  readonly quotes = this._quotes.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly page = this._page.asReadonly();
  readonly size = this._size.asReadonly();
  readonly total = this._total.asReadonly();

  // Derived state - computed from the signals above, never set directly.
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this._total() / this._size())));
  readonly hasPrevious = computed(() => this._page() > 1);
  readonly hasNext = computed(() => this._page() < this.totalPages());
  readonly isEmpty = computed(() => !this._loading() && !this._error() && this._quotes().length === 0);
  readonly viewState: Signal<'loading' | 'error' | 'empty' | 'success'> = computed(() => {
    if (this._loading()) {
      return 'loading';
    }
    if (this._error()) {
      return 'error';
    }
    if (this._quotes().length === 0) {
      return 'empty';
    }
    return 'success';
  });

  // Cancels whatever list request was in flight before starting a new one -
  // otherwise a slow response for a page/search the caller has since moved
  // away from could land after (and overwrite) what's currently on screen.
  // This is the primary guard: unsubscribing tears down the whole HttpClient
  // chain (interceptors + XHR/fetch), so a stale request's next()/error()
  // can never even run.
  private requestSubscription?: Subscription;

  // requestId is a second, independent guard on top of the unsubscribe
  // above. unsubscribe() is enough on its own for this app's real
  // interceptor chain, but the two callbacks below don't rely on that being
  // true forever - if the pipe ever grows a multicasting operator
  // (shareReplay, a caching interceptor, etc.) that keeps a subscription
  // alive past unsubscribe(), a response tagged with an old requestId is
  // still dropped instead of silently overwriting newer state.
  private requestId = 0;

  // Loads a given page/size/search into state. page/size are also written
  // to their own signals so the pagination computed values (totalPages,
  // hasPrevious, hasNext) and the UI's "Page X of Y" stay in sync with
  // whatever was actually requested, not just whatever the caller passed in.
  load(page: number, size: number, search?: string): void {
    this.requestSubscription?.unsubscribe();
    const requestId = ++this.requestId;

    this._page.set(page);
    this._size.set(size);
    this._loading.set(true);
    this._error.set(null);

    this.requestSubscription = this.quotesService.getQuotes(page, size, search).subscribe({
      next: (result) => {
        if (requestId !== this.requestId) {
          return;
        }
        this._quotes.set(result.items);
        this._total.set(result.total);
        this._loading.set(false);
      },
      error: (err: unknown) => {
        if (requestId !== this.requestId) {
          return;
        }
        this._error.set(this.describeError(err));
        this._loading.set(false);
      }
    });
  }

  setPage(page: number, search?: string): void {
    this.load(page, this._size(), search);
  }

  previousPage(search?: string): void {
    if (this.hasPrevious()) {
      this.setPage(this._page() - 1, search);
    }
  }

  nextPage(search?: string): void {
    if (this.hasNext()) {
      this.setPage(this._page() + 1, search);
    }
  }

  retry(search?: string): void {
    this.load(this._page(), this._size(), search);
  }

  // Every HttpClient request in this app goes through errorMappingInterceptor
  // (app.config.ts), which rethrows a typed AppError in place of the raw
  // HttpErrorResponse before it ever reaches a subscriber - matches the
  // describeError() convention already used in explore.ts/http-lab.ts.
  private describeError(err: unknown): string {
    if (err instanceof AppError) {
      if (err.kind === 'not-found') {
        return 'No quotes found.';
      }
      if (err.kind === 'network') {
        return 'Unable to reach the server. Check your connection and try again.';
      }
    }

    return 'Failed to load quotes.';
  }
}
