import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormField, FormRoot, form, maxLength, required, validate } from '@angular/forms/signals';
import { firstValueFrom } from 'rxjs';
import { CreateQuoteRequest, Quote } from '../quote';
import { QuotesService } from '../quotes.service';

// Mirrors the DataAnnotations on QuotesApi's CreateQuoteRequest
// (Required, StringLength(200/1000, MinimumLength = 1)) - same contract
// as the reactive-forms version in ../create/create.ts.
const AUTHOR_MAX_LENGTH = 200;
const TEXT_MAX_LENGTH = 1000;

interface QuoteFormModel {
  author: string;
  text: string;
}

function isBlank(value: string): boolean {
  return value.length > 0 && value.trim().length === 0;
}

/**
 * Extracts a human-readable message from the API's error response. Ported
 * verbatim from create.ts's extractServerError so the two forms behave
 * identically against a real server error, not just against happy-path
 * client validation.
 */
function extractServerError(err: unknown): string {
  if (!(err instanceof HttpErrorResponse)) {
    return 'Failed to create quote.';
  }

  if (err.status === 0) {
    return 'Could not reach the server. Check your connection and try again.';
  }

  const body = err.error as
    | { errors?: Record<string, string[]>; title?: string; detail?: string }
    | null;

  if (body?.errors && typeof body.errors === 'object') {
    const messages = Object.values(body.errors).flat();

    if (messages.length > 0) {
      return messages.join(' ');
    }
  }

  if (body?.detail) {
    return body.detail;
  }

  if (err.status === 401 || err.status === 403) {
    return 'You are not authorized to create quotes.';
  }

  if (body?.title) {
    return body.title;
  }

  return `Failed to create quote (${err.status}).`;
}

@Component({
  selector: 'app-create-signal',
  imports: [FormField, FormRoot],
  templateUrl: './create-signal.html',
  styleUrl: './create-signal.css'
})
export class CreateSignal {
  private readonly quotesService = inject(QuotesService);

  private readonly model = signal<QuoteFormModel>({ author: '', text: '' });

  protected readonly submitError = signal<string | null>(null);
  protected readonly created = signal<Quote | null>(null);

  protected readonly quoteForm = form(
    this.model,
    (path) => {
      required(path.author, { message: 'Author is required.' });
      validate(path.author, ({ value }) =>
        isBlank(value()) ? { kind: 'blank', message: 'Author cannot be blank.' } : undefined
      );
      maxLength(path.author, AUTHOR_MAX_LENGTH, {
        message: `Author must be ${AUTHOR_MAX_LENGTH} characters or fewer.`
      });

      required(path.text, { message: 'Quote text is required.' });
      validate(path.text, ({ value }) =>
        isBlank(value()) ? { kind: 'blank', message: 'Quote text cannot be blank.' } : undefined
      );
      maxLength(path.text, TEXT_MAX_LENGTH, {
        message: `Quote text must be ${TEXT_MAX_LENGTH} characters or fewer.`
      });
    },
    {
      submission: {
        // Only runs when the form is valid - submit() checks that itself,
        // so there's no `if (this.form.invalid) return` guard to write here.
        action: async (field) => {
          this.submitError.set(null);
          this.created.set(null);

          const value = field().value();
          const request: CreateQuoteRequest = {
            author: value.author.trim(),
            text: value.text.trim()
          };

          try {
            const quote = await firstValueFrom(this.quotesService.createQuote(request));
            this.created.set(quote);
            // model.set() alone only resets the value - the fields would
            // stay touched/dirty from the submission just made, so the
            // now-empty required fields would immediately show "required"
            // errors on a freshly "reset" form. reset(value) clears
            // touched/dirty *and* sets the value in one call - caught by
            // actually looking at the post-success screenshot, where the
            // success panel and two error messages showed at the same time.
            this.quoteForm().reset({ author: '', text: '' });
            this.quoteForm.author().focusBoundControl();
          } catch (err) {
            this.submitError.set(extractServerError(err));
          }

          // No field-level errors to attach - server errors are shown in
          // the top-level panel below, same as the reactive-forms version.
          return undefined;
        },
        // submit() calls this instead of `action` when the form is invalid -
        // this is where focus-first-invalid-field has to be done by hand,
        // same responsibility as reactive forms, just via focusBoundControl()
        // instead of viewChild + ElementRef.
        onInvalid: () => {
          this.submitError.set(null);
          this.created.set(null);
          this.focusFirstInvalidField();
        }
      }
    }
  );

  protected readonly status = computed<'idle' | 'submitting' | 'success' | 'error'>(() => {
    if (this.quoteForm().submitting()) {
      return 'submitting';
    }

    if (this.submitError()) {
      return 'error';
    }

    if (this.created()) {
      return 'success';
    }

    return 'idle';
  });

  protected fieldInvalid(name: 'author' | 'text'): boolean {
    const field = this.quoteForm[name]();
    return field.invalid() && (field.dirty() || field.touched());
  }

  protected fieldErrorMessage(name: 'author' | 'text'): string | null {
    if (!this.fieldInvalid(name)) {
      return null;
    }

    const errors = this.quoteForm[name]().errors();
    return errors[0]?.message ?? null;
  }

  private focusFirstInvalidField(): void {
    if (this.quoteForm.author().invalid()) {
      this.quoteForm.author().focusBoundControl();
      return;
    }

    if (this.quoteForm.text().invalid()) {
      this.quoteForm.text().focusBoundControl();
    }
  }
}
