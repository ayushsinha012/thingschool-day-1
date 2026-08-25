export interface Quote {
  id: number;
  author: string;
  text: string;
  isDeleted: boolean;
}

export interface CreateQuoteRequest {
  author: string;
  text: string;
}
