import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, of, throwError } from 'rxjs';
import { REJECTION_REASON_MAX, RejectCompanyDialogComponent } from './reject-company-dialog.component';
import { AdminService } from '../../../services/admin.service';
import { Company, CompanyStatus } from '../../../models/company.model';
import { makeCompany } from '../../../../testing/company-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('RejectCompanyDialogComponent', () => {
  let fixture: ComponentFixture<RejectCompanyDialogComponent>;
  let component: RejectCompanyDialogComponent;
  let rejectCompany: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;

  async function create(): Promise<void> {
    fixture = TestBed.createComponent(RejectCompanyDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function submitButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('button.submit-button')!;
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({ status, statusText: 'Error', error });
  }

  beforeEach(async () => {
    rejectCompany = vi.fn().mockImplementation((id: number) => of(makeCompany(id, { name: 'Acme', status: CompanyStatus.Rejected })));
    close = vi.fn();
    snackBarOpen = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [RejectCompanyDialogComponent],
      providers: [
        { provide: AdminService, useValue: { rejectCompany } },
        { provide: MatDialogRef, useValue: { close } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } },
        { provide: MAT_DIALOG_DATA, useValue: makeCompany(8, { name: 'Acme' }) }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('names the company in the title and explains who sees the reason', async () => {
    await create();

    const text = root().textContent ?? '';
    expect(text).toContain('Odrzuć firmę „Acme”');
    expect(text).toContain('Powód zobaczy pracodawca');
  });

  it('starts with an empty reason and a disabled submit button', async () => {
    await create();

    expect(component.reason.value).toBe('');
    expect(submitButton().disabled).toBe(true);
    expect(root().textContent).toContain('0 / 1000');
  });

  describe('reason validation', () => {
    it('requires a reason and shows the error once touched', async () => {
      await create();

      component.reason.markAsTouched();

      expect(component.reason.hasError('required')).toBe(true);
      expect(root().textContent).toContain('Podaj powód odrzucenia');
    });

    it.each([[' '], ['     '], ['\n\t ']])('rejects a whitespace-only reason %j', async reason => {
      await create();

      component.reason.setValue(reason);

      expect(component.reason.invalid).toBe(true);
      expect(submitButton().disabled).toBe(true);
    });

    it('enables submit for a non-blank reason', async () => {
      await create();

      component.reason.setValue('Brak danych kontaktowych');

      expect(submitButton().disabled).toBe(false);
    });

    it('accepts exactly 1000 characters and rejects 1001', async () => {
      await create();
      expect(REJECTION_REASON_MAX).toBe(1000);

      component.reason.setValue('a'.repeat(1000));
      expect(component.reason.valid).toBe(true);

      component.reason.setValue('a'.repeat(1001));
      expect(component.reason.hasError('maxlength')).toBe(true);
      expect(submitButton().disabled).toBe(true);
    });

    it('limits the textarea to 1000 characters and updates the counter', async () => {
      await create();

      component.reason.setValue('Powód');

      expect(root().querySelector<HTMLTextAreaElement>('textarea')!.maxLength).toBe(1000);
      expect(root().textContent).toContain('5 / 1000');
    });

    it('does not call the API for an invalid reason', async () => {
      await create();

      component.onSubmit();

      expect(rejectCompany).not.toHaveBeenCalled();
      expect(close).not.toHaveBeenCalled();
    });
  });

  describe('submitting', () => {
    it('sends the trimmed reason for the company and closes with the updated company', async () => {
      await create();
      component.reason.setValue('  Niepełne dane firmy  ');

      component.onSubmit();

      expect(rejectCompany).toHaveBeenCalledWith(8, 'Niepełne dane firmy');
      expect(close).toHaveBeenCalledWith(expect.objectContaining({ id: 8, status: CompanyStatus.Rejected }));
      expect(snackBarOpen).toHaveBeenCalledWith('Firma „Acme” została odrzucona', 'OK', expect.anything());
    });

    it('submits through the button click', async () => {
      await create();
      component.reason.setValue('Powód');

      submitButton().click();

      expect(rejectCompany).toHaveBeenCalledTimes(1);
    });

    it('ignores a second submit while the first one is in flight', async () => {
      await create();
      rejectCompany.mockReturnValue(new Subject<Company>());
      component.reason.setValue('Powód');

      component.onSubmit();
      component.onSubmit();

      expect(rejectCompany).toHaveBeenCalledTimes(1);
      expect(component.submitting()).toBe(true);
      expect(submitButton().disabled).toBe(true);
    });

    it('shows the API message and keeps the dialog open on a client error', async () => {
      await create();
      rejectCompany.mockReturnValue(throwError(() => httpError(404, { message: 'Company not found' })));
      component.reason.setValue('Powód');

      component.onSubmit();

      expect(snackBarOpen).toHaveBeenCalledWith('Firma nie została znaleziona', 'OK', expect.anything());
      expect(close).not.toHaveBeenCalled();
      expect(component.submitting()).toBe(false);
    });

    it('shows a generic message for a server error', async () => {
      await create();
      rejectCompany.mockReturnValue(throwError(() => httpError(500)));
      component.reason.setValue('Powód');

      component.onSubmit();

      expect(snackBarOpen).toHaveBeenCalledWith('Błąd podczas odrzucania firmy', 'OK', expect.anything());
    });

    it.each([401, 403])('does not show its own message when the interceptor handles %i', async status => {
      await create();
      rejectCompany.mockReturnValue(throwError(() => httpError(status)));
      component.reason.setValue('Powód');

      component.onSubmit();

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(component.submitting()).toBe(false);
    });
  });
});
