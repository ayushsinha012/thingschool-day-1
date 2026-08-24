import { Component, computed, effect, inject, signal } from '@angular/core';
import { Quote } from './quote';
import { QuotesService } from './quotes.service';

@Component({
  selector: 'app-root',
  imports: [],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
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

  protected readonly filter = signal('');
  protected readonly filteredQuotes = computed(() => {
    const term = this.filter().trim().toLowerCase();

    if (!term) {
      return this.quotes();
    }

    return this.quotes().filter(
      (quote) =>
        quote.author.toLowerCase().includes(term) ||
        quote.text.toLowerCase().includes(term)
    );
  });

  protected readonly viewState = computed<'loading' | 'error' | 'empty' | 'success'>(() => {
    if (this.loading()) {
      return 'loading';
    }

    if (this.error()) {
      return 'error';
    }

    if (this.filteredQuotes().length === 0) {
      return 'empty';
    }

    return 'success';
  });

  constructor() {
    effect(() => {
      const page = this.page();
      const size = this.size();

      this.loadQuotes(page, size);
    });
  }

  private loadQuotes(page: number, size: number): void {
    this.loading.set(true);
    this.error.set(null);

    this.quotesService.getQuotes(page, size).subscribe({
      next: (result) => {
        this.quotes.set(result.items);
        this.total.set(result.total);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load quotes.');
        this.loading.set(false);
      }
    });
  }

  protected retry(): void {
    this.loadQuotes(this.page(), this.size());
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
