import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { EMPTY, catchError, switchMap, tap } from 'rxjs';
import { AppError } from '../http/app-error';
import { QuoteDetail as QuoteDetailModel } from '../quote';
import { QuotesService } from '../quotes.service';

// Routed page for a single quote (GET /api/quotes/{id}), reached from Explore's
// list via routerLink="/quotes/{{quote.id}}" instead of the old in-page
// selectedId/detail-panel toggle. Reuses QuotesService.getQuoteById exactly as
// Explore already did - no second copy of the fetch/mapping logic.
@Component({
  selector: 'app-quote-detail',
  imports: [RouterLink],
  templateUrl: './quote-detail.html',
  styleUrl: './quote-detail.css'
})
export class QuoteDetailPage {
  private readonly quotesService = inject(QuotesService);

  // Bound from the :id route param via withComponentInputBinding() in
  // app.config.ts. Always a string (or undefined while the route is still
  // resolving) - the API's id field is numeric, so it's parsed below rather
  // than trusted as-is.
  readonly id = input<string>();

  // Route matchers can't constrain :id to digits the way ASP.NET's
  // "{id:int}" does server-side, so a non-numeric or non-positive segment
  // (e.g. /quotes/abc, /quotes/-1, /quotes/1.5) is caught here as its own
  // "invalid" state instead of being sent to the API as a doomed request.
  protected readonly parsedId = computed<number | null>(() => {
    const raw = this.id();

    if (raw === undefined) {
      return null;
    }

    const value = Number(raw);
    return Number.isInteger(value) && value > 0 ? value : null;
  });

  protected readonly detail = signal<QuoteDetailModel | null>(null);
  protected readonly loading = signal(false);
  // Every HttpClient request in this app goes through errorMappingInterceptor
  // (app.config.ts), which rethrows a typed AppError - never the raw
  // HttpErrorResponse - by the time it reaches a subscriber. Kept as the
  // AppError itself (kind + friendlyMessage), matching HttpLab's convention,
  // rather than pre-formatting a string here.
  protected readonly appError = signal<AppError | null>(null);

  protected readonly viewState = computed<'invalid' | 'loading' | 'not-found' | 'error' | 'success'>(() => {
    if (this.parsedId() === null) {
      return 'invalid';
    }

    if (this.loading()) {
      return 'loading';
    }

    const err = this.appError();

    if (err?.kind === 'not-found') {
      return 'not-found';
    }

    if (err) {
      return 'error';
    }

    return 'success';
  });

  constructor() {
    // switchMap cancels an in-flight GET /api/quotes/{id} the moment the id
    // input changes again (e.g. rapid back-to-back navigations between
    // detail pages), the same pattern Explore's old inline detail panel used.
    toObservable(this.parsedId)
      .pipe(
        switchMap((id) => {
          this.appError.set(null);

          if (id === null) {
            this.detail.set(null);
            this.loading.set(false);
            return EMPTY;
          }

          this.loading.set(true);

          return this.quotesService.getQuoteById(id).pipe(
            tap((result) => {
              this.detail.set(result);
              this.loading.set(false);
            }),
            catchError((err: unknown) => {
              this.detail.set(null);
              this.loading.set(false);
              this.appError.set(
                err instanceof AppError ? err : new AppError('unknown', 'Failed to load quote.', 0)
              );
              return EMPTY;
            })
          );
        }),
        takeUntilDestroyed()
      )
      .subscribe();
  }

  protected retry(): void {
    const id = this.parsedId();

    if (id === null) {
      return;
    }

    this.loading.set(true);
    this.appError.set(null);

    this.quotesService.getQuoteById(id).subscribe({
      next: (result) => {
        this.detail.set(result);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.appError.set(
          err instanceof AppError ? err : new AppError('unknown', 'Failed to load quote.', 0)
        );
      }
    });
  }
}
