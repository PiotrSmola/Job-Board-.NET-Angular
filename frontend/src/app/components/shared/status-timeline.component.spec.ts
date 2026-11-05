import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatusTimelineComponent } from './status-timeline.component';
import { ApplicationStatus, ApplicationStatusHistoryEntry } from '../../models/job-application.model';

function entry(
  status: ApplicationStatus,
  changedAt: string,
  comment: string | null = null,
  changedBy: string | null = null
): ApplicationStatusHistoryEntry {
  return { status, changedAt, comment, changedBy };
}

describe('StatusTimelineComponent', () => {
  let fixture: ComponentFixture<StatusTimelineComponent>;
  let component: StatusTimelineComponent;

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function show(history: ApplicationStatusHistoryEntry[] | null | undefined): void {
    fixture.componentRef.setInput('history', history);
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [StatusTimelineComponent] }).compileComponents();
    fixture = TestBed.createComponent(StatusTimelineComponent);
    component = fixture.componentInstance;
  });

  it.each([[[]], [null], [undefined]])('shows "Brak historii" for %j', history => {
    show(history);

    expect(root().querySelector('.empty')?.textContent).toContain('Brak historii');
    expect(root().querySelector('ol')).toBeNull();
  });

  it('renders one item per entry in the given (chronological) order', () => {
    show([
      entry(ApplicationStatus.Submitted, '2026-01-01T10:00:00Z'),
      entry(ApplicationStatus.InReview, '2026-01-02T10:00:00Z'),
      entry(ApplicationStatus.Interview, '2026-01-03T10:00:00Z')
    ]);

    const labels = Array.from(root().querySelectorAll('li app-status-chip')).map(e => e.textContent?.trim());
    expect(labels).toEqual(['Złożona', 'W analizie', 'Rozmowa']);
    expect(root().querySelector('.empty')).toBeNull();
  });

  it('uses the tone of each status', () => {
    show([entry(ApplicationStatus.Hired, '2026-01-01T10:00:00Z'), entry(ApplicationStatus.Rejected, '2026-01-02T10:00:00Z')]);

    const tones = Array.from(root().querySelectorAll('li .status-chip')).map(e => e.className);
    expect(tones[0]).toContain('tone-success');
    expect(tones[1]).toContain('tone-danger');
  });

  it('formats the date as dd.MM.yyyy HH:mm in local time', () => {
    const changedAt = new Date(2026, 2, 5, 9, 7, 0).toISOString();
    show([entry(ApplicationStatus.Submitted, changedAt)]);

    expect(root().querySelector('time')?.textContent?.trim()).toBe('05.03.2026 09:07');
  });

  it('shows the comment and the author only when present', () => {
    show([
      entry(ApplicationStatus.Submitted, '2026-01-01T10:00:00Z'),
      entry(ApplicationStatus.Interview, '2026-01-02T10:00:00Z', 'Zapraszamy w czwartek', 'admin@jobboard.com')
    ]);

    const items = root().querySelectorAll('li');
    expect(items[0].querySelector('.comment')).toBeNull();
    expect(items[0].querySelector('.author')).toBeNull();
    expect(items[1].querySelector('.comment')?.textContent).toContain('Zapraszamy w czwartek');
    expect(items[1].querySelector('.author')?.textContent).toContain('admin@jobboard.com');
  });

  it('does not render an author for the candidate view (changedBy = null)', () => {
    show([entry(ApplicationStatus.Interview, '2026-01-02T10:00:00Z', 'Komentarz', null)]);

    expect(root().querySelector('.author')).toBeNull();
  });

  it('renders the comment as text, not HTML', () => {
    show([entry(ApplicationStatus.Submitted, '2026-01-01T10:00:00Z', '<b>pogrubione</b>')]);

    expect(root().querySelector('.comment b')).toBeNull();
    expect(root().querySelector('.comment')?.textContent).toContain('<b>pogrubione</b>');
  });

  describe('notes', () => {
    it('marks an entry with the same status as the previous one as a note', () => {
      show([
        entry(ApplicationStatus.Submitted, '2026-01-01T10:00:00Z'),
        entry(ApplicationStatus.InReview, '2026-01-02T10:00:00Z'),
        entry(ApplicationStatus.InReview, '2026-01-03T10:00:00Z', 'Dodatkowa notatka')
      ]);

      expect(component.entries().map(e => e.isNote)).toEqual([false, false, true]);
      const kinds = Array.from(root().querySelectorAll('li')).map(li => li.querySelector('.kind') !== null);
      expect(kinds).toEqual([false, false, true]);
      expect(root().querySelector('.kind')?.textContent).toContain('notatka');
    });

    it('does not mark the first entry as a note', () => {
      show([entry(ApplicationStatus.Submitted, '2026-01-01T10:00:00Z')]);

      expect(component.entries()[0].isNote).toBe(false);
    });

    it('does not mark a re-submission after withdrawal as a note', () => {
      show([
        entry(ApplicationStatus.Submitted, '2026-01-01T10:00:00Z'),
        entry(ApplicationStatus.Withdrawn, '2026-01-02T10:00:00Z'),
        entry(ApplicationStatus.Submitted, '2026-01-03T10:00:00Z')
      ]);

      expect(component.entries().map(e => e.isNote)).toEqual([false, false, false]);
    });
  });

  it('falls back to an "unknown" label for a status the client does not know', () => {
    show([entry(99 as ApplicationStatus, '2026-01-01T10:00:00Z')]);

    expect(root().querySelector('li app-status-chip')?.textContent?.trim()).toBe('Nieznany');
  });
});
