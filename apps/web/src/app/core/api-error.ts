import { HttpErrorResponse } from '@angular/common/http';
import { ApiErrorBody } from './models';

export function apiErrorMessage(error: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (!(error instanceof HttpErrorResponse)) return fallback;
  if (error.status === 0) return 'The server is not responding. Check your connection and try again.';
  if (error.status === 401) return 'Your session has expired. Please sign in again.';
  if (error.status === 403) return 'You do not have permission to do that.';
  if (error.status === 413) return 'That upload is too large.';

  const body = error.error as ApiErrorBody | string | null;
  if (typeof body === 'string') return body.trim() || fallback;
  if (!body) return fallback;
  if (body.detail) return body.detail;
  if (body.message) return body.message;
  if (body.title && error.status < 500) return body.title;
  if (body.errors) {
    const first = Object.values(body.errors).flat()[0];
    if (typeof first === 'string' && first) return first;
  }
  return fallback;
}
