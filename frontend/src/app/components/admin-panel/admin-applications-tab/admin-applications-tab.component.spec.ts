import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { Observable, Subject, of, throwError } from 'rxjs';
import { AdminApplicationsTabComponent } from './admin-applications-tab.component';
import { ApplicationStatusDialogComponent } from '../application-status-dialog/application-status-dialog.component';
import { ApplicationHistoryDialogComponent } from '../application-history-dialog/application-history-dialog.component';
import { JobApplicationService } from '../../../services/job-application.service';
import { ApplicationAttachment, ApplicationStatus, AttachmentKind, JobApplication } from '../../../models/job-application.model';
import { ApplicationParams } from '../../../models/application-params.model';
import { PaginatedResult } from '../../../models/pagination.model';
import { PanelMode } from '../../../models/panel-mode.model';

function makeApplication(id: number, status = ApplicationStatus.Submitted): JobApplication {
  return {
    id,
    jobOfferId: 1,
    userId: 'u1',
    userEmail: 'jan@example.com',
    userFullName: 'Jan Kowalski',
    jobTitle: 'Programista',
    company: 'Acme',
    companyId: 1,
    note: null,
    appliedAt: new Date('2026-01-01T10:00:00Z'),
    status,
    statusChangedAt: '2026-01-01T10:00:00Z',
    attachments: [],
    attachmentsTotalSize: 0
  };
}

function page(items: number, totalItems: number, currentPage = 1, pageSize = 10): PaginatedResult<JobApplication> {
  return {
    result: Array.from({ length: items }, (_, i) => makeApplication(i + 1)),
    pagination: {
      CurrentPage: currentPage,
      ItemsPerPage: pageSize,
      TotalItems: totalItems,
      TotalPages: Math.max(1, Math.ceil(totalItems / pageSize))
    }
  };
}

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('AdminApplicationsTabComponent', () => {
  let fixture: ComponentFixture<AdminApplicationsTabComponent>;
  let component: AdminApplicationsTabComponent;
  let getAllApplications: ReturnType<typeof vi.fn>;
  let deleteApplication: ReturnType<typeof vi.fn>;
  let downloadAttachment: ReturnType<typeof vi.fn>;
  let alertSpy: ReturnType<typeof vi.spyOn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;
  let dialogOpen: ReturnType<typeof vi.fn>;
  let dialogResult: Observable<JobApplication | undefined>;

  function lastParams(): ApplicationParams {
    return getAllApplications.mock.calls[getAllApplications.mock.calls.length - 1][0] as ApplicationParams;
  }

  function create(initial: Observable<PaginatedResult<JobApplication>> = of(page(10, 35))): void {
    getAllApplications.mockReturnValue(initial);
    fixture = TestBed.createComponent(AdminApplicationsTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    getAllApplications = vi.fn();
    deleteApplication = vi.fn().mockReturnValue(of(undefined));
    downloadAttachment = vi.fn().mockReturnValue(of(undefined));
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    dialogResult = of(undefined);
    dialogOpen = vi.fn().mockImplementation(() => ({ afterClosed: () => dialogResult }));

    await TestBed.configureTestingModule({
      imports: [AdminApplicationsTabComponent],
      providers: [
        { provide: JobApplicationService, useValue: { getAllApplications, deleteApplication, downloadAttachment } },
        { provide: MatDialog, useValue: { open: dialogOpen } }
      ]
    }).compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('requests the first page with default params and stores the result', () => {
    create();

    expect(getAllApplications).toHaveBeenCalledTimes(1);
    const params = lastParams();
    expect(params.pageNumber).toBe(1);
    expect(params.pageSize).toBe(10);
    expect(params.orderBy).toBe('newest');
    expect(params.searchTerm).toBe('');
    expect(params.jobOfferId).toBeNull();
    expect(component.applications().length).toBe(10);
    expect(component.totalItems()).toBe(35);
    expect(component.loading()).toBe(false);
  });

  describe('paging and sorting', () => {
    it('onPageChange sends pageNumber (1-based) and pageSize', () => {
      create();

      component.onPageChange({ pageIndex: 1, pageSize: 5, length: 35, previousPageIndex: 0 });

      expect(lastParams().pageNumber).toBe(2);
      expect(lastParams().pageSize).toBe(5);
    });

    it('clicking the paginator "next" button requests the next page', async () => {
      create();
      await fixture.whenStable();
      fixture.detectChanges();

      const next = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next');
      expect(next).not.toBeNull();
      next!.click();

      expect(lastParams().pageNumber).toBe(2);
    });

    it('sorting by appliedAt ascending sends orderBy=oldest and resets the page', () => {
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.onSortChange({ active: 'appliedAt', direction: 'asc' });

      expect(lastParams().orderBy).toBe('oldest');
      expect(lastParams().pageNumber).toBe(1);
    });

    it('sorting by appliedAt descending sends orderBy=newest', () => {
      create();
      component.onSortChange({ active: 'appliedAt', direction: 'asc' });

      component.onSortChange({ active: 'appliedAt', direction: 'desc' });

      expect(lastParams().orderBy).toBe('newest');
    });

    it('clicking the "Data aplikacji" header toggles the order', async () => {
      create();
      await fixture.whenStable();
      fixture.detectChanges();

      // Domyślnie malejąco (newest) – kliknięcie przełącza na rosnące
      const header = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('th.mat-sort-header'))
        .find(h => h.textContent?.includes('Data aplikacji'));
      expect(header).toBeDefined();
      header!.click();

      expect(lastParams().orderBy).toBe('oldest');
    });
  });

  describe('search', () => {
    it('is debounced, trimmed and resets the page', () => {
      vi.useFakeTimers();
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });
      const callsBefore = getAllApplications.mock.calls.length;

      component.onSearchInput('kow');
      component.onSearchInput(' kowalski ');
      vi.advanceTimersByTime(399);
      expect(getAllApplications.mock.calls.length).toBe(callsBefore);

      vi.advanceTimersByTime(1);

      expect(getAllApplications.mock.calls.length).toBe(callsBefore + 1);
      expect(lastParams().searchTerm).toBe('kowalski');
      expect(lastParams().pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(true);
    });

    it('clearSearch reloads immediately and cancels a pending debounce', () => {
      vi.useFakeTimers();
      create();
      component.onSearchInput('abc');
      vi.advanceTimersByTime(400);
      expect(lastParams().searchTerm).toBe('abc');

      component.onSearchInput('abcd');
      component.clearSearch();
      const callsAfterClear = getAllApplications.mock.calls.length;
      expect(lastParams().searchTerm).toBe('');

      vi.advanceTimersByTime(500);

      expect(getAllApplications.mock.calls.length).toBe(callsAfterClear);
    });

    it('clearFilters resets search, sort and page', () => {
      create();
      component.onSortChange({ active: 'appliedAt', direction: 'asc' });
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.clearFilters();

      expect(lastParams().orderBy).toBe('newest');
      expect(lastParams().searchTerm).toBe('');
      expect(lastParams().pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(false);
    });
  });

  describe('deleting', () => {
    it('does nothing when the user cancels the confirmation', () => {
      create();

      confirmSpy.mockReturnValue(false);
      component.deleteApplication(1);

      expect(deleteApplication).not.toHaveBeenCalled();
      expect(getAllApplications).toHaveBeenCalledTimes(1);
    });

    it('reloads the current page after deleting one of many items', () => {
      create();
      component.onPageChange({ pageIndex: 1, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.deleteApplication(4);

      expect(deleteApplication).toHaveBeenCalledWith(4);
      expect(lastParams().pageNumber).toBe(2);
      expect(alertSpy).toHaveBeenCalledWith('Aplikacja została usunięta');
      expect(component.deletingId()).toBeNull();
    });

    it('steps back one page after deleting the last item on a page', () => {
      create(of(page(10, 11)));
      getAllApplications.mockReturnValue(of(page(1, 11, 2)));
      component.onPageChange({ pageIndex: 1, pageSize: 10, length: 11, previousPageIndex: 0 });
      expect(component.applications().length).toBe(1);
      getAllApplications.mockReturnValue(of(page(10, 10)));

      component.deleteApplication(1);

      expect(lastParams().pageNumber).toBe(1);
      expect(component.pageIndex()).toBe(0);
      expect(component.applications().length).toBe(10);
    });

    it('shows an error when deletion fails', () => {
      create();
      deleteApplication.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      component.deleteApplication(1);

      expect(alertSpy).toHaveBeenCalledWith('Błąd podczas usuwania aplikacji');
      expect(component.deletingId()).toBeNull();
    });

    it.each([401, 403])('does not show its own alert when the interceptor handles %i', status => {
      create();
      deleteApplication.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));

      component.deleteApplication(1);

      expect(alertSpy).not.toHaveBeenCalled();
    });
  });

  describe('load errors and out-of-range pages', () => {
    it('alerts on a load error and stops the loading indicator', () => {
      create(throwError(() => new HttpErrorResponse({ status: 500 })));

      expect(alertSpy).toHaveBeenCalledWith('Błąd podczas ładowania aplikacji');
      expect(component.loading()).toBe(false);
    });

    it('does not alert on 401 (handled by the interceptor)', () => {
      create(throwError(() => new HttpErrorResponse({ status: 401 })));

      expect(alertSpy).not.toHaveBeenCalled();
    });

    it('jumps to the last existing page when the requested page is out of range', () => {
      create();
      getAllApplications.mockReset();
      getAllApplications
        .mockReturnValueOnce(of(page(0, 11, 3)))
        .mockReturnValueOnce(of(page(1, 11, 2)));

      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      expect(getAllApplications).toHaveBeenCalledTimes(2);
      expect(lastParams().pageNumber).toBe(2);
      expect(component.pageIndex()).toBe(1);
      expect(component.applications().length).toBe(1);
      expect(component.loading()).toBe(false);
    });

    it('ignores a stale response when a newer request was issued (switchMap)', () => {
      const first = new Subject<PaginatedResult<JobApplication>>();
      const second = new Subject<PaginatedResult<JobApplication>>();
      create(first);
      getAllApplications.mockReturnValue(second);

      component.reload();
      first.next(page(5, 5));
      expect(component.applications().length).toBe(0);

      second.next(page(2, 2));
      expect(component.applications().length).toBe(2);
    });
  });

  describe('status filter', () => {
    it('offers "Wszystkie" and every status in the dropdown', () => {
      create();

      expect(component.statusOptions.map(o => o.label)).toEqual([
        'Wszystkie', 'Złożona', 'W analizie', 'Rozmowa', 'Oferta', 'Zatrudniony', 'Odrzucona', 'Wycofana'
      ]);
    });

    it('does not send a status by default', () => {
      create();

      expect(lastParams().status).toBeNull();
      expect(component.hasActiveFilters()).toBe(false);
    });

    it('sends the selected status and resets to the first page', () => {
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.onStatusChange(ApplicationStatus.Interview);

      expect(lastParams().status).toBe(ApplicationStatus.Interview);
      expect(lastParams().pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(true);
    });

    it('sends status 0 (Złożona) as an active filter, not as "no filter"', () => {
      create();

      component.onStatusChange(ApplicationStatus.Submitted);

      expect(lastParams().status).toBe(0);
      expect(component.hasActiveFilters()).toBe(true);
    });

    it('keeps the status filter when paging and sorting', () => {
      create();
      component.onStatusChange(ApplicationStatus.Rejected);

      component.onPageChange({ pageIndex: 1, pageSize: 10, length: 35, previousPageIndex: 0 });
      expect(lastParams().status).toBe(ApplicationStatus.Rejected);

      component.onSortChange({ active: 'appliedAt', direction: 'asc' });
      expect(lastParams().status).toBe(ApplicationStatus.Rejected);
    });

    it('selecting an option in the dropdown applies the filter', async () => {
      create();
      await fixture.whenStable();
      fixture.detectChanges();

      const selects = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.filters-card .mat-mdc-select-trigger');
      selects[0].click();
      fixture.detectChanges();
      await fixture.whenStable();
      const option = Array.from(document.querySelectorAll<HTMLElement>('mat-option')).find(o => o.textContent?.trim() === 'Rozmowa');
      expect(option).toBeDefined();
      option!.click();
      fixture.detectChanges();

      expect(lastParams().status).toBe(ApplicationStatus.Interview);
    });

    it('clearFilters resets the status filter', () => {
      create();
      component.onStatusChange(ApplicationStatus.Hired);

      component.clearFilters();

      expect(lastParams().status).toBeNull();
      expect(component.status()).toBeNull();
      expect(component.hasActiveFilters()).toBe(false);
    });

    it('shows the "no results" state with a clear button when the status filter matches nothing', async () => {
      create();
      getAllApplications.mockReturnValue(of(page(0, 0)));

      component.onStatusChange(ApplicationStatus.Hired);
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Brak wyników');
      expect(text).not.toContain('Brak aplikacji');
    });
  });

  describe('status column and row actions', () => {
    async function render(applications: JobApplication[]): Promise<HTMLElement> {
      create(of({
        result: applications,
        pagination: { CurrentPage: 1, ItemsPerPage: 10, TotalItems: applications.length, TotalPages: 1 }
      }));
      await fixture.whenStable();
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    it('shows a Polish status chip in every row', async () => {
      const root = await render([
        makeApplication(1, ApplicationStatus.Interview),
        makeApplication(2, ApplicationStatus.Rejected),
        makeApplication(3, ApplicationStatus.Withdrawn)
      ]);

      const chips = Array.from(root.querySelectorAll('td app-status-chip')).map(c => c.textContent?.trim());
      expect(chips).toEqual(['Rozmowa', 'Odrzucona', 'Wycofana']);
    });

    it('disables the status change button only for withdrawn applications', async () => {
      const root = await render([makeApplication(1, ApplicationStatus.Hired), makeApplication(2, ApplicationStatus.Withdrawn)]);

      const rows = Array.from(root.querySelectorAll('tr.table-row'));
      const changeButton = (row: Element) => row.querySelector<HTMLButtonElement>('.actions-cell button:nth-child(1)')!;
      expect(changeButton(rows[0]).disabled).toBe(false);
      expect(changeButton(rows[1]).disabled).toBe(true);
    });

    it('opens the status dialog from the row button with the application as data', async () => {
      const root = await render([makeApplication(1, ApplicationStatus.InReview)]);

      root.querySelector<HTMLButtonElement>('.actions-cell button:nth-child(1)')!.click();

      expect(dialogOpen).toHaveBeenCalledTimes(1);
      expect(dialogOpen.mock.calls[0][0]).toBe(ApplicationStatusDialogComponent);
      expect(dialogOpen.mock.calls[0][1].data).toEqual(expect.objectContaining({ id: 1, status: ApplicationStatus.InReview }));
    });

    it('opens the history dialog from the row button', async () => {
      const root = await render([makeApplication(5)]);

      root.querySelector<HTMLButtonElement>('.actions-cell button:nth-child(2)')!.click();

      expect(dialogOpen).toHaveBeenCalledTimes(1);
      expect(dialogOpen.mock.calls[0][0]).toBe(ApplicationHistoryDialogComponent);
      expect(dialogOpen.mock.calls[0][1].data).toEqual(expect.objectContaining({ id: 5 }));
    });
  });

  describe('dialogs', () => {
    it('reloads the current page after a status change', () => {
      create();
      component.onPageChange({ pageIndex: 1, pageSize: 10, length: 35, previousPageIndex: 0 });
      const callsBefore = getAllApplications.mock.calls.length;
      dialogResult = of(makeApplication(1, ApplicationStatus.Interview));

      component.openStatusDialog(makeApplication(1));

      expect(getAllApplications.mock.calls.length).toBe(callsBefore + 1);
      expect(lastParams().pageNumber).toBe(2);
    });

    it('keeps the active status filter when reloading after a status change', () => {
      create();
      component.onStatusChange(ApplicationStatus.Submitted);
      dialogResult = of(makeApplication(1, ApplicationStatus.Interview));

      component.openStatusDialog(makeApplication(1));

      expect(lastParams().status).toBe(ApplicationStatus.Submitted);
    });

    it('does not reload when the status dialog is dismissed', () => {
      create();
      const callsBefore = getAllApplications.mock.calls.length;
      dialogResult = of(undefined);

      component.openStatusDialog(makeApplication(1));

      expect(getAllApplications.mock.calls.length).toBe(callsBefore);
    });

    it('does not reload after the history dialog is closed', () => {
      create();
      const callsBefore = getAllApplications.mock.calls.length;

      component.openHistoryDialog(makeApplication(1));

      expect(dialogOpen).toHaveBeenCalledWith(ApplicationHistoryDialogComponent, expect.objectContaining({ data: expect.objectContaining({ id: 1 }) }));
      expect(getAllApplications.mock.calls.length).toBe(callsBefore);
    });
  });

  describe('panel mode', () => {
    function createInMode(mode: PanelMode, applications = [makeApplication(1)]): void {
      getAllApplications.mockReturnValue(of({ ...page(0, applications.length), result: applications }));
      fixture = TestBed.createComponent(AdminApplicationsTabComponent);
      fixture.componentRef.setInput('mode', mode);
      component = fixture.componentInstance;
      fixture.detectChanges();
    }

    function root(): HTMLElement {
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    function headers(): string[] {
      return Array.from(root().querySelectorAll('th')).map(h => h.textContent?.trim() ?? '');
    }

    function rowButtonLabels(index: number): string[] {
      return Array.from(root().querySelectorAll('tr.table-row')[index].querySelectorAll('.actions-cell mat-icon'))
        .map(icon => icon.textContent?.trim() ?? '');
    }

    describe('admin', () => {
      it('is the default mode: shows the company column and the delete button', () => {
        create(of(page(2, 2)));

        expect(component.isAdmin()).toBe(true);
        expect(component.displayedColumns()).toContain('company');
        expect(headers()).toContain('Firma');
        expect(rowButtonLabels(0)).toEqual(['swap_horiz', 'history', 'delete']);
      });

      it('renders the company name (or N/A) in the company column', () => {
        createInMode('admin', [{ ...makeApplication(1), company: 'Alfa' }, { ...makeApplication(2), company: '' }]);

        const cells = Array.from(root().querySelectorAll('tr.table-row .company-name')).map(c => c.textContent);
        expect(cells).toEqual(['Alfa', 'N/A']);
      });

      it('deletes an application after confirmation', () => {
        createInMode('admin');

        component.deleteApplication(1);

        expect(deleteApplication).toHaveBeenCalledWith(1);
      });

      it('clicking the delete button removes the application', () => {
        createInMode('admin');

        root().querySelector<HTMLButtonElement>('tr.table-row button[color="warn"]')!.click();

        expect(deleteApplication).toHaveBeenCalledWith(1);
      });

      it('shows the generic empty state text', () => {
        createInMode('admin', []);

        expect(root().textContent).toContain('Nie ma jeszcze żadnych aplikacji na oferty pracy');
      });
    });

    describe('employer', () => {
      it('hides the company column (all applications are for the employer own company)', () => {
        createInMode('employer');

        expect(component.isAdmin()).toBe(false);
        expect(component.displayedColumns()).toEqual(['id', 'jobTitle', 'user', 'status', 'appliedAt', 'note', 'attachments', 'actions']);
        expect(headers()).not.toContain('Firma');
        expect(root().querySelector('.company-name')).toBeNull();
      });

      it('offers only "Zmień status" and "Historia" - no delete button', () => {
        createInMode('employer', [makeApplication(1), makeApplication(2)]);

        expect(rowButtonLabels(0)).toEqual(['swap_horiz', 'history']);
        expect(rowButtonLabels(1)).toEqual(['swap_horiz', 'history']);
        expect(root().querySelector('mat-icon.mat-warn, button[color="warn"]')).toBeNull();
      });

      it('deleteApplication is a no-op: no confirmation, no API call', () => {
        createInMode('employer');
        const confirmCalls = confirmSpy.mock.calls.length;

        component.deleteApplication(1);

        expect(confirmSpy.mock.calls.length).toBe(confirmCalls);
        expect(deleteApplication).not.toHaveBeenCalled();
        expect(component.deletingId()).toBeNull();
      });

      it('still loads, filters and sorts through the same endpoint', () => {
        createInMode('employer');

        component.onStatusChange(ApplicationStatus.Interview);

        expect(lastParams().status).toBe(ApplicationStatus.Interview);
        expect(lastParams().pageNumber).toBe(1);
      });

      it('still opens the status and history dialogs', () => {
        createInMode('employer');

        component.openStatusDialog(makeApplication(1));
        component.openHistoryDialog(makeApplication(1));

        expect(dialogOpen).toHaveBeenCalledWith(ApplicationStatusDialogComponent, expect.anything());
        expect(dialogOpen).toHaveBeenCalledWith(ApplicationHistoryDialogComponent, expect.anything());
      });

      it('shows the employer-specific empty state text', () => {
        createInMode('employer', []);

        expect(root().textContent).toContain('Nie ma jeszcze aplikacji na oferty Twojej firmy');
        expect(root().textContent).not.toContain('Nie ma jeszcze żadnych aplikacji na oferty pracy');
      });
    });
  });

  it('formatDate renders a Polish date', () => {
    create();
    const text = component.formatDate(new Date('2026-03-05T10:30:00Z'));
    expect(text).toContain('2026');
    expect(text.toLowerCase()).toContain('marca');
  });

  describe('attachments', () => {
    const CV: ApplicationAttachment = { id: 21, kind: AttachmentKind.Cv, fileName: 'jan-cv.pdf', size: 1024, uploadedAt: '2026-01-02T10:00:00Z' };
    const PORTFOLIO: ApplicationAttachment = { id: 22, kind: AttachmentKind.Portfolio, fileName: 'projekt.png', size: 2048, uploadedAt: '2026-01-03T10:00:00Z' };

    function withAttachments(id: number, attachments = [CV, PORTFOLIO]): JobApplication {
      return { ...makeApplication(id), attachments, attachmentsTotalSize: attachments.reduce((s, a) => s + a.size, 0) };
    }

    function createWith(mode: PanelMode, applications: JobApplication[]): void {
      getAllApplications.mockReturnValue(of({ ...page(0, applications.length), result: applications }));
      fixture = TestBed.createComponent(AdminApplicationsTabComponent);
      fixture.componentRef.setInput('mode', mode);
      component = fixture.componentInstance;
      fixture.detectChanges();
    }

    function cell(index: number): HTMLElement {
      fixture.detectChanges();
      return (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('td.mat-column-attachments')[index];
    }

    function downloadButtons(index: number): HTMLButtonElement[] {
      return Array.from(cell(index).querySelectorAll<HTMLButtonElement>('button.download-button'));
    }

    it.each<PanelMode>(['admin', 'employer'])('%s: shows the "Załączniki" column before the actions', mode => {
      createWith(mode, [withAttachments(1)]);

      const columns = component.displayedColumns();
      expect(columns.indexOf('attachments')).toBe(columns.indexOf('actions') - 1);
      const headers = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('th')).map(h => h.textContent?.trim());
      expect(headers).toContain('Załączniki');
    });

    it.each<PanelMode>(['admin', 'employer'])('%s: offers a labelled download button for every attachment and nothing else', mode => {
      createWith(mode, [withAttachments(1), makeApplication(2)]);

      expect(downloadButtons(0).map(b => b.getAttribute('aria-label'))).toEqual([
        'Pobierz CV: jan-cv.pdf', 'Pobierz Portfolio: projekt.png'
      ]);
      expect(cell(0).querySelectorAll('.delete-button').length).toBe(0);
      expect(cell(1).querySelector('.empty')?.textContent).toBe('Brak');
      expect(cell(1).querySelectorAll('button').length).toBe(0);
    });

    it('no longer shows the single-CV button in the actions', () => {
      createWith('admin', [withAttachments(1)]);

      expect((fixture.nativeElement as HTMLElement).querySelector('.actions-cell .download-cv')).toBeNull();
    });

    it('downloads the clicked attachment of the row application', () => {
      createWith('employer', [withAttachments(4)]);

      downloadButtons(0)[1].click();

      expect(downloadAttachment).toHaveBeenCalledWith(4, PORTFOLIO);
    });

    it('marks the button with aria-disabled while the file is being downloaded', () => {
      const pending = new Subject<void>();
      downloadAttachment.mockReturnValue(pending);
      createWith('admin', [withAttachments(1)]);

      downloadButtons(0)[0].click();

      expect(downloadButtons(0)[0].getAttribute('aria-disabled')).toBe('true');
      expect(downloadButtons(0)[0].disabled).toBe(false);
      pending.next();
      pending.complete();
      expect(downloadButtons(0)[0].getAttribute('aria-disabled')).toBeNull();
    });

    it('shows the translated API message in an alert when the download fails', () => {
      downloadAttachment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Attachment not found' } })));
      createWith('admin', [withAttachments(1)]);

      downloadButtons(0)[0].click();

      expect(cell(0).querySelector('[role="alert"]')?.textContent).toBe('Nie znaleziono załącznika');
      expect(alertSpy).not.toHaveBeenCalled();
    });

    it('does not show a message when the interceptor handles 403', () => {
      downloadAttachment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
      createWith('employer', [withAttachments(1)]);

      downloadButtons(0)[0].click();

      expect(cell(0).querySelector('[role="alert"]')).toBeNull();
      expect(alertSpy).not.toHaveBeenCalled();
    });

    it('opens the status and history dialogs as ARIA modals', () => {
      createWith('admin', [withAttachments(1)]);

      component.openStatusDialog(component.applications()[0]);
      component.openHistoryDialog(component.applications()[0]);

      expect(dialogOpen.mock.calls.map(call => call[1].ariaModal)).toEqual([true, true]);
    });

    it('stops a pending download when the tab is destroyed', () => {
      const pending = new Subject<void>();
      downloadAttachment.mockReturnValue(pending);
      createWith('admin', [withAttachments(1)]);

      downloadButtons(0)[0].click();
      fixture.destroy();

      expect(pending.observed).toBe(false);
    });
  });
});
