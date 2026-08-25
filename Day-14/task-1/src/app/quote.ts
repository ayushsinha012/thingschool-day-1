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
