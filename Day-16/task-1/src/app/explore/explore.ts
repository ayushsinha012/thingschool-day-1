import { Component, computed, effect, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subscription, debounceTime, distinctUntilChanged } from 'rxjs';
import { AppError } from '../http/app-error';
import { Quote, quoteDetailTransitionName } from '../quote';
import { QuotesService } from '../quotes.service';

@Component({
  selector: 'app-explore',
  imports: [RouterLink],
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
  }

  // Cancels whatever list request was in flight before starting a new one -
  // otherwise a slow response for a page the user has since paged away from
  // could land after (and overwrite) the page currently on screen.
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
        this.error.set(this.describeError(err));
        this.loading.set(false);
      }
    });
  }

  // Every HttpClient request in this app goes through errorMappingInterceptor
  // (app.config.ts), which rethrows a typed AppError in place of the raw
  // HttpErrorResponse before it ever reaches a subscriber - so branching on
  // `err instanceof HttpErrorResponse` here (as this method used to) is
  // always false and silently falls through to a generic message. Matches
  // HttpLab's asAppError() convention instead.
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

  protected retry(): void {
    this.loadQuotes(this.page(), this.size(), this.searchTerm());
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

  protected wordCount(text: string): number {
    return text.trim().split(/\s+/).filter(Boolean).length;
  }
}
