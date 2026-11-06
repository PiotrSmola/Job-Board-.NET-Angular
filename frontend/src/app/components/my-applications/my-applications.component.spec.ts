import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { Observable, Subject, of, throwError } from 'rxjs';
import { MyApplicationsComponent } from './my-applications.component';
import { ATTACHMENTS_UPLOAD_ABORTED, AttachmentsDialogComponent } from './attachments-dialog.component';
import { JobApplicationService } from '../../services/job-application.service';
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
  let alertSpy: ReturnType<typeof vi.spyOn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;

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
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [MyApplicationsComponent],
      providers: [
        provideRouter([]),
        { provide: JobApplicationService, useValue: { getMyApplications, getApplication, withdraw, downloadAttachment, deleteAttachment } },
        { provide: MatDialog, useValue: { open: dialogOpen } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('loads and shows the current user applications', () => {
    create();

    expect(component.applications.map(a => a.id)).toEqual([1, 2]);
    expect(component.loading).toBe(false);
  });

  it('shows a snackbar when loading fails', () => {
    create(throwError(() => new HttpErrorResponse({ status: 500 })));

    expect(snackBarOpen).toHaveBeenCalledTimes(1);
    expect(snackBarOpen.mock.calls[0][0]).toBe('Błąd podczas ładowania aplikacji');
    expect(alertSpy).not.toHaveBeenCalled();
    expect(component.loading).toBe(false);
  });

  it.each([401, 403])('does not duplicate the interceptor notice when loading fails with %i', status => {
    create(throwError(() => new HttpErrorResponse({ status, statusText: 'Error', url: '/api/jobapplications/my-applications' })));

    expect(snackBarOpen).not.toHaveBeenCalled();
    expect(alertSpy).not.toHaveBeenCalled();
    expect(component.loading).toBe(false);
  });

  it('shows the Polish status label of each application', () => {
    create(of([makeApplication(1, ApplicationStatus.Interview), makeApplication(2, ApplicationStatus.Rejected)]));

    const labels = Array.from(root().querySelectorAll('app-status-chip')).map(e => e.textContent?.trim());
    expect(labels).toEqual(['Rozmowa', 'Odrzucona']);
  });

  describe('withdrawing', () => {
    it('withdraws after confirmation and updates the status in place', () => {
      create();

      component.withdrawApplication(component.applications[0]);

      expect(withdraw).toHaveBeenCalledWith(1);
      expect(component.applications.map(a => a.status)).toEqual([ApplicationStatus.Withdrawn, ApplicationStatus.Submitted]);
      expect(component.withdrawingId).toBeNull();
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja została wycofana');
    });

    it('does not withdraw when the confirmation is declined', () => {
      create();
      confirmSpy.mockReturnValue(false);

      component.withdrawApplication(component.applications[0]);

      expect(withdraw).not.toHaveBeenCalled();
      expect(component.applications[0].status).toBe(ApplicationStatus.Submitted);
    });

    it('offers withdrawal only for statuses the API allows', () => {
      const statuses = [
        ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Offered,
        ApplicationStatus.Hired, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn
      ];
      create(of(statuses.map((status, i) => makeApplication(i + 1, status))));

      expect(component.applications.map(a => component.canWithdraw(a))).toEqual([true, true, true, true, false, false, false]);
      expect(root().querySelectorAll('button[mattooltip="Wycofaj aplikację"]').length).toBe(4);
    });

    it('keeps the status and shows the translated API message when withdrawal is rejected', () => {
      create();
      withdraw.mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 400, error: { message: 'Application cannot be withdrawn in its current status' }
      })));

      component.withdrawApplication(component.applications[0]);

      expect(component.applications[0].status).toBe(ApplicationStatus.Submitted);
      expect(component.withdrawingId).toBeNull();
      expect(snackBarOpen.mock.calls[0][0]).toBe('Tej aplikacji nie można już wycofać');
    });

    it('shows a generic message when withdrawal fails on the server', () => {
      create();
      withdraw.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      component.withdrawApplication(component.applications[0]);

      expect(snackBarOpen.mock.calls[0][0]).toBe('Błąd podczas wycofywania aplikacji');
    });

    it.each([401, 403])('does not duplicate the interceptor notice when withdrawal fails with %i', status => {
      create();
      withdraw.mockReturnValue(throwError(() => new HttpErrorResponse({ status, statusText: 'Error' })));

      component.withdrawApplication(component.applications[0]);

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(alertSpy).not.toHaveBeenCalled();
      expect(component.withdrawingId).toBeNull();
    });
  });

  describe('history', () => {
    it('loads the history once and toggles the timeline', () => {
      create();

      component.toggleHistory(component.applications[0]);

      expect(getApplication).toHaveBeenCalledWith(1);
      expect(component.expandedId).toBe(1);
      expect(root().querySelectorAll('app-status-timeline li').length).toBe(2);
      expect(root().querySelector('app-status-timeline')?.textContent).toContain('Zapraszamy');

      component.toggleHistory(component.applications[0]);
      expect(component.expandedId).toBeNull();

      component.toggleHistory(component.applications[0]);
      expect(getApplication).toHaveBeenCalledTimes(1);
    });

    it('collapses and shows a snackbar when the history cannot be loaded', () => {
      create();
      getApplication.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      component.toggleHistory(component.applications[0]);

      expect(component.expandedId).toBeNull();
      expect(snackBarOpen.mock.calls[0][0]).toBe('Nie udało się wczytać historii aplikacji');
    });

    it('keeps the spinner of a row expanded later when the history of an earlier row arrives', () => {
      create();
      const first = new Subject<JobApplication>();
      const second = new Subject<JobApplication>();
      getApplication.mockImplementation((id: number) => id === 1 ? first : second);

      component.toggleHistory(component.applications[0]);
      component.toggleHistory(component.applications[1]);
      first.next({ ...makeApplication(1), history: HISTORY });

      expect(component.expandedId).toBe(2);
      expect(component.historyLoadingId).toBe(2);
      expect(component.histories[1].length).toBe(2);

      second.next({ ...makeApplication(2), history: HISTORY.slice(0, 1) });
      expect(component.historyLoadingId).toBeNull();
      expect(root().querySelectorAll('app-status-timeline li').length).toBe(1);
    });

    it('does not collapse a row expanded later when the history of an earlier row fails', () => {
      create();
      const first = new Subject<JobApplication>();
      const second = new Subject<JobApplication>();
      getApplication.mockImplementation((id: number) => id === 1 ? first : second);

      component.toggleHistory(component.applications[0]);
      component.toggleHistory(component.applications[1]);
      first.error(new HttpErrorResponse({ status: 500 }));

      expect(component.expandedId).toBe(2);
      expect(component.historyLoadingId).toBe(2);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Nie udało się wczytać historii aplikacji');
    });

    it('reloads the cached history of an expanded row after withdrawal without history in the response', () => {
      create();
      component.toggleHistory(component.applications[0]);
      expect(component.histories[1].length).toBe(2);
      withdraw.mockReturnValue(of(makeApplication(1, ApplicationStatus.Withdrawn)));
      const withWithdrawal = [...HISTORY, { status: ApplicationStatus.Withdrawn, changedAt: '2026-01-03T10:00:00Z', comment: null, changedBy: null }];
      getApplication.mockReturnValue(of({ ...makeApplication(1, ApplicationStatus.Withdrawn), history: withWithdrawal }));

      component.withdrawApplication(component.applications[0]);

      expect(getApplication).toHaveBeenCalledTimes(2);
      expect(component.histories[1].length).toBe(3);
    });

    it('drops the cached history of a collapsed row after withdrawal without history in the response', () => {
      create();
      component.toggleHistory(component.applications[0]);
      component.toggleHistory(component.applications[0]);
      withdraw.mockReturnValue(of(makeApplication(1, ApplicationStatus.Withdrawn)));

      component.withdrawApplication(component.applications[0]);

      expect(component.histories[1]).toBeUndefined();
      expect(getApplication).toHaveBeenCalledTimes(1);
      component.toggleHistory(component.applications[0]);
      expect(getApplication).toHaveBeenCalledTimes(2);
    });

    it('uses the history returned by withdraw without another request', () => {
      create();
      component.withdrawApplication(component.applications[0]);

      component.toggleHistory(component.applications[0]);

      expect(getApplication).not.toHaveBeenCalled();
      expect(component.histories[1].length).toBe(3);
    });
  });

  it('viewJobDetails navigates to the job page', () => {
    create();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.viewJobDetails(20);

    expect(navigate).toHaveBeenCalledWith(['/jobs', 20]);
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

    function cell(index: number): HTMLElement {
      return root().querySelectorAll<HTMLElement>('td.mat-column-attachments')[index];
    }

    function labels(index: number): (string | null)[] {
      return Array.from(cell(index).querySelectorAll('button')).map(b => b.getAttribute('aria-label'));
    }

    it('has an "Załączniki" column instead of the single CV column', () => {
      create();

      expect(component.displayedColumns).toContain('attachments');
      expect(component.displayedColumns).not.toContain('cv');
      expect(Array.from(root().querySelectorAll('th')).map(th => th.textContent?.trim())).toContain('Załączniki');
    });

    it('lists the kind, name, size and date of every attachment', () => {
      create(of([withAttachments(1), makeApplication(2)]));

      const items = Array.from(cell(0).querySelectorAll('li'));
      expect(items.map(li => li.querySelector('.file-name')?.textContent)).toEqual(['moje-cv.pdf', 'list.docx']);
      expect(items.map(li => li.querySelector('.meta')?.textContent)).toEqual([
        'CV · 350 KB · 02.01.2026', 'List motywacyjny · 2 KB · 03.01.2026'
      ]);
      expect(cell(1).querySelector('.empty')?.textContent).toBe('Brak');
    });

    it('labels the buttons with the action, the kind and the file name', () => {
      create(of([withAttachments(1), makeApplication(2)]));

      expect(labels(0)).toEqual([
        'Pobierz CV: moje-cv.pdf', 'Usuń CV: moje-cv.pdf',
        'Pobierz List motywacyjny: list.docx', 'Usuń List motywacyjny: list.docx',
        'Dodaj załączniki do aplikacji: Oferta 1'
      ]);
      expect(labels(1)).toEqual(['Dodaj załączniki do aplikacji: Oferta 2']);
    });

    it('offers adding only in the statuses the API allows, but deleting and downloading always', () => {
      const statuses = [
        ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Offered,
        ApplicationStatus.Hired, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn
      ];
      create(of(statuses.map((status, i) => withAttachments(i + 1, status, [CV]))));

      const adds = statuses.map((_, i) => cell(i).querySelectorAll('button.add-attachments').length);
      expect(adds).toEqual([1, 1, 1, 1, 0, 0, 0]);
      const others = statuses.map((_, i) => cell(i).querySelectorAll('.download-button, .delete-button').length);
      expect(others).toEqual([2, 2, 2, 2, 2, 2, 2]);
    });

    it('does not offer adding when the application already has 10 attachments', () => {
      const ten = Array.from({ length: 10 }, (_, i) => ({ ...CV, id: 100 + i, fileName: `plik-${i}.pdf` }));
      create(of([withAttachments(1, ApplicationStatus.Submitted, ten)]));

      expect(component.canAddAttachments(component.applications[0])).toBe(false);
      expect(cell(0).querySelector('button.add-attachments')).toBeNull();
    });

    it('downloads an attachment of the right application', () => {
      create(of([withAttachments(1)]));

      cell(0).querySelectorAll<HTMLButtonElement>('.download-button')[1].click();

      expect(downloadAttachment).toHaveBeenCalledWith(1, LETTER);
    });

    it('marks the download button with aria-disabled until the file arrives', () => {
      create(of([withAttachments(1)]));
      const pending = new Subject<void>();
      downloadAttachment.mockReturnValue(pending);
      const button = cell(0).querySelector<HTMLButtonElement>('.download-button')!;

      button.click();
      expect(cell(0).querySelector('.download-button')?.getAttribute('aria-disabled')).toBe('true');
      expect(button.disabled).toBe(false);

      pending.next();
      pending.complete();
      expect(cell(0).querySelector('.download-button')?.getAttribute('aria-disabled')).toBeNull();
    });

    it('shows the translated API message when the download fails', () => {
      create(of([withAttachments(1)]));
      downloadAttachment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Attachment not found' } })));

      cell(0).querySelector<HTMLButtonElement>('.download-button')!.click();

      expect(cell(0).querySelector('[role="alert"]')?.textContent).toBe('Nie znaleziono załącznika');
    });

    it('deletes an attachment after confirmation and updates the list and the total size', () => {
      create(of([withAttachments(1)]));

      cell(0).querySelectorAll<HTMLButtonElement>('.delete-button')[0].click();

      expect(confirmSpy).toHaveBeenCalled();
      expect(deleteAttachment).toHaveBeenCalledWith(1, 11);
      expect(component.applications[0].attachments).toEqual([LETTER]);
      expect(component.applications[0].attachmentsTotalSize).toBe(2048);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Załącznik został usunięty');
      expect(Array.from(cell(0).querySelectorAll('.file-name')).map(e => e.textContent)).toEqual(['list.docx']);
    });

    it('allows deleting an attachment of a withdrawn application (right to erasure)', () => {
      create(of([withAttachments(1, ApplicationStatus.Withdrawn, [CV])]));

      cell(0).querySelector<HTMLButtonElement>('.delete-button')!.click();

      expect(deleteAttachment).toHaveBeenCalledWith(1, 11);
      expect(component.applications[0].attachments).toEqual([]);
      expect(cell(0).querySelector('.empty')?.textContent).toBe('Brak');
    });

    it('moves the focus to the next attachment after deleting one', () => {
      create(of([withAttachments(1)]));

      cell(0).querySelectorAll<HTMLButtonElement>('.delete-button')[0].click();
      root();

      expect(document.activeElement?.getAttribute('aria-label')).toBe('Usuń List motywacyjny: list.docx');
    });

    it('moves the focus to the empty-list text after deleting the last attachment', () => {
      create(of([withAttachments(1, ApplicationStatus.Submitted, [CV])]));

      cell(0).querySelector<HTMLButtonElement>('.delete-button')!.click();
      root();

      expect(document.activeElement).toBe(cell(0).querySelector('.empty'));
    });

    it('does not delete when the confirmation is declined', () => {
      create(of([withAttachments(1)]));
      confirmSpy.mockReturnValue(false);

      cell(0).querySelector<HTMLButtonElement>('.delete-button')!.click();

      expect(deleteAttachment).not.toHaveBeenCalled();
      expect(component.applications[0].attachments.length).toBe(2);
    });

    it('opens the attachments dialog as an ARIA modal and puts the updated application into the list', () => {
      create();
      const updated = withAttachments(1);
      dialogOpen.mockReturnValue({ afterClosed: () => of(updated) });

      const original = component.applications[0];
      cell(0).querySelector<HTMLButtonElement>('button.add-attachments')!.click();

      expect(dialogOpen).toHaveBeenCalledWith(AttachmentsDialogComponent, expect.objectContaining({ data: original, ariaModal: true }));
      expect(component.applications[0].attachments).toEqual([CV, LETTER]);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Załączniki zostały dodane do aplikacji');
      expect(getApplication).not.toHaveBeenCalled();
    });

    it('leaves the list unchanged when the dialog is cancelled', () => {
      create();
      dialogOpen.mockReturnValue({ afterClosed: () => of(undefined) });

      component.openAttachmentsDialog(component.applications[0]);

      expect(component.applications[0].attachments).toEqual([]);
      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(getApplication).not.toHaveBeenCalled();
    });

    it('refreshes the application from the server after an aborted upload (the server may have saved the files)', () => {
      create();
      dialogOpen.mockReturnValue({ afterClosed: () => of(ATTACHMENTS_UPLOAD_ABORTED) });
      getApplication.mockReturnValue(of({ ...withAttachments(1, ApplicationStatus.Submitted, [CV]), history: HISTORY }));

      component.openAttachmentsDialog(component.applications[0]);

      expect(getApplication).toHaveBeenCalledWith(1);
      expect(component.applications[0].attachments).toEqual([CV]);
      expect(component.applications[0].history).toBeUndefined();
      expect(component.histories[1]).toEqual(HISTORY);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Przerwano wysyłanie załączników');
      expect(Array.from(cell(0).querySelectorAll('.file-name')).map(e => e.textContent)).toEqual(['moje-cv.pdf']);
    });

    it('tells the user to reload when refreshing after an abort fails', () => {
      create();
      dialogOpen.mockReturnValue({ afterClosed: () => of(ATTACHMENTS_UPLOAD_ABORTED) });
      getApplication.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      component.openAttachmentsDialog(component.applications[0]);

      expect(snackBarOpen.mock.calls.map(call => call[0])).toEqual([
        'Przerwano wysyłanie załączników', 'Nie udało się odświeżyć aplikacji – odśwież stronę'
      ]);
    });

    it('stops listening to a pending download when the page is destroyed', () => {
      create(of([withAttachments(1)]));
      const pending = new Subject<void>();
      downloadAttachment.mockReturnValue(pending);

      cell(0).querySelector<HTMLButtonElement>('.download-button')!.click();
      fixture.destroy();

      expect(pending.observed).toBe(false);
    });

    it('stops listening to the refresh after an abort when the page is destroyed', () => {
      create();
      const refresh = new Subject<JobApplication>();
      dialogOpen.mockReturnValue({ afterClosed: () => of(ATTACHMENTS_UPLOAD_ABORTED) });
      getApplication.mockReturnValue(refresh);

      component.openAttachmentsDialog(component.applications[0]);
      fixture.destroy();

      expect(refresh.observed).toBe(false);
    });

    it('renders markup in attachment names as text', () => {
      const name = '<img src=x onerror="window.__pwned=true">.pdf';
      create(of([withAttachments(1, ApplicationStatus.Submitted, [{ ...CV, fileName: name }])]));

      expect(cell(0).querySelector('.file-name')?.textContent).toBe(name);
      expect(cell(0).querySelector('img')).toBeNull();
    });
  });
});
