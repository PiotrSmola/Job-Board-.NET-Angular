import { HttpErrorResponse } from '@angular/common/http';
import { getApiErrorMessage, isHandledByInterceptor } from './http-errors';

const FALLBACK = 'Coś poszło nie tak';

function httpError(status: number, error: unknown = null): HttpErrorResponse {
  return new HttpErrorResponse({ status, statusText: 'Error', url: '/api/x', error });
}

describe('http-errors', () => {
  describe('isHandledByInterceptor', () => {
    it.each([401, 403])('is true for %i', status => {
      expect(isHandledByInterceptor(httpError(status))).toBe(true);
    });

    it.each([0, 400, 404, 500])('is false for %i', status => {
      expect(isHandledByInterceptor(httpError(status))).toBe(false);
    });

    it('is false for non-HTTP errors', () => {
      expect(isHandledByInterceptor(new Error('x'))).toBe(false);
    });
  });

  describe('getApiErrorMessage', () => {
    it.each([
      ['You have already applied to this job', 'Już aplikowałeś na tę ofertę'],
      ['Job offer not found', 'Oferta nie została znaleziona'],
      ['Application not found', 'Aplikacja nie została znaleziona']
    ])('translates the known API message "%s"', (message, expected) => {
      expect(getApiErrorMessage(httpError(400, { message }), FALLBACK)).toBe(expected);
    });

    it('returns an unknown API message from a 4xx response as is', () => {
      expect(getApiErrorMessage(httpError(400, { message: 'Something specific' }), FALLBACK)).toBe('Something specific');
    });

    it('returns a generic text for ValidationProblemDetails', () => {
      const body = { title: 'One or more validation errors occurred.', status: 400, errors: { SalaryMin: ['bad'] } };
      expect(getApiErrorMessage(httpError(400, body), FALLBACK)).toBe('Przesłane dane są nieprawidłowe');
    });

    it('returns a connection message for status 0', () => {
      expect(getApiErrorMessage(httpError(0), FALLBACK)).toBe('Brak połączenia z serwerem');
    });

    it('returns the fallback for server errors even with a message', () => {
      expect(getApiErrorMessage(httpError(500, { message: 'Internal details' }), FALLBACK)).toBe(FALLBACK);
    });

    it('never returns the raw Angular "Http failure response" text', () => {
      const error = httpError(404, 'Not Found');
      expect(error.message).toContain('Http failure response');
      expect(getApiErrorMessage(error, FALLBACK)).toBe(FALLBACK);
    });

    it.each([null, '', { message: '   ' }, { message: 42 }])('returns the fallback for an unusable body %j', body => {
      expect(getApiErrorMessage(httpError(400, body), FALLBACK)).toBe(FALLBACK);
    });

    it('returns the fallback for non-HTTP errors', () => {
      expect(getApiErrorMessage(new Error('boom'), FALLBACK)).toBe(FALLBACK);
    });
  });
});
