import { Component, effect, inject, signal, untracked } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { QuotesStateService } from './quotes-state.service';

@Component({
  selector: 'app-explore',
  imports: [RouterLink],
  templateUrl: './explore.html',
  styleUrl: './explore.css'
})
export class Explore {
  // All list state (quotes/loading/error/page/size/total + derived
  // totalPages/hasPrevious/hasNext/viewState) lives in QuotesStateService -
  // see quotes-state.service.ts. Explore itself only owns the raw search
  // input and asks the service to load pages.
  protected readonly state = inject(QuotesStateService);

  // filter is the raw input value (updates every keystroke, for the box
  // itself); searchTerm is debounced and is what actually drives the query,
  // so the API isn't hit on every keystroke. Search runs server-side
  // (QuotesService.getQuotes, via QuotesStateService) against the whole
  // dataset, not just the currently loaded page - a client-side filter over
  // one page's items would miss a match sitting on a different page.
  protected readonly filter = signal('');
  private readonly searchTerm = toSignal(
    toObservable(this.filter).pipe(debounceTime(300), distinctUntilChanged()),
    { initialValue: '' }
  );

  constructor() {
    // A new search term always starts back at page 1 - the filtered result
    // set is a different size than the unfiltered one, so whatever page the
    // user was on may no longer exist (or may now show unrelated results).
    effect(() => {
      const search = this.searchTerm();
      // size is read via untracked() so this effect only re-runs on a new
      // search term, not on every state change - `size` is stable today
      // (no UI control changes it) but untracked keeps that assumption from
      // silently becoming load-bearing.
      untracked(() => this.state.load(1, this.state.size(), search));
    });
  }

  protected retry(): void {
    this.state.retry(this.searchTerm());
  }

  protected previousPage(): void {
    this.state.previousPage(this.searchTerm());
  }

  protected nextPage(): void {
    this.state.nextPage(this.searchTerm());
  }

  protected wordCount(text: string): number {
    return text.trim().split(/\s+/).filter(Boolean).length;
  }
}
