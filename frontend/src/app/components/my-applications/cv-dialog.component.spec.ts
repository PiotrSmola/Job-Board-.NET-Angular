import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Subject, throwError } from 'rxjs';
import { CvDialogComponent } from './cv-dialog.component';
import { JobApplicationService } from '../../services/job-application.service';
import { ApplicationStatus, CvUploadEvent, JobApplication } from '../../models/job-application.model';

const APPLICATION: JobApplication = {
  id: 7, jobOfferId: 3, userId: 'u1', userEmail: 'jan@example.com', userFullName: 'Jan Kowalski',
  jobTitle: 'Programista Angular', company: 'Acme', companyId: 1, note: null, appliedAt: new Date('2026-01-01T10:00:00Z'),
  status: ApplicationStatus.Submitted, statusChangedAt: '2026-01-01T10:00:00Z',
  hasCv: false, cvFileName: null, cvSize: null, cvUploadedAt: null
};

const WITH_CV: JobApplication = {
  ...APPLICATION, hasCv: true, cvFileName: 'stare-cv.pdf', cvSize: 2048, cvUploadedAt: '2026-01-02T10:00:00Z'
};

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('CvDialogComponent', () => {
  let fixture: ComponentFixture<CvDialogComponent>;
  let component: CvDialogComponent;
  let uploadCv: ReturnType<typeof vi.fn>;
  let dialogRef: { close: ReturnType<typeof vi.fn>; disableClose: boolean | undefined };
  let events: Subject<CvUploadEvent>;

  const FILE = new File(['%PDF-'], 'nowe-cv.pdf', { type: 'application/pdf' });

  function create(data: JobApplication = APPLICATION): void {
    TestBed.overrideProvider(MAT_DIALOG_DATA, { useValue: data });
    fixture = TestBed.createComponent(CvDialogComponent);
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

  beforeEach(async () => {
    events = new Subject<CvUploadEvent>();
    uploadCv = vi.fn().mockReturnValue(events);
    dialogRef = { close: vi.fn(), disableClose: undefined };
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [CvDialogComponent],
      providers: [
        { provide: JobApplicationService, useValue: { uploadCv } },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MAT_DIALOG_DATA, useValue: APPLICATION }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('offers adding a CV for an application without one', () => {
    create();

    expect(root().querySelector('h2')?.textContent).toContain('Dodaj CV');
    expect(root().querySelector('.current')).toBeNull();
    expect(uploadButton().textContent).toContain('Prześlij CV');
  });

  it('shows the current CV and offers replacing it', () => {
    create(WITH_CV);

    const current = root().querySelector('.current')?.textContent ?? '';
    expect(root().querySelector('h2')?.textContent).toContain('Zmień CV');
    expect(current).toContain('stare-cv.pdf');
    expect(current).toContain('2 KB');
    expect(current).toContain('02.01.2026');
    expect(uploadButton().textContent).toContain('Zastąp CV');
  });

  it('disables uploading until a file is chosen', () => {
    create();

    expect(uploadButton().disabled).toBe(true);

    component.file.set(FILE);
    expect(uploadButton().disabled).toBe(false);
  });

  it('uploads the file, shows the progress and closes with the updated application', () => {
    create();
    component.file.set(FILE);

    component.upload();

    expect(uploadCv).toHaveBeenCalledWith(7, FILE);
    expect(dialogRef.disableClose).toBe(true);
    expect(uploadButton().disabled).toBe(true);
    expect(root().querySelector('.cancel-button')?.textContent).toContain('Przerwij wysyłanie');

    events.next({ type: 'progress', percent: 30 });
    expect(root().querySelector('mat-progress-bar')?.getAttribute('aria-valuenow')).toBe('30');

    const updated = { ...WITH_CV, cvFileName: 'nowe-cv.pdf' };
    events.next({ type: 'done', application: updated });

    expect(dialogRef.close).toHaveBeenCalledWith(updated);
    expect(component.file()).toBeNull();
  });

  it('does not start a second upload while one is in progress', () => {
    create();
    component.file.set(FILE);

    component.upload();
    component.upload();

    expect(uploadCv).toHaveBeenCalledTimes(1);
  });

  it('cancels the upload when the dialog is closed (component destroyed)', () => {
    create();
    component.file.set(FILE);
    component.upload();
    expect(events.observed).toBe(true);

    fixture.destroy();

    expect(events.observed).toBe(false);
  });

  it('shows the translated API error in the picker and lets the user retry', () => {
    create();
    uploadCv.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, error: { message: 'Unsupported file type - upload a PDF or DOCX file' }
    })));
    component.file.set(FILE);

    component.upload();

    const error = root().querySelector('app-cv-upload .error');
    expect(error?.textContent).toContain('Nieobsługiwany typ pliku – prześlij plik PDF lub DOCX');
    expect(error?.getAttribute('role')).toBe('alert');
    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(dialogRef.disableClose).toBe(false);
    expect(component.uploading()).toBe(false);
    expect(uploadButton().disabled).toBe(false);
  });

  it('shows a generic message for server errors', () => {
    create();
    uploadCv.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.file.set(FILE);

    component.upload();

    expect(root().querySelector('app-cv-upload .error')?.textContent).toContain('Nie udało się przesłać CV');
  });
});
