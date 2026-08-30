export interface PresignedUploadTarget {
  uploadUrl: string;
  requiredHeaders: Record<string, string>;
}

export type FetchUpload = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

const UPLOAD_ERROR_MESSAGE = 'The file could not be uploaded. Please try again.';

export class PresignedUploadError extends Error {
  readonly status?: number;

  constructor(status?: number) {
    super(UPLOAD_ERROR_MESSAGE);
    this.name = 'PresignedUploadError';
    this.status = status;
  }
}

export async function uploadToPresignedUrl(
  file: File,
  target: PresignedUploadTarget,
  fetchUpload: FetchUpload = fetch
): Promise<void> {
  const entries = Object.entries(target.requiredHeaders);
  if (entries.some(([name]) => name.toLowerCase() === 'authorization')) {
    throw new PresignedUploadError();
  }

  let response: Response;
  try {
    response = await fetchUpload(target.uploadUrl, {
      method: 'PUT',
      headers: new Headers(entries),
      body: file,
      credentials: 'omit'
    });
  } catch {
    throw new PresignedUploadError();
  }

  if (!response.ok) {
    throw new PresignedUploadError(response.status);
  }
}
