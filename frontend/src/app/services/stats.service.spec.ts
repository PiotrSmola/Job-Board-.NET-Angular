import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { StatsService } from './stats.service';
import { adminStatsFixture, employerStatsFixture } from '../../testing/stats-fixtures';
import { getApiErrorMessage } from '../utils/http-errors';

const RANGE = { from: '2026-10-01', to: '2026-10-31' };

describe('StatsService', () => {
  let service: StatsService;
  let http: HttpTestingController;
  let createObjectURL: ReturnType<typeof vi.fn>;
  let clicked: HTMLAnchorElement[];

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(StatsService);
    http = TestBed.inject(HttpTestingController);

    createObjectURL = vi.fn().mockReturnValue('blob:http://localhost/csv');
    Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() });
    clicked = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push(this);
    });
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('requests admin stats with the range as query params', () => {
    const stats = adminStatsFixture();
    let received: unknown;
    service.getAdminStats(RANGE).subscribe(result => (received = result));

    const req = http.expectOne(r => r.url === '/api/admin/stats');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().sort()).toEqual(['from', 'to']);
    expect(req.request.params.get('from')).toBe('2026-10-01');
    expect(req.request.params.get('to')).toBe('2026-10-31');
    req.flush(stats);
    expect(received).toEqual(stats);
  });

  it('requests employer stats with the range as query params', () => {
    service.getEmployerStats(RANGE).subscribe();

    const req = http.expectOne(r => r.url === '/api/employer/stats');
    expect(req.request.params.get('from')).toBe('2026-10-01');
    expect(req.request.params.get('to')).toBe('2026-10-31');
    req.flush(employerStatsFixture());
  });

  it('cancels the in-flight request on unsubscribe', () => {
    const subscription = service.getAdminStats(RANGE).subscribe();
    const req = http.expectOne(r => r.url === '/api/admin/stats');

    subscription.unsubscribe();

    expect(req.cancelled).toBe(true);
  });

  describe('exportCsv', () => {
    it('downloads the CSV as a blob using the file name from Content-Disposition', () => {
      vi.useFakeTimers();
      let done = false;
      service.exportCsv('admin', RANGE).subscribe(() => (done = true));

      const req = http.expectOne(r => r.url === '/api/admin/stats/export');
      expect(req.request.responseType).toBe('blob');
      expect(req.request.params.get('from')).toBe('2026-10-01');
      expect(req.request.params.get('to')).toBe('2026-10-31');
      const blob = new Blob(['﻿Data;Aplikacje'], { type: 'text/csv' });
      req.flush(blob, { headers: { 'Content-Disposition': "attachment; filename*=UTF-8''statystyki-2026-10-01_2026-10-31.csv" } });

      expect(done).toBe(true);
      expect(createObjectURL).toHaveBeenCalledWith(blob);
      expect(clicked.length).toBe(1);
      expect(clicked[0].download).toBe('statystyki-2026-10-01_2026-10-31.csv');
      vi.runAllTimers();
    });

    it('uses the employer endpoint and falls back to a generated file name', () => {
      vi.useFakeTimers();
      service.exportCsv('employer', RANGE).subscribe();

      const req = http.expectOne(r => r.url === '/api/employer/stats/export');
      req.flush(new Blob(['x']));

      expect(clicked[0].download).toBe('statystyki-2026-10-01_2026-10-31.csv');
      vi.runAllTimers();
    });

    it('parses the JSON error body so the API message can be shown', async () => {
      let error: unknown;
      service.exportCsv('admin', RANGE).subscribe({ error: e => (error = e) });

      const req = http.expectOne(r => r.url === '/api/admin/stats/export');
      req.flush(new Blob([JSON.stringify({ message: 'Date range cannot exceed 366 days' })], { type: 'application/json' }), {
        status: 400,
        statusText: 'Bad Request'
      });
      await vi.waitFor(() => expect(error).toBeInstanceOf(HttpErrorResponse));

      expect(clicked.length).toBe(0);
      expect(getApiErrorMessage(error, 'x')).toBe('Zakres dat nie może przekraczać 366 dni');
    });
  });
});
