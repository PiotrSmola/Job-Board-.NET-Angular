import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Subject, throwError } from 'rxjs';
import { ATTACHMENTS_DIALOG_CONFIG, ATTACHMENTS_UPLOAD_ABORTED, AttachmentsDialogComponent } from './attachments-dialog.component';
import { JobApplicationService } from '../../services/job-application.service';
import {
  ApplicationStatus, AttachmentKind, AttachmentUploadEvent, AttachmentUploadItem, JobApplication
} from '../../models/job-application.model';

const APPLICATION: JobApplication = {
  id: 7, jobOfferId: 3, userId: 'u1', userEmail: 'jan@example.com', userFullName: 'Jan Kowalski',
  jobTitle: 'Programista Angular', company: 'Acme', companyId: 1, note: null, appliedAt: new Date('2026-01-01T10:00:00Z'),
  status: ApplicationStatus.Submitted, statusChangedAt: '2026-01-01T10:00:00Z',
  attachments: [], attachmentsTotalSize: 0
};

const WITH_ATTACHMENTS: JobApplication = {
  ...APPLICATION,
  attachments: [{ id: 1, kind: AttachmentKind.Cv, fileName: 'stare-cv.pdf', size: 2048, uploadedAt: '2026-01-02T10:00:00Z' }],
  attachmentsTotalSize: 2048
};

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('AttachmentsDialogComponent', () => {
  let fixture: ComponentFixture<AttachmentsDialogComponent>;
  let component: AttachmentsDialogComponent;
  let uploadAttachments: ReturnType<typeof vi.fn>;
  let keydown: Subject<KeyboardEvent>;
  let backdrop: Subject<MouseEvent>;
  let dialogRef: {
    close: ReturnType<typeof vi.fn>; disableClose: boolean | undefined;
    keydownEvents: () => Subject<KeyboardEvent>; backdropClick: () => Subject<MouseEvent>;
  };
  let events: Subject<AttachmentUploadEvent>;

  const ITEMS: AttachmentUploadItem[] = [
    { file: new File(['%PDF-'], 'nowe-cv.pdf', { type: 'application/pdf' }), kind: AttachmentKind.Cv },
    { file: new File(['PK'], 'list.docx'), kind: AttachmentKind.CoverLetter }
  ];

  function create(data: JobApplication = APPLICATION): void {
    TestBed.overrideProvider(MAT_DIALOG_DATA, { useValue: data });
    fixture = TestBed.createComponent(AttachmentsDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function uploadButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('.upload-button')!;
  }

  function escape(): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: 'Escape', cancelable: true });
    keydown.next(event);
    return event;
  }

  beforeEach(async () => {
    events = new Subject<AttachmentUploadEvent>();
    uploadAttachments = vi.fn().mockReturnValue(events);
    keydown = new Subject<KeyboardEvent>();
    backdrop = new Subject<MouseEvent>();
    dialogRef = { close: vi.fn(), disableClose: undefined, keydownEvents: () => keydown, backdropClick: () => backdrop };
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [AttachmentsDialogComponent],
      providers: [
        { provide: JobApplicationService, useValue: { uploadAttachments } },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MAT_DIALOG_DATA, useValue: APPLICATION }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('is opened as an ARIA modal dialog wide enough for the file list', () => {
    expect(ATTACHMENTS_DIALOG_CONFIG).toEqual(expect.objectContaining({ ariaModal: true, maxWidth: '95vw' }));
  });

  it('offers adding attachments for the application', () => {
    create();

    expect(root().querySelector('h2')?.textContent).toContain('Dodaj załączniki');
    expect(root().querySelector('.summary')?.textContent).toContain('Programista Angular');
    expect(root().querySelector('.current')).toBeNull();
    expect(uploadButton().textContent).toContain('Prześlij');
  });

  it('mentions the existing attachments and counts them in the picker limits', () => {
    create(WITH_ATTACHMENTS);

    expect(root().querySelector('.current')?.textContent).toContain('Aplikacja ma już załączników: 1');
    expect(root().querySelector('.usage')?.textContent).toContain('plików: 1 z 10');
  });

  it('handles Escape and backdrop clicks itself (disableClose)', () => {
    create();

    expect(dialogRef.disableClose).toBe(true);
  });

  it('disables uploading until a file is chosen', () => {
    create();

    expect(uploadButton().disabled).toBe(true);

    component.onItemsChange(ITEMS);
    expect(uploadButton().disabled).toBe(false);
  });

  it('uploads all files in one request, shows the progress and closes with the updated application', () => {
    create();
    component.onItemsChange(ITEMS);

    component.upload();

    expect(uploadAttachments).toHaveBeenCalledTimes(1);
    expect(uploadAttachments).toHaveBeenCalledWith(7, ITEMS);
    expect(uploadButton().disabled).toBe(true);
    expect(root().querySelector('.cancel-button')?.textContent).toContain('Przerwij wysyłanie');
    expect(root().querySelector('[role="status"]')?.textContent).toBe('Wysyłanie załączników…');

    events.next({ type: 'progress', percent: 30 });
    expect(root().querySelector('mat-progress-bar')?.getAttribute('aria-valuenow')).toBe('30');

    const updated = { ...WITH_ATTACHMENTS };
    events.next({ type: 'done', application: updated });

    expect(dialogRef.close).toHaveBeenCalledWith(updated);
    expect(component.items()).toEqual([]);
  });

  it('does not start a second upload while one is in progress', () => {
    create();
    component.onItemsChange(ITEMS);

    component.upload();
    component.upload();

    expect(uploadAttachments).toHaveBeenCalledTimes(1);
  });

  it('cancels the upload when the dialog is closed (component destroyed)', () => {
    create();
    component.onItemsChange(ITEMS);
    component.upload();
    expect(events.observed).toBe(true);

    fixture.destroy();

    expect(events.observed).toBe(false);
  });

  it('closes without a result on "Anuluj" when nothing is being uploaded', () => {
    create();
    component.onItemsChange(ITEMS);

    root().querySelector<HTMLButtonElement>('.cancel-button')!.click();

    expect(dialogRef.close).toHaveBeenCalledWith(undefined);
    expect(component.items()).toEqual([]);
  });

  it('"Przerwij wysyłanie" cancels the request at once and reports the abort to the parent', () => {
    create();
    component.onItemsChange(ITEMS);
    component.upload();
    events.next({ type: 'progress', percent: 100 });

    root().querySelector<HTMLButtonElement>('.cancel-button')!.click();

    expect(events.observed).toBe(false);
    expect(dialogRef.close).toHaveBeenCalledWith(ATTACHMENTS_UPLOAD_ABORTED);
  });

  it('Escape during the upload cancels the request and closes the dialog', () => {
    create();
    component.onItemsChange(ITEMS);
    component.upload();

    const event = escape();

    expect(event.defaultPrevented).toBe(true);
    expect(events.observed).toBe(false);
    expect(dialogRef.close).toHaveBeenCalledWith(ATTACHMENTS_UPLOAD_ABORTED);
  });

  it('Escape without an upload closes the dialog without a result', () => {
    create();

    escape();

    expect(dialogRef.close).toHaveBeenCalledWith(undefined);
  });

  it('ignores keys other than Escape', () => {
    create();

    keydown.next(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('does not abort the upload on an accidental backdrop click', () => {
    create();
    component.onItemsChange(ITEMS);
    component.upload();

    backdrop.next(new MouseEvent('click'));

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(events.observed).toBe(true);
  });

  it('closes on a backdrop click when nothing is being uploaded', () => {
    create();

    backdrop.next(new MouseEvent('click'));

    expect(dialogRef.close).toHaveBeenCalledWith(undefined);
  });

  it('shows the translated API error in the picker, announces the abort and lets the user retry', () => {
    create();
    uploadAttachments.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, error: { message: 'Total size of attachments cannot exceed 25 MB' }
    })));
    component.onItemsChange(ITEMS);

    component.upload();

    const error = root().querySelector('app-attachments-picker .error');
    expect(error?.textContent).toContain('Łączny rozmiar załączników nie może przekroczyć 25 MB');
    expect(error?.getAttribute('role')).toBe('alert');
    expect(root().querySelector('[role="status"]')?.textContent).toBe('Przerwano wysyłanie załączników');
    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.uploading()).toBe(false);
    expect(component.items()).toEqual(ITEMS);
    expect(uploadButton().disabled).toBe(false);
  });

  it('clears the API error and the final announcement when the selection changes', () => {
    create();
    uploadAttachments.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'File is empty' } })));
    component.onItemsChange(ITEMS);
    component.upload();

    component.onItemsChange(ITEMS.slice(0, 1));

    expect(component.error()).toBeNull();
    expect(component.statusMessage()).toBeNull();
    expect(root().querySelector('app-attachments-picker .error')).toBeNull();
  });

  it.each([
    [500, null, 'Nie udało się przesłać załączników'],
    [429, { message: 'Too many requests - try again later' }, 'Zbyt wiele żądań – spróbuj ponownie za kilka minut'],
    [403, null, 'Brak uprawnień do dodania załączników']
  ])('shows a Polish message for a %i response', (status, body, expected) => {
    create();
    uploadAttachments.mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: body })));
    component.onItemsChange(ITEMS);

    component.upload();

    expect(root().querySelector('app-attachments-picker .error')?.textContent).toContain(expected);
  });
});
