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

export interface QuoteDetail {
  id: number;
  author: string;
  text: string;
  display: string;
  characterCount: number;
}
