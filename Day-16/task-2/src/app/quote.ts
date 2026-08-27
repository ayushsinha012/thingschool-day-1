export interface Quote {
  id: number;
  author: string;
  text: string;
  isDeleted: boolean;
}

export interface QuotesPage {
  page: number;
  size: number;
  total: number;
  items: Quote[];
}

// QuotesApi's GET /api/quotes/{id} returns the same shape as a list item
// (id, author, text, isDeleted) - it has no display/characterCount fields.
// QuoteDetail is a client-side view model derived from that response, not
// a distinct API contract.
export interface QuoteDetail {
  id: number;
  author: string;
  text: string;
  display: string;
  characterCount: number;
}

export interface CreateQuoteRequest {
  author: string;
  text: string;
}

// Shared by Explore's list cards and QuoteDetailPage so the two sides of a
// navigation are tagged with a matching view-transition-name. Per-id, not a
// shared constant: the View Transitions API requires every
// view-transition-name on screen at once to be unique, and Explore renders
// up to `size` (10) cards simultaneously - a single hardcoded name across all
// of them would collide and make the browser abort the transition entirely
// (falls back to an instant, un-animated navigation) the moment more than
// one quote is in view.
export function quoteDetailTransitionName(id: number): string {
  return `quote-detail-${id}`;
}
