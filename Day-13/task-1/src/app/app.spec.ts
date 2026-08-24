import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { App } from './app';

describe('App', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(
      (req) => req.url === 'http://localhost:5062/api/quotes'
    ).flush({ page: 1, size: 10, total: 0, items: [] });
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render title', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(
      (req) => req.url === 'http://localhost:5062/api/quotes'
    ).flush({ page: 1, size: 10, total: 0, items: [] });
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Quotes');
  });

  it('recomputes the rendered list when the filter signal changes', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(
      (req) => req.url === 'http://localhost:5062/api/quotes'
    ).flush({
      page: 1,
      size: 10,
      total: 2,
      items: [
        { id: 1, author: 'Mark Twain', text: 'Get busy living.', isDeleted: false },
        { id: 2, author: 'Maya Angelou', text: 'Still I rise.', isDeleted: false }
      ]
    });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelectorAll('li').length).toBe(2);

    const input = compiled.querySelector('input') as HTMLInputElement;
    input.value = 'twain';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const filtered = compiled.querySelectorAll('li');
    expect(filtered.length).toBe(1);
    expect(filtered[0].textContent).toContain('Mark Twain');
  });

  it('shows the empty state when the filter matches nothing', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(
      (req) => req.url === 'http://localhost:5062/api/quotes'
    ).flush({
      page: 1,
      size: 10,
      total: 1,
      items: [
        { id: 1, author: 'Mark Twain', text: 'Get busy living.', isDeleted: false }
      ]
    });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const input = compiled.querySelector('input') as HTMLInputElement;
    input.value = 'no such author';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(compiled.querySelectorAll('li').length).toBe(0);
    expect(compiled.textContent).toContain('No quotes found.');
  });
});
