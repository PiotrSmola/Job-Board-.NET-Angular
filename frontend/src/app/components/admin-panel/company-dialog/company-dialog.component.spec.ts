import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, of, throwError } from 'rxjs';
import { CompanyDialogComponent } from './company-dialog.component';
import { AdminService } from '../../../services/admin.service';
import { Company } from '../../../models/company.model';
import { makeCompany } from '../../../../testing/company-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('CompanyDialogComponent', () => {
  let fixture: ComponentFixture<CompanyDialogComponent>;
  let component: CompanyDialogComponent;
  let createCompany: ReturnType<typeof vi.fn>;
  let updateCompany: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;

  async function create(data: Company | null = null): Promise<void> {
    TestBed.overrideProvider(MAT_DIALOG_DATA, { useValue: data });
    fixture = TestBed.createComponent(CompanyDialogComponent);
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
    return root().querySelector<HTMLButtonElement>('button[mat-raised-button]')!;
  }

  function fill(name: string, value: string): void {
    component.form.get(name)!.setValue(value);
    component.form.get(name)!.markAsTouched();
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({ status, statusText: 'Error', error });
  }

  beforeEach(async () => {
    createCompany = vi.fn().mockImplementation((body: { name: string }) => of(makeCompany(50, { name: body.name })));
    updateCompany = vi.fn().mockImplementation((id: number, body: { name: string }) => of(makeCompany(id, { name: body.name })));
    close = vi.fn();
    snackBarOpen = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [CompanyDialogComponent],
      providers: [
        { provide: AdminService, useValue: { createCompany, updateCompany } },
        { provide: MatDialogRef, useValue: { close } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } },
        { provide: MAT_DIALOG_DATA, useValue: null }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  describe('add mode', () => {
    it('shows the add title, empty form and the auto-approval hint', async () => {
      await create(null);

      const text = root().textContent ?? '';
      expect(text).toContain('Dodaj firmę');
      expect(text).toContain('od razu zatwierdzona');
      expect(component.form.value).toEqual({ name: '', description: '', website: '', location: '' });
      expect(submitButton().textContent).toContain('Dodaj');
    });

    it('disables submit until the name is filled in', async () => {
      await create(null);
      expect(submitButton().disabled).toBe(true);

      fill('name', 'Acme');

      expect(submitButton().disabled).toBe(false);
    });

    it('creates the company with trimmed values and null for blank optional fields, then closes with the result', async () => {
      await create(null);
      fill('name', '  Acme Sp. z o.o.  ');
      fill('website', 'https://acme.example.com');
      fill('location', '   ');
      fill('description', '');

      component.onSubmit();

      expect(createCompany).toHaveBeenCalledWith({
        name: 'Acme Sp. z o.o.',
        description: null,
        website: 'https://acme.example.com',
        location: null
      });
      expect(updateCompany).not.toHaveBeenCalled();
      expect(close).toHaveBeenCalledWith(expect.objectContaining({ id: 50, name: 'Acme Sp. z o.o.' }));
      expect(snackBarOpen).toHaveBeenCalledWith('Firma dodana', 'OK', expect.anything());
    });

    it('submits through the button click', async () => {
      await create(null);
      fill('name', 'Acme');

      submitButton().click();

      expect(createCompany).toHaveBeenCalledTimes(1);
    });
  });

  describe('edit mode', () => {
    const existing = (): Company => makeCompany(12, {
      name: 'Stara nazwa', description: 'Opis', website: 'http://stara.pl', location: 'Kraków'
    });

    it('shows the edit title, prefilled values and no auto-approval hint', async () => {
      await create(existing());

      const text = root().textContent ?? '';
      expect(text).toContain('Edytuj firmę');
      expect(text).not.toContain('od razu zatwierdzona');
      expect(component.form.value).toEqual({ name: 'Stara nazwa', description: 'Opis', website: 'http://stara.pl', location: 'Kraków' });
      expect(submitButton().textContent).toContain('Zapisz');
    });

    it('updates the existing company by id and closes with the result', async () => {
      await create(existing());
      fill('name', 'Nowa nazwa');

      component.onSubmit();

      expect(updateCompany).toHaveBeenCalledWith(12, {
        name: 'Nowa nazwa', description: 'Opis', website: 'http://stara.pl', location: 'Kraków'
      });
      expect(createCompany).not.toHaveBeenCalled();
      expect(close).toHaveBeenCalledWith(expect.objectContaining({ id: 12, name: 'Nowa nazwa' }));
      expect(snackBarOpen).toHaveBeenCalledWith('Dane firmy zapisane', 'OK', expect.anything());
    });

    it('allows clearing optional fields (sent as null)', async () => {
      await create(existing());
      fill('website', '');
      fill('description', '');

      component.onSubmit();

      expect(updateCompany).toHaveBeenCalledWith(12, expect.objectContaining({ website: null, description: null }));
    });
  });

  describe('validation', () => {
    it('requires the company name and shows the error once touched', async () => {
      await create(null);
      fill('name', '');
      component.form.get('name')!.markAsTouched();

      expect(component.form.get('name')!.hasError('required')).toBe(true);
      expect(root().textContent).toContain('Nazwa firmy jest wymagana');
    });

    it('rejects a whitespace-only name', async () => {
      await create(null);

      fill('name', '    ');

      expect(component.form.get('name')!.valid).toBe(false);
      expect(submitButton().disabled).toBe(true);
    });

    it('rejects a name longer than 200 characters and accepts exactly 200', async () => {
      await create(null);

      fill('name', 'a'.repeat(201));
      expect(component.form.get('name')!.hasError('maxlength')).toBe(true);

      fill('name', 'a'.repeat(200));
      expect(component.form.get('name')!.valid).toBe(true);
    });

    it.each([
      ['https://firma.pl'],
      ['http://firma.pl'],
      ['HTTPS://FIRMA.PL/kariera?x=1#a'],
      ['http://localhost:4200/x']
    ])('accepts the website "%s"', async website => {
      await create(null);
      fill('name', 'Acme');

      fill('website', website);

      expect(component.form.get('website')!.valid).toBe(true);
      expect(submitButton().disabled).toBe(false);
    });

    it.each([
      ['javascript:alert(1)'],
      ['ftp://firma.pl'],
      ['firma.pl'],
      ['www.firma.pl'],
      ['https://'],
      ['https:// spacja.pl'],
      ['data:text/html,<b>x</b>']
    ])('rejects the website "%s" and blocks saving', async website => {
      await create(null);
      fill('name', 'Acme');

      fill('website', website);

      expect(component.form.get('website')!.valid).toBe(false);
      expect(submitButton().disabled).toBe(true);
      expect(root().textContent).toContain('Podaj adres zaczynający się od http:// lub https://');
    });

    it('treats an empty website as valid (optional)', async () => {
      await create(null);

      fill('website', '');

      expect(component.form.get('website')!.valid).toBe(true);
    });

    it('limits website to 300, location to 200 and description to 5000 characters', async () => {
      await create(null);

      fill('website', 'https://a.pl/' + 'x'.repeat(300));
      fill('location', 'l'.repeat(201));
      fill('description', 'd'.repeat(5001));

      expect(component.form.get('website')!.hasError('maxlength')).toBe(true);
      expect(component.form.get('location')!.hasError('maxlength')).toBe(true);
      expect(component.form.get('description')!.hasError('maxlength')).toBe(true);
    });

    it('shows the description counter', async () => {
      await create(null);

      fill('description', 'Krótki opis');

      expect(root().textContent).toContain('11 / 5000');
    });

    it('does not call the API when the form is invalid', async () => {
      await create(null);
      fill('name', '');

      component.onSubmit();

      expect(createCompany).not.toHaveBeenCalled();
      expect(close).not.toHaveBeenCalled();
    });
  });

  describe('submitting', () => {
    it('ignores a second submit while the first one is in flight and disables buttons', async () => {
      await create(null);
      createCompany.mockReturnValue(new Subject<Company>());
      fill('name', 'Acme');

      component.onSubmit();
      component.onSubmit();

      expect(createCompany).toHaveBeenCalledTimes(1);
      expect(component.submitting()).toBe(true);
      expect(submitButton().disabled).toBe(true);
      expect(root().querySelector('mat-dialog-actions mat-spinner')).not.toBeNull();
    });

    it('translates the "name already taken" error and keeps the dialog open', async () => {
      await create(null);
      createCompany.mockReturnValue(throwError(() => httpError(400, { message: 'Company name is already taken' })));
      fill('name', 'Acme');

      component.onSubmit();

      expect(snackBarOpen).toHaveBeenCalledWith('Ta nazwa firmy jest już zajęta', 'OK', expect.anything());
      expect(close).not.toHaveBeenCalled();
      expect(component.submitting()).toBe(false);
    });

    it('shows a generic message for a server error', async () => {
      await create(null);
      createCompany.mockReturnValue(throwError(() => httpError(500)));
      fill('name', 'Acme');

      component.onSubmit();

      expect(snackBarOpen).toHaveBeenCalledWith('Błąd podczas zapisywania firmy', 'OK', expect.anything());
    });

    it.each([401, 403])('does not show its own message when the interceptor handles %i', async status => {
      await create(null);
      createCompany.mockReturnValue(throwError(() => httpError(status)));
      fill('name', 'Acme');

      component.onSubmit();

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(component.submitting()).toBe(false);
    });
  });
});
