// Mirrors QuotesApi's Jobs.JobStatus (serialized as its name via
// JsonStringEnumConverter - see JobStatus.cs) and Jobs.JobRecord.
export type JobStatusName = 'Queued' | 'Running' | 'Completed' | 'Failed';

export interface JobRecord {
  id: string;
  label: string;
  status: JobStatusName;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  error: string | null;
}

export interface CreateJobRequest {
  label?: string;
  durationSeconds: number;
  simulateFailure: boolean;
}
