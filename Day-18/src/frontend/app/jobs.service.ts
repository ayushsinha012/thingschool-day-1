import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { CreateJobRequest, JobRecord } from './job';

// Day 18: talks to QuotesApi's queued-background-job demo
// (POST/GET /api/jobs, see day-1/QuotesApi/Endpoints/JobEndpoints.cs). The
// POST returns as soon as the job is enqueued - the slow work happens later
// on BackgroundJobWorker, off this request entirely.
@Injectable({ providedIn: 'root' })
export class JobsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/jobs`;

  enqueueJob(request: CreateJobRequest): Observable<JobRecord> {
    return this.http.post<JobRecord>(this.baseUrl, request);
  }

  getJob(id: string): Observable<JobRecord> {
    return this.http.get<JobRecord>(`${this.baseUrl}/${id}`);
  }

  // Most recent 20 jobs (server-capped - see JobEndpoints), newest first.
  getRecentJobs(): Observable<JobRecord[]> {
    return this.http.get<JobRecord[]>(this.baseUrl);
  }
}
