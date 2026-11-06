import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { CompanyAutocompleteComponent, CompanyOption, CompanySource } from './company-autocomplete.component';
import { AdminService } from '../../services/admin.service';
import { CompanyService } from '../../services/company.service';
import { Company } from '../../models/company.model';
import { AdminCompanyParams, CompanyParams } from '../../models/company-params.model';
import { PaginatedResult } from '../../models/pagination.model';
import { companyPage, makeCompany } from '../../../testing/company-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('CompanyAutocompleteComponent', () => {
  let fixture: ComponentFixture<CompanyAutocompleteComponent>;
  let component: CompanyAutocompleteComponent;
  let publicGetCompanies: ReturnType<typeof vi.fn>;
  let adminGetCompanies: ReturnType<typeof vi.fn>;
  let emitted: (CompanyOption | null)[];

  function result(companies: Company[]): Observable<PaginatedResult<Company>> {
    return of({ ...companyPage(0, companies.length), result: companies });
  }

  function create(inputs: Record<string, unknown> = {}): void {
    fixture = TestBed.createComponent(CompanyAutocompleteComponent);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    component = fixture.componentInstance;
    emitted = [];
    component.companyChange.subscribe(value => emitted.push(value));
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function input(): HTMLInputElement {
    return root().querySelector<HTMLInputElement>('input')!;
  }

  function type(text: string): void {
    const el = input();
    el.value = text;
    el.dispatchEvent(new Event('input'));
  }

  function publicParams(): CompanyParams {
    return publicGetCompanies.mock.calls[publicGetCompanies.mock.calls.length - 1][0] as CompanyParams;
  }

  function adminParams(): AdminCompanyParams {
    return adminGetCompanies.mock.calls[adminGetCompanies.mock.calls.length - 1][0] as AdminCompanyParams;
  }

  beforeEach(async () => {
    publicGetCompanies = vi.fn().mockReturnValue(result([makeCompany(1, { name: 'Alfa' }), makeCompany(2, { name: 'Beta' })]));
    adminGetCompanies = vi.fn().mockReturnValue(result([makeCompany(3, { name: 'Gamma' })]));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [CompanyAutocompleteComponent],
      providers: [
        { provide: CompanyService, useValue: { getCompanies: publicGetCompanies } },
        { provide: AdminService, useValue: { getCompanies: adminGetCompanies } }
      ]
    }).compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  describe('suggestions source', () => {
    it('uses the public (approved-only) list by default', () => {
      vi.useFakeTimers();
      create();

      type('al');
      vi.advanceTimersByTime(300);

      expect(publicGetCompanies).toHaveBeenCalledTimes(1);
      expect(adminGetCompanies).not.toHaveBeenCalled();
      expect(publicParams().searchTerm).toBe('al');
      expect(publicParams().pageSize).toBe(10);
      expect(publicParams().pageNumber).toBe(1);
      expect(component.options()).toEqual([{ id: 1, name: 'Alfa' }, { id: 2, name: 'Beta' }]);
    });

    it('uses the admin list (every status, sorted by name) for source="all"', () => {
      vi.useFakeTimers();
      create({ source: 'all' satisfies CompanySource });

      type('ga');
      vi.advanceTimersByTime(300);

      expect(adminGetCompanies).toHaveBeenCalledTimes(1);
      expect(publicGetCompanies).not.toHaveBeenCalled();
      expect(adminParams().searchTerm).toBe('ga');
      expect(adminParams().orderBy).toBe('nameAsc');
      expect(adminParams().status).toBeNull();
      expect(adminParams().pageSize).toBe(10);
      expect(component.options()).toEqual([{ id: 3, name: 'Gamma' }]);
    });

    it('maps results to {id, name} only', () => {
      vi.useFakeTimers();
      create();

      type('a');
      vi.advanceTimersByTime(300);

      expect(Object.keys(component.options()[0]).sort()).toEqual(['id', 'name']);
    });
  });

  describe('debounce', () => {
    it('waits 300 ms of silence and sends only the last term, trimmed', () => {
      vi.useFakeTimers();
      create();

      type('a');
      vi.advanceTimersByTime(200);
      type('  ac  ');
      vi.advanceTimersByTime(299);
      expect(publicGetCompanies).not.toHaveBeenCalled();

      vi.advanceTimersByTime(1);

      expect(publicGetCompanies).toHaveBeenCalledTimes(1);
      expect(publicParams().searchTerm).toBe('ac');
    });

    it('focusing the empty field loads the first suggestions once', () => {
      vi.useFakeTimers();
      create();

      input().dispatchEvent(new Event('focus'));
      vi.advanceTimersByTime(300);
      expect(publicGetCompanies).toHaveBeenCalledTimes(1);
      expect(publicParams().searchTerm).toBe('');

      input().dispatchEvent(new Event('focus'));
      vi.advanceTimersByTime(300);
      expect(publicGetCompanies).toHaveBeenCalledTimes(1);
    });

    it('shows "Brak pasujących firm" after a search without results', () => {
      vi.useFakeTimers();
      publicGetCompanies.mockReturnValue(result([]));
      create();
      expect(component.searched()).toBe(false);

      type('zzz');
      vi.advanceTimersByTime(300);

      expect(component.options()).toEqual([]);
      expect(component.searched()).toBe(true);
    });

    it('keeps working (empty suggestions) when the request fails', () => {
      vi.useFakeTimers();
      publicGetCompanies.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
      create();

      type('a');
      vi.advanceTimersByTime(300);
      expect(component.options()).toEqual([]);

      publicGetCompanies.mockReturnValue(result([makeCompany(1, { name: 'Alfa' })]));
      type('al');
      vi.advanceTimersByTime(300);

      expect(component.options()).toEqual([{ id: 1, name: 'Alfa' }]);
    });
  });

  describe('selection', () => {
    it('emits the selected company (id and name)', () => {
      create();

      component.select({ id: 2, name: 'Beta' });

      expect(emitted).toEqual([{ id: 2, name: 'Beta' }]);
    });

    it('clicking an option in the panel emits it and shows its name in the input', async () => {
      create();
      input().focus();
      input().dispatchEvent(new Event('focus'));
      await new Promise(resolve => setTimeout(resolve, 350));
      fixture.detectChanges();
      await fixture.whenStable();
      input().dispatchEvent(new Event('focusin'));
      fixture.detectChanges();

      const options = Array.from(document.querySelectorAll<HTMLElement>('mat-option'));
      expect(options.map(o => o.textContent?.trim())).toEqual(['Alfa', 'Beta']);
      options[1].click();
      fixture.detectChanges();

      expect(emitted.at(-1)).toEqual({ id: 2, name: 'Beta' });
      expect(input().value).toBe('Beta');
    });

    it('typing after a selection invalidates it (emits null)', () => {
      vi.useFakeTimers();
      create();
      component.control.setValue({ id: 2, name: 'Beta' });
      emitted.length = 0;

      type('Bet');

      expect(emitted).toEqual([null]);
    });

    it('selecting an option (object value) does not emit null', () => {
      create();

      component.control.setValue({ id: 2, name: 'Beta' });

      expect(emitted).toEqual([]);
    });

    it('displayName renders option names, plain text and nulls', () => {
      create();

      expect(component.displayName({ id: 1, name: 'Alfa' })).toBe('Alfa');
      expect(component.displayName('abc')).toBe('abc');
      expect(component.displayName(null)).toBe('');
    });
  });

  describe('clear and reset', () => {
    it('the clear button appears when the field has a value and clears it (emits null)', () => {
      vi.useFakeTimers();
      create({ initial: { id: 1, name: 'Alfa' } });
      const clear = root().querySelector<HTMLButtonElement>('button[aria-label="Wyczyść firmę"]')!;
      expect(clear).not.toBeNull();

      clear.click();

      expect(component.control.value).toBe('');
      expect(emitted).toEqual([null]);
      expect(root().querySelector('button[aria-label="Wyczyść firmę"]')).toBeNull();
    });

    it('has no clear button for an empty field', () => {
      create();

      expect(root().querySelector('button[aria-label="Wyczyść firmę"]')).toBeNull();
    });

    it('reset clears the field without emitting', () => {
      create({ initial: { id: 1, name: 'Alfa' } });

      component.reset();

      expect(component.control.value).toBeNull();
      expect(emitted).toEqual([]);
    });
  });

  describe('initial value and required', () => {
    it('shows the initial company without emitting or searching', async () => {
      create({ initial: { id: 7, name: 'Zeta' } });
      await fixture.whenStable();
      await new Promise(resolve => setTimeout(resolve, 400));

      expect(input().value).toBe('Zeta');
      expect(emitted).toEqual([]);
      expect(publicGetCompanies).not.toHaveBeenCalled();
    });

    it('is valid without a selection when not required', () => {
      create();

      expect(component.control.valid).toBe(true);
    });

    it('required: invalid until a company object is chosen, plain text is not enough', () => {
      create({ required: true });
      expect(component.control.hasError('companyRequired')).toBe(true);

      component.control.setValue('Alf', { emitEvent: false });
      component.control.updateValueAndValidity({ emitEvent: false });
      expect(component.control.hasError('companyRequired')).toBe(true);

      component.control.setValue({ id: 1, name: 'Alfa' });
      expect(component.control.valid).toBe(true);
    });

    it('required with an initial company starts valid', () => {
      create({ required: true, initial: { id: 1, name: 'Alfa' } });

      expect(component.control.valid).toBe(true);
    });

    it('shows the custom required message once the field is touched', () => {
      create({ required: true, requiredMessage: 'Wybierz firmę ogłoszenia' });

      component.control.markAsTouched();

      expect(root().textContent).toContain('Wybierz firmę ogłoszenia');
    });

    it('uses the given label and placeholder', () => {
      create({ label: 'Filtruj po firmie', placeholder: 'Wpisz nazwę' });

      expect(root().querySelector('mat-label')?.textContent).toBe('Filtruj po firmie');
      expect(input().placeholder).toBe('Wpisz nazwę');
    });
  });
});
