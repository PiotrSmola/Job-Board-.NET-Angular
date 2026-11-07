import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { Observable, Subject, of, throwError } from 'rxjs';
import { MyApplicationsComponent, formatStatusSince, isClosedApplication } from './my-applications.component';
import { ATTACHMENTS_UPLOAD_ABORTED, AttachmentsDialogComponent } from './attachments-dialog.component';
import { JobApplicationService } from '../../services/job-application.service';
import { ConfirmService } from '../../services/confirm.service';
import { ApplicationAttachment, ApplicationStatus, AttachmentKind, JobApplication } from '../../models/job-application.model';

function makeApplication(id: number, status = ApplicationStatus.Submitted): JobApplication {
  return {
    id, jobOfferId: id * 10, userId: 'u1', userEmail: 'jan@example.com', userFullName: 'Jan Kowalski',
    jobTitle: `Oferta ${id}`, company: 'Acme', companyId: 1, note: null, appliedAt: new Date('2026-01-01T10:00:00Z'),
    status, statusChangedAt: '2026-01-01T10:00:00Z',
    attachments: [], attachmentsTotalSize: 0
  };
}

const HISTORY = [
  { status: ApplicationStatus.Submitted, changedAt: '2026-01-01T10:00:00Z', comment: null, changedBy: null },
  { status: ApplicationStatus.Interview, changedAt: '2026-01-02T10:00:00Z', comment: 'Zapraszamy', changedBy: null }
];

const ALL_STATUSES = [
  ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Offered,
  ApplicationStatus.Hired, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn
];

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('MyApplicationsComponent', () => {
  let fixture: ComponentFixture<MyApplicationsComponent>;
  let component: MyApplicationsComponent;
  let getMyApplications: ReturnType<typeof vi.fn>;
  let getApplication: ReturnType<typeof vi.fn>;
  let withdraw: ReturnType<typeof vi.fn>;
  let downloadAttachment: ReturnType<typeof vi.fn>;
  let deleteAttachment: ReturnType<typeof vi.fn>;
  let dialogOpen: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let confirm: ReturnType<typeof vi.fn>;

  function create(initial: Observable<JobApplication[]> = of([makeApplication(1), makeApplication(2)])): void {
    getMyApplications.mockReturnValue(initial);
    fixture = TestBed.createComponent(MyApplicationsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function cards(): HTMLElement[] {
    return Array.from(root().querySelectorAll<HTMLElement>('article.application'));
  }

  function card(index: number): HTMLElement {
    return cards()[index];
  }

  function toggleButton(index: number): HTMLButtonElement {
    return card(index).querySelector<HTMLButtonElement>('button.toggle')!;
  }

  function expand(index: number): HTMLElement {
    toggleButton(index).click();
    return card(index);
  }

  function tabLabels(): string[] {
    return Array.from(root().querySelectorAll('[role="tab"]')).map(t => t.textContent?.trim() ?? '');
  }

  function selectTab(index: number): void {
    root().querySelectorAll<HTMLElement>('[role="tab"]')[index].click();
    root();
  }

  function byId(id: number): JobApplication {
    return component.applications().find(a => a.id === id)!;
  }

  beforeEach(async () => {
    getMyApplications = vi.fn();
    getApplication = vi.fn().mockReturnValue(of({ ...makeApplication(1, ApplicationStatus.Interview), history: HISTORY }));
    withdraw = vi.fn().mockImplementation((id: number) => of({
      ...makeApplication(id, ApplicationStatus.Withdrawn),
      history: [...HISTORY, { status: ApplicationStatus.Withdrawn, changedAt: '2026-01-03T10:00:00Z', comment: null, changedBy: null }]
    }));
    downloadAttachment = vi.fn().mockReturnValue(of(undefined));
    deleteAttachment = vi.fn().mockReturnValue(of(undefined));
    dialogOpen = vi.fn().mockReturnValue({ afterClosed: () => of(undefined) });
    snackBarOpen = vi.fn();
    confirm = vi.fn().mockReturnValue(of(true));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [MyApplicationsComponent],
      providers: [
        provideRouter([]),
        { provide: JobApplicationService, useValue: { getMyApplications, getApplication, withdraw, downloadAttachment, deleteAttachment } },
        { provide: MatDialog, useValue: { open: dialogOpen } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } },
        { provide: ConfirmService, useValue: { confirm } },
        // przełączanie zakładek bez animacji – treść podmieniana od razu
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  describe('list', () => {
    it('loads and shows the current user applications as cards with a heading link', () => {
      create();

      expect(component.applications().map(a => a.id)).toEqual([1, 2]);
      expect(component.state()).toBe('loaded');
      expect(root().querySelector('h1')?.textContent?.trim()).toBe('Moje aplikacje');
      const link = card(0).querySelector<HTMLAnchorElement>('h2 a')!;
      expect(link.textContent?.trim()).toBe('Oferta 1');
      expect(link.getAttribute('href')).toBe('/jobs/10');
      expect(card(0).getAttribute('aria-labelledby')).toBe('application-title-1');
    });

    it('shows the company, the location and since when the status holds', () => {
      const app = { ...makeApplication(1), jobOffer: { location: 'Rzeszów' } as JobApplication['jobOffer'] };
      create(of([app]));

      expect(card(0).querySelector('.secondary')?.textContent?.trim()).toBe('Acme · Rzeszów');
      expect(card(0).querySelector('.since')?.textContent?.trim()).toBe('od ' + formatStatusSince('2026-01-01T10:00:00Z'));
    });

    it('reserves space with a skeleton while loading', () => {
      create(new Subject<JobApplication[]>());

      expect(root().querySelectorAll('.skeleton').length).toBe(3);
      expect(root().querySelector('[role="status"]')?.textContent).toContain('Wczytywanie aplikacji');
    });

    it('shows an inline error with a retry button when loading fails', () => {
      create(throwError(() => new HttpErrorResponse({ status: 500 })));

      const alert = root().querySelector('[role="alert"]')!;
      expect(alert.textContent).toContain('Nie udało się wczytać aplikacji – spróbuj ponownie');
      expect(snackBarOpen).not.toHaveBeenCalled();

      getMyApplications.mockReturnValue(of([makeApplication(1)]));
      alert.querySelector<HTMLButtonElement>('button')!.click();
      expect(cards().length).toBe(1);
    });

    it('shows an empty state with a link to the offers', () => {
      create(of([]));

      expect(root().textContent).toContain('Nie masz jeszcze żadnych aplikacji.');
      expect(root().querySelector('a[href="/jobs"]')).not.toBeNull();
    });

    it('shows the Polish status label of each application', () => {
      create(of([makeApplication(1, ApplicationStatus.Interview), makeApplication(2, ApplicationStatus.InReview)]));

      const labels = Array.from(root().querySelectorAll('app-status-chip')).map(e => e.textContent?.trim());
      expect(labels).toEqual(['Rozmowa', 'W analizie']);
    });
  });

  describe('tabs', () => {
    it('splits the applications into "W toku" and "Zakończone" by status', () => {
      create(of(ALL_STATUSES.map((status, i) => makeApplication(i + 1, status))));

      expect(ALL_STATUSES.map(isClosedApplication)).toEqual([false, false, false, false, true, true, true]);
      expect(tabLabels()).toEqual(['W toku (4)', 'Zakończone (3)']);
      expect(cards().map(c => c.querySelector('h2')?.textContent?.trim())).toEqual(['Oferta 1', 'Oferta 2', 'Oferta 3', 'Oferta 4']);

      selectTab(1);

      expect(cards().map(c => c.querySelector('h2')?.textContent?.trim())).toEqual(['Oferta 5', 'Oferta 6', 'Oferta 7']);
    });

    it('opens "Zakończone" when nothing is in progress', () => {
      create(of([makeApplication(1, ApplicationStatus.Rejected)]));

      expect(component.selectedTab()).toBe(1);
      expect(cards().length).toBe(1);
    });

    it('shows an empty message in an empty tab', () => {
      create(of([makeApplication(1)]));

      selectTab(1);

      expect(root().textContent).toContain('Nie masz zakończonych aplikacji.');
    });
  });

  describe('details', () => {
    it('is a disclosure button with aria-expanded and aria-controls', () => {
      create();
      const button = toggleButton(0);

      expect(button.getAttribute('aria-label')).toBe('Szczegóły aplikacji: Oferta 1');
      expect(button.getAttribute('aria-expanded')).toBe('false');
      const panel = root().querySelector<HTMLElement>('#' + button.getAttribute('aria-controls'))!;
      expect(panel.hidden).toBe(true);

      button.click();
      root();

      expect(button.getAttribute('aria-expanded')).toBe('true');
      expect(panel.hidden).toBe(false);
    });

    it('loads the history once and toggles the timeline', () => {
      create();

      expand(0);

      expect(getApplication).toHaveBeenCalledWith(1);
      expect(card(0).querySelectorAll('app-status-timeline li').length).toBe(2);
      expect(card(0).querySelector('app-status-timeline')?.textContent).toContain('Zapraszamy');

      toggleButton(0).click();
      expect(card(0).querySelector('app-status-timeline')).toBeNull();

      toggleButton(0).click();
      expect(getApplication).toHaveBeenCalledTimes(1);
    });

    it('shows the full message of the application as text (no tooltip, no truncation)', () => {
      const note = 'Dzień dobry,\n' + 'bardzo chętnie '.repeat(10) + '<b>dołączę</b>';
      create(of([{ ...makeApplication(1), note }]));

      const details = expand(0);

      expect(details.querySelector('.note')?.textContent).toBe(note);
      expect(details.querySelector('.note b')).toBeNull();
      expect(root().querySelector('[mattooltip]')).toBeNull();
    });

    it('collapses and shows a snackbar when the history cannot be loaded', () => {
      create();
      getApplication.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      expand(0);

      expect(component.isExpanded(1)).toBe(false);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Nie udało się wczytać historii aplikacji');
    });

    it('keeps several rows expanded and loads each history independently', () => {
      create();
      const first = new Subject<JobApplication>();
      const second = new Subject<JobApplication>();
      getApplication.mockImplementation((id: number) => id === 1 ? first : second);

      expand(0);
      expand(1);
      first.next({ ...makeApplication(1), history: HISTORY });

      expect(component.isExpanded(1) && component.isExpanded(2)).toBe(true);
      expect(component.historyLoadingIds().has(2)).toBe(true);
      expect(card(1).querySelector('.history-loading [role="status"], .history-loading')).not.toBeNull();

      second.error(new HttpErrorResponse({ status: 500 }));
      root();

      expect(component.isExpanded(1)).toBe(true);
      expect(component.isExpanded(2)).toBe(false);
      expect(card(0).querySelectorAll('app-status-timeline li').length).toBe(2);
    });
  });

  describe('withdrawing', () => {
    it('asks through ConfirmService, withdraws and moves the application to "Zakończone"', () => {
      create();
      expand(0).querySelector<HTMLButtonElement>('button.withdraw')!.click();

      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({
        title: 'Wycofać aplikację?', confirmLabel: 'Wycofaj aplikację', destructive: true
      }));
      expect(confirm.mock.calls[0][0].message).toContain('„Oferta 1”');
      expect(withdraw).toHaveBeenCalledWith(1);
      expect(byId(1).status).toBe(ApplicationStatus.Withdrawn);
      expect(component.withdrawingId()).toBeNull();
      expect(snackBarOpen).toHaveBeenCalledWith(
        'Aplikacja została wycofana – znajdziesz ją w zakładce „Zakończone”', 'OK', { duration: 5000, politeness: 'polite' });
      expect(tabLabels()).toEqual(['W toku (1)', 'Zakończone (1)']);
      // karta zniknęła z bieżącej zakładki – fokus na nagłówku strony
      expect(document.activeElement).toBe(root().querySelector('h1'));
    });

    it('does not withdraw when the confirmation is declined', () => {
      create();
      confirm.mockReturnValue(of(false));

      component.withdrawApplication(byId(1));

      expect(withdraw).not.toHaveBeenCalled();
      expect(byId(1).status).toBe(ApplicationStatus.Submitted);
    });

    it('offers withdrawal only for statuses the API allows', () => {
      create(of(ALL_STATUSES.map((status, i) => makeApplication(i + 1, status))));

      expect(component.applications().map(a => component.canWithdraw(a))).toEqual([true, true, true, true, false, false, false]);
      for (let i = 0; i < 4; i++) expand(i);
      expect(root().querySelectorAll('button.withdraw').length).toBe(4);

      selectTab(1);
      for (let i = 0; i < 3; i++) expand(i);
      expect(root().querySelectorAll('button.withdraw').length).toBe(0);
    });

    it('keeps the button focusable (aria-disabled) while the request is pending', () => {
      create();
      withdraw.mockReturnValue(new Subject<JobApplication>());
      const button = expand(0).querySelector<HTMLButtonElement>('button.withdraw')!;

      button.click();
      root();

      expect(button.disabled).toBe(false);
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(button.textContent).toContain('Wycofywanie…');

      component.withdrawApplication(byId(2));
      expect(confirm).toHaveBeenCalledTimes(1);
    });

    it('keeps the status and shows the translated API message when withdrawal is rejected', () => {
      create();
      withdraw.mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 400, error: { message: 'Application cannot be withdrawn in its current status' }
      })));

      component.withdrawApplication(byId(1));

      expect(byId(1).status).toBe(ApplicationStatus.Submitted);
      expect(component.withdrawingId()).toBeNull();
      expect(snackBarOpen.mock.calls[0][0]).toBe('Tej aplikacji nie można już wycofać');
    });

    it('shows a generic message when withdrawal fails on the server', () => {
      create();
      withdraw.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      component.withdrawApplication(byId(1));

      expect(snackBarOpen.mock.calls[0][0]).toBe('Nie udało się wycofać aplikacji');
    });

    it.each([401, 403])('does not duplicate the interceptor notice when withdrawal fails with %i', status => {
      create();
      withdraw.mockReturnValue(throwError(() => new HttpErrorResponse({ status, statusText: 'Error' })));

      component.withdrawApplication(byId(1));

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(component.withdrawingId()).toBeNull();
    });

    it('reloads the cached history of an expanded row after withdrawal without history in the response', () => {
      create();
      expand(0);
      expect(component.histories()[1].length).toBe(2);
      withdraw.mockReturnValue(of(makeApplication(1, ApplicationStatus.Withdrawn)));
      const withWithdrawal = [...HISTORY, { status: ApplicationStatus.Withdrawn, changedAt: '2026-01-03T10:00:00Z', comment: null, changedBy: null }];
      getApplication.mockReturnValue(of({ ...makeApplication(1, ApplicationStatus.Withdrawn), history: withWithdrawal }));

      component.withdrawApplication(byId(1));

      expect(getApplication).toHaveBeenCalledTimes(2);
      expect(component.histories()[1].length).toBe(3);
    });

    it('drops the cached history of a collapsed row after withdrawal without history in the response', () => {
      create();
      expand(0);
      toggleButton(0).click();
      withdraw.mockReturnValue(of(makeApplication(1, ApplicationStatus.Withdrawn)));

      component.withdrawApplication(byId(1));

      expect(component.histories()[1]).toBeUndefined();
      expect(getApplication).toHaveBeenCalledTimes(1);
      component.toggleDetails(byId(1));
      expect(getApplication).toHaveBeenCalledTimes(2);
    });

    it('uses the history returned by withdraw without another request', () => {
      create();
      component.withdrawApplication(byId(1));

      component.toggleDetails(byId(1));

      expect(getApplication).not.toHaveBeenCalled();
      expect(component.histories()[1].length).toBe(3);
      expect(byId(1).history).toBeUndefined();
    });
  });

  describe('attachments', () => {
    const CV: ApplicationAttachment = { id: 11, kind: AttachmentKind.Cv, fileName: 'moje-cv.pdf', size: 350 * 1024, uploadedAt: '2026-01-02T10:00:00Z' };
    const LETTER: ApplicationAttachment = { id: 12, kind: AttachmentKind.CoverLetter, fileName: 'list.docx', size: 2048, uploadedAt: '2026-01-03T10:00:00Z' };

    function withAttachments(id: number, status = ApplicationStatus.Submitted, attachments = [CV, LETTER]): JobApplication {
      return {
        ...makeApplication(id, status),
        attachments,
        attachmentsTotalSize: attachments.reduce((sum, a) => sum + a.size, 0)
      };
    }

    function files(index: number): HTMLElement {
      return card(index).querySelector<HTMLElement>('app-attachment-list')!;
    }

    it('lists the kind, name, size and date of every attachment in the details', () => {
      create(of([withAttachments(1), makeApplication(2)]));
      expand(0);
      expand(1);

      const items = Array.from(files(0).querySelectorAll('li'));
      expect(items.map(li => li.querySelector('.file-name')?.textContent)).toEqual(['moje-cv.pdf', 'list.docx']);
      expect(items.map(li => li.querySelector('.meta')?.textContent)).toEqual([
        'CV · 350 KB · 02.01.2026', 'List motywacyjny · 2 KB · 03.01.2026'
      ]);
      expect(files(1).querySelector('.empty')?.textContent).toBe('Brak załączników');
    });

    it('labels the buttons with the action, the kind and the file name', () => {
      create(of([withAttachments(1)]));
      const details = expand(0);

      expect(Array.from(files(0).querySelectorAll('button')).map(b => b.getAttribute('aria-label'))).toEqual([
        'Pobierz CV: moje-cv.pdf', 'Usuń CV: moje-cv.pdf',
        'Pobierz List motywacyjny: list.docx', 'Usuń List motywacyjny: list.docx'
      ]);
      // bez spacji przed dwukropkiem (ukryty dopisek w osobnym elemencie dawał „Dodaj załączniki : …”)
      expect(details.querySelector('button.add-attachments')?.getAttribute('aria-label')).toBe('Dodaj załączniki: Oferta 1');
      expect(details.querySelector('button.withdraw')?.getAttribute('aria-label')).toBe('Wycofaj aplikację: Oferta 1');
    });

    it('offers adding only in the statuses the API allows, but deleting and downloading always', () => {
      create(of(ALL_STATUSES.map((status, i) => withAttachments(i + 1, status, [CV]))));
      for (let i = 0; i < 4; i++) expand(i);

      expect(cards().map(c => c.querySelectorAll('button.add-attachments').length)).toEqual([1, 1, 1, 1]);
      expect(cards().map(c => c.querySelectorAll('.download-button, .delete-button').length)).toEqual([2, 2, 2, 2]);

      selectTab(1);
      for (let i = 0; i < 3; i++) expand(i);
      expect(cards().map(c => c.querySelectorAll('button.add-attachments').length)).toEqual([0, 0, 0]);
      expect(cards().map(c => c.querySelectorAll('.download-button, .delete-button').length)).toEqual([2, 2, 2]);
    });

    it('does not offer adding when the application already has 10 attachments', () => {
      const ten = Array.from({ length: 10 }, (_, i) => ({ ...CV, id: 100 + i, fileName: `plik-${i}.pdf` }));
      create(of([withAttachments(1, ApplicationStatus.Submitted, ten)]));

      expect(component.canAddAttachments(byId(1))).toBe(false);
      expect(expand(0).querySelector('button.add-attachments')).toBeNull();
    });

    it('downloads an attachment of the right application', () => {
      create(of([withAttachments(1)]));
      expand(0);

      files(0).querySelectorAll<HTMLButtonElement>('.download-button')[1].click();

      expect(downloadAttachment).toHaveBeenCalledWith(1, LETTER);
    });

    it('deletes an attachment and updates the list and the total size', () => {
      create(of([withAttachments(1)]));
      expand(0);

      files(0).querySelectorAll<HTMLButtonElement>('.delete-button')[0].click();

      expect(deleteAttachment).toHaveBeenCalledWith(1, 11);
      expect(byId(1).attachments).toEqual([LETTER]);
      expect(byId(1).attachmentsTotalSize).toBe(2048);
      expect(snackBarOpen).toHaveBeenCalledWith('Załącznik został usunięty', 'OK', { duration: 5000, politeness: 'polite' });
      expect(Array.from(files(0).querySelectorAll('.file-name')).map(e => e.textContent)).toEqual(['list.docx']);
    });

    it('allows deleting an attachment of a withdrawn application (right to erasure)', () => {
      create(of([withAttachments(1, ApplicationStatus.Withdrawn, [CV])]));
      expand(0);

      files(0).querySelector<HTMLButtonElement>('.delete-button')!.click();

      expect(deleteAttachment).toHaveBeenCalledWith(1, 11);
      expect(byId(1).attachments).toEqual([]);
      expect(files(0).querySelector('.empty')?.textContent).toBe('Brak załączników');
    });

    it('opens the attachments dialog as an ARIA modal and puts the updated application into the list', () => {
      create();
      const updated = withAttachments(1);
      dialogOpen.mockReturnValue({ afterClosed: () => of(updated) });
      const original = byId(1);

      expand(0).querySelector<HTMLButtonElement>('button.add-attachments')!.click();

      expect(dialogOpen).toHaveBeenCalledWith(AttachmentsDialogComponent, expect.objectContaining({ data: original, ariaModal: true }));
      expect(byId(1).attachments).toEqual([CV, LETTER]);
      expect(snackBarOpen).toHaveBeenCalledWith('Załączniki zostały dodane do aplikacji', 'OK', { duration: 5000, politeness: 'polite' });
      expect(getApplication).toHaveBeenCalledTimes(1); // tylko historia przy rozwinięciu
    });

    it('leaves the list unchanged when the dialog is cancelled', () => {
      create();
      dialogOpen.mockReturnValue({ afterClosed: () => of(undefined) });

      component.openAttachmentsDialog(byId(1));

      expect(byId(1).attachments).toEqual([]);
      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(getApplication).not.toHaveBeenCalled();
    });

    it('refreshes the application from the server after an aborted upload (the server may have saved the files)', () => {
      create();
      dialogOpen.mockReturnValue({ afterClosed: () => of(ATTACHMENTS_UPLOAD_ABORTED) });
      getApplication.mockReturnValue(of({ ...withAttachments(1, ApplicationStatus.Submitted, [CV]), history: HISTORY }));

      component.openAttachmentsDialog(byId(1));

      expect(getApplication).toHaveBeenCalledWith(1);
      expect(byId(1).attachments).toEqual([CV]);
      expect(byId(1).history).toBeUndefined();
      expect(component.histories()[1]).toEqual(HISTORY);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Przerwano wysyłanie załączników');
    });

    it('tells the user to reload when refreshing after an abort fails', () => {
      create();
      dialogOpen.mockReturnValue({ afterClosed: () => of(ATTACHMENTS_UPLOAD_ABORTED) });
      getApplication.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      component.openAttachmentsDialog(byId(1));

      expect(snackBarOpen.mock.calls.map(call => call[0])).toEqual([
        'Przerwano wysyłanie załączników', 'Nie udało się odświeżyć aplikacji – odśwież stronę'
      ]);
    });

    it('stops listening to the refresh after an abort when the page is destroyed', () => {
      create();
      const refresh = new Subject<JobApplication>();
      dialogOpen.mockReturnValue({ afterClosed: () => of(ATTACHMENTS_UPLOAD_ABORTED) });
      getApplication.mockReturnValue(refresh);

      component.openAttachmentsDialog(byId(1));
      fixture.destroy();

      expect(refresh.observed).toBe(false);
    });

    it('renders markup in attachment names as text', () => {
      const name = '<img src=x onerror="window.__pwned=true">.pdf';
      create(of([withAttachments(1, ApplicationStatus.Submitted, [{ ...CV, fileName: name }])]));
      expand(0);

      expect(files(0).querySelector('.file-name')?.textContent).toBe(name);
      expect(files(0).querySelector('img')).toBeNull();
    });
  });

  it('formats since when the status holds (with the year only for other years)', () => {
    const now = new Date('2026-11-07T12:00:00');
    expect(formatStatusSince('2026-11-03T10:00:00', now)).toBe('3 listopada');
    expect(formatStatusSince('2025-11-03T10:00:00', now)).toBe('3 listopada 2025');
  });
});
