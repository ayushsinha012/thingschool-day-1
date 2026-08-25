import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators
} from '@angular/forms';
import { CreateQuoteRequest, Quote } from './quote';
import { QuotesService } from './quotes.service';

// Mirrors the DataAnnotations on QuotesApi's CreateQuoteRequest
// (Required, StringLength(200/1000, MinimumLength = 1)).
const AUTHOR_MAX_LENGTH = 200;
const TEXT_MAX_LENGTH = 1000;

function notBlank(control: AbstractControl): ValidationErrors | null {
  const value = control.value;

  return typeof value === 'string' && value.length > 0 && value.trim().length === 0
    ? { blank: true }
    : null;
}

type FieldName = 'author' | 'text';

type QuoteFormControls = {
  author: FormControl<string>;
  text: FormControl<string>;
};

/**
 * Extracts a human-readable message from the API's error response,
 * covering the shapes QuoteEndpoints actually returns: a ValidationProblem
 * ({ errors: { Field: [message] } }) from ValidationExtensions.Validate,
 * or a ProblemDetails ({ title, detail }) from the ArgumentException catch.
 */
function extractServerError(err: HttpErrorResponse): string {
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
  selector: 'app-root',
  imports: [ReactiveFormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  private readonly quotesService = inject(QuotesService);
  private readonly formBuilder = inject(FormBuilder);

  private readonly authorInput = viewChild<ElementRef<HTMLInputElement>>('authorInput');
  private readonly textInput = viewChild<ElementRef<HTMLTextAreaElement>>('textInput');

  protected readonly authorMaxLength = AUTHOR_MAX_LENGTH;
  protected readonly textMaxLength = TEXT_MAX_LENGTH;

  protected readonly form: FormGroup<QuoteFormControls> = this.formBuilder.nonNullable.group({
    author: ['', [Validators.required, notBlank, Validators.maxLength(AUTHOR_MAX_LENGTH)]],
    text: ['', [Validators.required, notBlank, Validators.maxLength(TEXT_MAX_LENGTH)]]
  });

  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  protected readonly created = signal<Quote | null>(null);

  protected readonly status = computed<'idle' | 'submitting' | 'success' | 'error'>(() => {
    if (this.submitting()) {
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

  protected fieldInvalid(name: FieldName): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.dirty || control.touched);
  }

  protected fieldError(name: FieldName): string | null {
    if (!this.fieldInvalid(name)) {
      return null;
    }

    const control = this.form.controls[name];
    const label = name === 'author' ? 'Author' : 'Quote text';
    const limit = name === 'author' ? AUTHOR_MAX_LENGTH : TEXT_MAX_LENGTH;

    if (control.hasError('required')) {
      return `${label} is required.`;
    }

    if (control.hasError('blank')) {
      return `${label} cannot be blank.`;
    }

    if (control.hasError('maxlength')) {
      return `${label} must be ${limit} characters or fewer.`;
    }

    return null;
  }

  protected submit(): void {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalidField();
      return;
    }

    this.submitting.set(true);
    this.submitError.set(null);
    this.created.set(null);

    const { author, text } = this.form.getRawValue();
    const request: CreateQuoteRequest = { author: author.trim(), text: text.trim() };

    this.quotesService.createQuote(request).subscribe({
      next: (quote) => {
        this.submitting.set(false);
        this.created.set(quote);
        this.form.reset();
        this.authorInput()?.nativeElement.focus();
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.submitError.set(extractServerError(err));
      }
    });
  }

  private focusFirstInvalidField(): void {
    if (this.form.controls.author.invalid) {
      this.authorInput()?.nativeElement.focus();
      return;
    }

    if (this.form.controls.text.invalid) {
      this.textInput()?.nativeElement.focus();
    }
  }
}
