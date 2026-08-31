import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { interval, startWith, switchMap } from 'rxjs';
import { environment } from '../../environments/environment';
import { AppError } from '../http/app-error';
import { CreateJobRequest, JobRecord } from '../job';
import { JobsService } from '../jobs.service';

const MIN_DURATION_SECONDS = 1;
const MAX_DURATION_SECONDS = 20;
const DEFAULT_DURATION_SECONDS = 4;
const MAX_LABEL_LENGTH = 200;

// Polls GET /api/jobs on a fixed interval rather than opening a
// WebSocket/SSE stream - deliberately the smallest thing that shows queued
// -> running -> completed/failed changing over time. Fast enough that a
// 4-second demo job visibly moves through states, without hammering the API.
const POLL_INTERVAL_MS = 1200;

/// <summary>
/// Day 18: Background Jobs page. Enqueues work against QuotesApi's
/// POST /api/jobs and demonstrates - both by the request timing shown after
/// each enqueue and by the Queued/Running rows in the table below - that the
/// HTTP request returns immediately while BackgroundJobWorker does the slow
/// work afterwards. See day-1/QuotesApi/Jobs/ for the server side.
@Component({
  selector: 'app-jobs',
  imports: [FormsModule, DatePipe],
  templateUrl: './jobs.html',
  styleUrl: './jobs.css'
})
export class Jobs {
  private readonly jobsService = inject(JobsService);

  protected readonly minDuration = MIN_DURATION_SECONDS;
  protected readonly maxDuration = MAX_DURATION_SECONDS;
  protected readonly maxLabelLength = MAX_LABEL_LENGTH;
  protected readonly hangfireDashboardUrl = `${environment.apiBaseUrl}/hangfire`;

  protected readonly label = signal('Demo digest');
  protected readonly durationSeconds = signal(DEFAULT_DURATION_SECONDS);
  protected readonly simulateFailure = signal(false);

  protected readonly enqueueing = signal(false);
  protected readonly enqueueError = signal<AppError | null>(null);
  protected readonly lastRequestMs = signal<number | null>(null);
  protected readonly lastEnqueuedId = signal<string | null>(null);

  protected readonly jobs = signal<JobRecord[]>([]);
  protected readonly jobsLoaded = signal(false);

  protected readonly hasActiveJobs = computed(() =>
    this.jobs().some((job) => job.status === 'Queued' || job.status === 'Running')
  );

  constructor() {
    // Runs for as long as this component is alive; takeUntilDestroyed tears
    // the subscription down when the user navigates to another route, so
    // there is no polling loop left running against a page nobody is
    // looking at.
    interval(POLL_INTERVAL_MS)
      .pipe(
        startWith(0),
        switchMap(() => this.jobsService.getRecentJobs()),
        takeUntilDestroyed()
      )
      .subscribe({
        next: (jobs) => {
          this.jobs.set(jobs);
          this.jobsLoaded.set(true);
        }
        // A transient poll failure (backend briefly unreachable) is silently
        // retried on the next tick rather than surfaced as a page-level
        // error - it would otherwise flash an error state on every 1.2s
        // interval blip. enqueue() below has its own explicit error state
        // for the action the user actually took.
      });
  }

  protected fieldInvalid(): boolean {
    return (
      this.durationSeconds() < MIN_DURATION_SECONDS ||
      this.durationSeconds() > MAX_DURATION_SECONDS ||
      this.label().length > MAX_LABEL_LENGTH
    );
  }

  protected enqueue(): void {
    if (this.enqueueing() || this.fieldInvalid()) {
      return;
    }

    this.enqueueing.set(true);
    this.enqueueError.set(null);
    this.lastRequestMs.set(null);
    this.lastEnqueuedId.set(null);

    const request: CreateJobRequest = {
      label: this.label().trim() || undefined,
      durationSeconds: this.durationSeconds(),
      simulateFailure: this.simulateFailure()
    };

    // performance.now() around just the HTTP call - not the job - is the
    // actual evidence that the request returned before the slow work
    // finished: it is always far smaller than durationSeconds.
    const startedAt = performance.now();

    this.jobsService.enqueueJob(request).subscribe({
      next: (job) => {
        this.lastRequestMs.set(Math.round(performance.now() - startedAt));
        this.lastEnqueuedId.set(job.id);
        this.enqueueing.set(false);
        this.jobs.update((current) => [job, ...current.filter((existing) => existing.id !== job.id)].slice(0, 20));
      },
      error: (err: unknown) => {
        this.lastRequestMs.set(Math.round(performance.now() - startedAt));
        this.enqueueing.set(false);
        this.enqueueError.set(this.asAppError(err));
      }
    });
  }

  protected statusClass(status: JobRecord['status']): string {
    switch (status) {
      case 'Queued':
        return 'status-badge status-queued';
      case 'Running':
        return 'status-badge status-running';
      case 'Completed':
        return 'status-badge status-completed';
      case 'Failed':
        return 'status-badge status-failed';
    }
  }

  private asAppError(err: unknown): AppError {
    return err instanceof AppError
      ? err
      : new AppError('unknown', 'Could not reach the server. Check your connection and try again.', 0);
  }
}
