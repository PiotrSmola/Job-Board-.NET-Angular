import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';
import { AdminPendingCompaniesComponent } from './admin-pending-companies.component';
import { RejectCompanyDialogComponent } from '../reject-company-dialog/reject-company-dialog.component';
import { AdminService } from '../../../services/admin.service';
import { CompanyStatus } from '../../../models/company.model';
import { companyPage, makeCompany } from '../../../../testing/company-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('AdminPendingCompaniesComponent', () => {
  let fixture: ComponentFixture<AdminPendingCompaniesComponent>;
  let getCompanies: ReturnType<typeof vi.fn>;
  let approveCompany: ReturnType<typeof vi.fn>;
  let dialogOpen: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let changes: number;

  function create(): void {
    fixture = TestBed.createComponent(AdminPendingCompaniesComponent);
    changes = 0;
    fixture.componentInstance.changed.subscribe(() => changes++);
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    getCompanies = vi.fn().mockReturnValue(of({
      ...companyPage(0, 12),
      result: [
        makeCompany(1, { name: 'Nowa Firma', status: CompanyStatus.Pending, website: 'https://nowafirma.pl' }),
        makeCompany(2, { name: 'GreenGrid', status: CompanyStatus.Pending, website: 'javascript:alert(1)' })
      ]
    }));
    approveCompany = vi.fn().mockImplementation((id: number) => of(makeCompany(id, { status: CompanyStatus.Approved })));
    dialogOpen = vi.fn().mockReturnValue({ afterClosed: () => of(undefined) });
    snackBarOpen = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [AdminPendingCompaniesComponent],
      providers: [
        provideRouter([]),
        { provide: AdminService, useValue: { getCompanies, approveCompany } },
        { provide: MatDialog, useValue: { open: dialogOpen } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('requests the oldest pending companies and shows the total', () => {
    create();

    expect(getCompanies).toHaveBeenCalledWith(expect.objectContaining({ status: CompanyStatus.Pending, orderBy: 'oldest', pageSize: 10 }));
    expect(root().querySelector('h2')?.textContent).toContain('Firmy do weryfikacji');
    expect(root().querySelector('h2')?.textContent).toContain('(12)');
    expect(root().querySelector('table')?.getAttribute('aria-label')).toBe('Firmy do weryfikacji');
    expect(root().textContent).toContain('pozostałe w zakładce Firmy');
  });

  it('links only safe websites', () => {
    create();

    const links = Array.from(root().querySelectorAll<HTMLAnchorElement>('a.text-link')).map(a => a.getAttribute('href'));
    expect(links).toEqual(['https://nowafirma.pl']);
    expect(root().textContent).toContain('brak');
  });

  it('approves a company, reloads the list and notifies the parent', () => {
    create();

    root().querySelector<HTMLButtonElement>('button[aria-label="Zatwierdź firmę Nowa Firma"]')!.click();

    expect(approveCompany).toHaveBeenCalledWith(1);
    expect(getCompanies).toHaveBeenCalledTimes(2);
    expect(changes).toBe(1);
    expect(snackBarOpen).toHaveBeenCalledWith(expect.stringContaining('została zatwierdzona'), 'OK', expect.objectContaining({ politeness: 'polite' }));
  });

  it('opens the reject dialog and reloads only after a rejection', () => {
    create();

    root().querySelector<HTMLButtonElement>('button[aria-label="Odrzuć firmę GreenGrid"]')!.click();
    expect(dialogOpen).toHaveBeenCalledWith(RejectCompanyDialogComponent, expect.objectContaining({ maxWidth: '95vw' }));
    expect(getCompanies).toHaveBeenCalledTimes(1);

    dialogOpen.mockReturnValue({ afterClosed: () => of(makeCompany(2, { status: CompanyStatus.Rejected })) });
    root().querySelector<HTMLButtonElement>('button[aria-label="Odrzuć firmę GreenGrid"]')!.click();
    expect(getCompanies).toHaveBeenCalledTimes(2);
    expect(changes).toBe(1);
  });

  it('shows an empty state and a load error with retry', () => {
    getCompanies.mockReturnValue(of(companyPage(0, 0)));
    create();
    expect(root().textContent).toContain('Brak firm oczekujących na weryfikację');

    getCompanies.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    fixture.componentInstance.reload();
    expect(root().querySelector('[role="alert"]')?.textContent).toContain('Nie udało się wczytać firm do weryfikacji');
  });
});
