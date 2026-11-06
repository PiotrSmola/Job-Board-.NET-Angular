import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { parseBlobErrorBody } from './blob-error';

function blobError(status: number, body: unknown): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    statusText: 'Error',
    url: '/api/jobapplications/1/cv',
    headers: new HttpHeaders({ 'X-Test': '1' }),
    error: body
  });
}

describe('parseBlobErrorBody', () => {
  it('replaces a JSON Blob body with the parsed object and keeps status, URL and headers', async () => {
    const error = blobError(404, new Blob([JSON.stringify({ message: 'Application not found' })], { type: 'application/json' }));

    const parsed = await parseBlobErrorBody(error);

    expect(parsed).not.toBe(error);
    expect(parsed.error).toEqual({ message: 'Application not found' });
    expect(parsed.status).toBe(404);
    expect(parsed.url).toBe('/api/jobapplications/1/cv');
    expect(parsed.headers.get('X-Test')).toBe('1');
  });

  it('returns null as the body for an empty Blob', async () => {
    const parsed = await parseBlobErrorBody(blobError(404, new Blob([])));

    expect(parsed.error).toBeNull();
    expect(parsed.status).toBe(404);
  });

  it('returns the original error when the Blob is not JSON', async () => {
    const error = blobError(500, new Blob(['<html>Server error</html>'], { type: 'text/html' }));

    expect(await parseBlobErrorBody(error)).toBe(error);
  });

  it('returns the original error when the body is not a Blob', async () => {
    const error = blobError(400, { message: 'File is empty' });

    expect(await parseBlobErrorBody(error)).toBe(error);
  });
});
