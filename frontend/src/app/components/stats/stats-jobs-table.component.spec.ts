import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatsJobsTableComponent, sortJobRows } from './stats-jobs-table.component';
import { JobRow, toAdminView, toEmployerView } from '../../utils/stats-view';
import { adminStatsFixture, employerStatsFixture } from '../../../testing/stats-fixtures';

vi.setConfig({ testTimeout: 20000 });

describe('sortJobRows', () => {
  const rows: JobRow[] = [
    { jobId: 1, title: 'Żuraw', company: 'B', statusLabel: 'Aktywna', views: 5, applications: 1, conversion: 0.2 },
    { jobId: 2, title: 'Analityk', company: 'A', statusLabel: 'Szkic', views: 50, applications: 3, conversion: 0.06 },
    { jobId: 3, title: 'Zadaniowiec', company: 'C', statusLabel: 'Aktywna', views: 0, applications: 3, conversion: 0 }
  ];

  it('sorts numbers both ways without mutating the input', () => {
    expect(sortJobRows(rows, 'views', 'desc').map(row => row.jobId)).toEqual([2, 1, 3]);
    expect(sortJobRows(rows, 'views', 'asc').map(row => row.jobId)).toEqual([3, 1, 2]);
    expect(rows.map(row => row.jobId)).toEqual([1, 2, 3]);
  });

  it('sorts Polish text with locale rules (Ż after Z)', () => {
    expect(sortJobRows(rows, 'title', 'asc').map(row => row.title)).toEqual(['Analityk', 'Zadaniowiec', 'Żuraw']);
  });

  it('keeps the original order for ties', () => {
    expect(sortJobRows(rows, 'applications', 'desc').map(row => row.jobId)).toEqual([2, 3, 1]);
  });

  it('copes with an empty list', () => {
    expect(sortJobRows([], 'title', 'asc')).toEqual([]);
  });
});

describe('StatsJobsTableComponent', () => {
  let fixture: ComponentFixture<StatsJobsTableComponent>;

  function create(view: ReturnType<typeof toAdminView> | ReturnType<typeof toEmployerView>): HTMLElement {
    fixture = TestBed.createComponent(StatsJobsTableComponent);
    fixture.componentRef.setInput('title', view.jobsTitle);
    fixture.componentRef.setInput('columns', view.jobColumns);
    fixture.componentRef.setInput('rows', view.jobRows);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function titles(root: HTMLElement): string[] {
    return Array.from(root.querySelectorAll('tbody th')).map(cell => cell.textContent?.trim() ?? '');
  }

  function headerButton(root: HTMLElement, label: string): HTMLButtonElement {
    return Array.from(root.querySelectorAll<HTMLButtonElement>('thead button')).find(button => button.textContent?.includes(label)) as HTMLButtonElement;
  }

  beforeEach(() => TestBed.configureTestingModule({ imports: [StatsJobsTableComponent] }).compileComponents());

  it('renders an accessible table for admin with company column and applications sorted desc by default', () => {
    const root = create(toAdminView(adminStatsFixture()));

    expect(root.querySelector('table caption')?.textContent).toContain('Najpopularniejsze oferty');
    expect(Array.from(root.querySelectorAll('thead th')).map(th => th.textContent?.replace('unfold_more', '').replace('arrow_downward', '').trim()))
      .toEqual(['Oferta', 'Firma', 'Aplikacje', 'Wyświetlenia']);
    expect(titles(root)).toEqual(['Programista .NET', 'Analityk']);
    const applications = root.querySelectorAll('thead th')[2];
    expect(applications.getAttribute('aria-sort')).toBe('descending');
    expect(root.querySelectorAll('thead th')[0].getAttribute('aria-sort')).toBe('none');
  });

  it('shows status labels and conversion for the employer', () => {
    const root = create(toEmployerView(employerStatsFixture()));
    const firstRow = Array.from(root.querySelector('tbody tr')?.children ?? []).map(cell => cell.textContent?.trim());

    expect(firstRow).toEqual(['Tester', 'Aktywna', '50', '5', `${(10).toLocaleString('pl-PL')}%`]);
    expect(Array.from(root.querySelectorAll('tbody tr')).map(row => row.children[1].textContent?.trim()))
      .toEqual(['Aktywna', 'Wygasła', 'Szkic']);
  });

  it('sorts by clicking a header, toggles direction, updates aria-sort and announces the change', () => {
    const root = create(toEmployerView(employerStatsFixture()));

    headerButton(root, 'Wyświetlenia').click();
    fixture.detectChanges();
    expect(titles(root)).toEqual(['Tester', 'Stara oferta', 'Szkic']);
    expect(root.querySelectorAll('thead th')[2].getAttribute('aria-sort')).toBe('descending');
    expect(root.querySelector('[role="status"]')?.textContent).toBe('Posortowano według kolumny Wyświetlenia, malejąco');

    headerButton(root, 'Wyświetlenia').click();
    fixture.detectChanges();
    expect(titles(root)).toEqual(['Szkic', 'Stara oferta', 'Tester']);
    expect(root.querySelectorAll('thead th')[2].getAttribute('aria-sort')).toBe('ascending');

    headerButton(root, 'Oferta').click();
    fixture.detectChanges();
    expect(titles(root)).toEqual(['Stara oferta', 'Szkic', 'Tester']);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('rosnąco');
  });

  it('has keyboard-focusable sort buttons and a focusable scroll region', () => {
    const root = create(toEmployerView(employerStatsFixture()));

    root.querySelectorAll<HTMLButtonElement>('thead button').forEach(button => expect(button.type).toBe('button'));
    expect(root.querySelector('.scroll')?.getAttribute('tabindex')).toBe('0');
  });

  it('shows a message instead of an empty table', () => {
    const root = create(toEmployerView(employerStatsFixture({ jobs: [] })));

    expect(root.querySelector('table')).toBeNull();
    expect(root.textContent).toContain('Brak ofert do wyświetlenia.');
  });
});
