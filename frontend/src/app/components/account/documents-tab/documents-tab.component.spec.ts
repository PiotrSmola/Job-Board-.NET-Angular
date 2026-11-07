import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, of, throwError } from 'rxjs';
import { DocumentsTabComponent } from './documents-tab.component';
import { AccountService } from '../../../services/account.service';
import { ConfirmService } from '../../../services/confirm.service';
import { ProfileDocument, ProfileDocumentUploadEvent } from '../../../models/account.model';
import { AttachmentKind } from '../../../models/job-application.model';

vi.setConfig({ testTimeout: 20000 });

const CV: ProfileDocument = { id: 1, kind: AttachmentKind.Cv, fileName: 'cv.pdf', size: 1024, uploadedAt: '2026-01-02T10:00:00Z' };
const LETTER: ProfileDocument = { id: 2, kind: AttachmentKind.CoverLetter, fileName: 'list.docx', size: 2048, uploadedAt: '2026-01-01T10:00:00Z' };

describe('DocumentsTabComponent', () => {
  let fixture: ComponentFixture<DocumentsTabComponent>;
  let getDocuments: ReturnType<typeof vi.fn>;
  let uploadDocuments: ReturnType<typeof vi.fn>;
  let deleteDocument: ReturnType<typeof vi.fn>;
  let downloadDocument: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let confirm: ReturnType<typeof vi.fn>;

  function create(documents: ProfileDocument[] = [LETTER, CV]): void {
    getDocuments.mockReturnValue(of(documents));
    fixture = TestBed.createComponent(DocumentsTabComponent);
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function chooseFiles(...files: File[]): void {
    const input = root().querySelector<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: files, configurable: true });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function uploadButton(): HTMLButtonElement {
    return Array.from(root().querySelectorAll<HTMLButtonElement>('.actions button')).find(b => b.textContent?.includes('Prześlij'))!;
  }

  beforeEach(() => {
    getDocuments = vi.fn();
    uploadDocuments = vi.fn();
    deleteDocument = vi.fn();
    downloadDocument = vi.fn().mockReturnValue(of(undefined));
    snackBarOpen = vi.fn();
    confirm = vi.fn().mockReturnValue(of(true));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      imports: [DocumentsTabComponent],
      providers: [
        { provide: AccountService, useValue: { getDocuments, uploadDocuments, deleteDocument, downloadDocument } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } },
        { provide: ConfirmService, useValue: { confirm } }
      ]
    });
  });

  afterEach(() => vi.restoreAllMocks());

  it('lists the documents sorted by kind with download buttons labelled with kind and name', () => {
    create();

    const labels = Array.from(root().querySelectorAll('.download-button')).map(b => b.getAttribute('aria-label'));
    expect(labels).toEqual(['Pobierz CV: cv.pdf', 'Pobierz List motywacyjny: list.docx']);
  });

  it('downloads and deletes through the account endpoints', () => {
    create();
    deleteDocument.mockReturnValue(of(undefined));

    root().querySelector<HTMLButtonElement>('.download-button')!.click();
    expect(downloadDocument).toHaveBeenCalledWith(CV);

    root().querySelector<HTMLButtonElement>('.delete-button')!.click();
    fixture.detectChanges();
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Usunąć dokument?', confirmLabel: 'Usuń', destructive: true }));
    expect(confirm.mock.calls[0][0].message).toContain('„cv.pdf” (CV)');
    expect(deleteDocument).toHaveBeenCalledWith(1);
    expect(root().querySelectorAll('.download-button').length).toBe(1);
    expect(snackBarOpen).toHaveBeenCalledWith('Dokument został usunięty', 'OK', { duration: 5000, politeness: 'polite' });
  });

  it('shows the empty state', () => {
    create([]);
    expect(root().textContent).toContain('Nie masz jeszcze dokumentów w profilu');
    // bez plików przycisk jest nieaktywny, ale zostaje w kolejności fokusu
    expect(uploadButton().getAttribute('aria-disabled')).toBe('true');
  });

  it('uploads the picked files, shows the progress and merges the returned list', () => {
    create([CV]);
    const events = new Subject<ProfileDocumentUploadEvent>();
    uploadDocuments.mockReturnValue(events);
    const file = new File(['%PDF-'], 'portfolio.pdf');

    chooseFiles(file);
    expect(uploadButton().getAttribute('aria-disabled')).toBeNull();
    uploadButton().click();
    fixture.detectChanges();

    expect(uploadDocuments).toHaveBeenCalledWith([{ file, kind: AttachmentKind.Other }]);
    events.next({ type: 'progress', percent: 40 });
    fixture.detectChanges();
    expect(root().querySelector('mat-progress-bar')).not.toBeNull();
    // w trakcie wysyłania nie usuwamy dokumentów
    expect(root().querySelector('.delete-button')).toBeNull();

    const added: ProfileDocument = { id: 3, kind: AttachmentKind.Other, fileName: 'portfolio.pdf', size: 5, uploadedAt: '2026-02-01T10:00:00Z' };
    events.next({ type: 'done', documents: [CV, added] });
    fixture.detectChanges();

    expect(root().querySelectorAll('.download-button').length).toBe(2);
    expect(snackBarOpen).toHaveBeenCalledWith('Dokument został dodany', 'OK', { duration: 5000, politeness: 'polite' });
    expect(root().querySelectorAll('app-attachments-picker .file').length).toBe(0);
  });

  it('applies the profile limits with profile wording (10 documents in total)', () => {
    create(Array.from({ length: 10 }, (_, i) => ({ ...CV, id: i + 1 })));

    expect(root().querySelector<HTMLInputElement>('input[type="file"]')!.disabled).toBe(true);
    expect(root().querySelector('app-attachments-picker .usage')?.textContent).toContain('plików: 10 z 10');
  });

  it('announces a translated upload error and keeps the picked files', () => {
    create([CV]);
    uploadDocuments.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, error: { message: 'Total size of profile documents cannot exceed 25 MB' }
    })));

    chooseFiles(new File(['x'], 'duzy.pdf'));
    uploadButton().click();
    fixture.detectChanges();

    expect(root().querySelector('app-attachments-picker [role="alert"]')?.textContent)
      .toContain('Łączny rozmiar dokumentów profilu nie może przekroczyć 25 MB');
    expect(root().querySelectorAll('app-attachments-picker .file').length).toBe(1);
  });

  it('cancels the upload when the page is left', () => {
    create([CV]);
    const events = new Subject<ProfileDocumentUploadEvent>();
    uploadDocuments.mockReturnValue(events);

    chooseFiles(new File(['x'], 'a.pdf'));
    uploadButton().click();
    fixture.destroy();

    expect(events.observed).toBe(false);
  });

  it('announces a loading error with retry', () => {
    getDocuments.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    fixture = TestBed.createComponent(DocumentsTabComponent);
    fixture.detectChanges();

    expect(root().querySelector('[role="alert"]')?.textContent).toContain('Nie udało się wczytać dokumentów');
  });
});
