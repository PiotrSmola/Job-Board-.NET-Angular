import { ChangeDetectorRef, Component, OnInit, inject, input, output, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, ReactiveFormsModule, ValidationErrors } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Observable, Subject, catchError, debounceTime, map, of, switchMap } from 'rxjs';
import { Company } from '../../models/company.model';
import { AdminCompanyParams, CompanyParams } from '../../models/company-params.model';
import { AdminService } from '../../services/admin.service';
import { CompanyService } from '../../services/company.service';

/** Wybrana firma – wystarczy id i nazwa (np. z oferty edytowanej w dialogu). */
export type CompanyOption = Pick<Company, 'id' | 'name'>;

/**
 * Źródło podpowiedzi: `approved` – publiczna lista (tylko zatwierdzone),
 * `all` – lista admina (każdy status).
 */
export type CompanySource = 'approved' | 'all';

const SUGGESTIONS_LIMIT = 10;

function isCompanyOption(value: unknown): value is CompanyOption {
  return !!value && typeof value === 'object' && 'id' in value;
}

/** Pole tekstowe wymaga wybrania firmy z listy (sam wpisany tekst nie wystarcza). */
function companySelectedValidator(control: AbstractControl): ValidationErrors | null {
  return isCompanyOption(control.value) ? null : { companyRequired: true };
}

/** Pole z podpowiedziami firm; emituje wybraną firmę albo null po wyczyszczeniu / zmianie tekstu. */
@Component({
  selector: 'app-company-autocomplete',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule
  ],
  template: `
    <mat-form-field appearance="outline" class="company-field">
      <mat-label>{{ label() }}</mat-label>
      <input matInput [formControl]="control" [matAutocomplete]="auto" [placeholder]="placeholder()" (focus)="onFocus()">
      <mat-icon matPrefix aria-hidden="true">business</mat-icon>
      @if (control.value) {
        <button mat-icon-button matSuffix type="button" (click)="clear()" aria-label="Wyczyść firmę">
          <mat-icon aria-hidden="true">close</mat-icon>
        </button>
      }
      <mat-autocomplete #auto="matAutocomplete" [displayWith]="displayName" (optionSelected)="select($event.option.value)">
        @for (company of options(); track company.id) {
          <mat-option [value]="company">{{ company.name }}</mat-option>
        } @empty {
          @if (searched()) {
            <mat-option disabled>Brak pasujących firm</mat-option>
          }
        }
      </mat-autocomplete>
      @if (control.hasError('companyRequired')) {
        <mat-error>{{ requiredMessage() }}</mat-error>
      }
    </mat-form-field>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    :host { display: block; }
    .company-field { width: 100%; }
  `]
})
export class CompanyAutocompleteComponent implements OnInit {
  private companyService = inject(CompanyService);
  private adminService = inject(AdminService);
  private cdr = inject(ChangeDetectorRef);

  readonly label = input('Firma');
  readonly placeholder = input('Zacznij wpisywać nazwę...');
  readonly source = input<CompanySource>('approved');
  /** Wymagany wybór – pole jest wtedy błędne, dopóki nie wybrano firmy z listy. */
  readonly required = input(false);
  readonly requiredMessage = input('Wybierz firmę z listy');
  /** Firma wybrana na starcie (np. przy edycji oferty). */
  readonly initial = input<CompanyOption | null>(null);

  readonly companyChange = output<CompanyOption | null>();

  readonly control = new FormControl<CompanyOption | string | null>(null);
  readonly options = signal<CompanyOption[]>([]);
  readonly searched = signal(false);

  private readonly search$ = new Subject<string>();

  constructor() {
    // OnPush: wartość zmienia się też z panelu podpowiedzi i programowo (reset() rodzica)
    this.control.events.pipe(takeUntilDestroyed()).subscribe(() => this.cdr.markForCheck());

    this.search$.pipe(
      debounceTime(300),
      switchMap(term => this.fetch(term)),
      takeUntilDestroyed()
    ).subscribe(options => {
      this.options.set(options);
      this.searched.set(true);
    });

    this.control.valueChanges.pipe(takeUntilDestroyed()).subscribe(value => {
      if (typeof value === 'string') {
        // Zmiana tekstu unieważnia wcześniejszy wybór
        this.companyChange.emit(null);
        this.search$.next(value.trim());
      }
    });
  }

  ngOnInit(): void {
    if (this.required()) {
      this.control.addValidators(companySelectedValidator);
    }
    this.control.setValue(this.initial(), { emitEvent: false });
    this.control.updateValueAndValidity({ emitEvent: false });
  }

  readonly displayName = (value: CompanyOption | string | null): string =>
    isCompanyOption(value) ? value.name : value ?? '';

  /** Pierwsze podpowiedzi (alfabetycznie) po wejściu w puste pole. */
  onFocus(): void {
    if (!this.searched()) {
      this.search$.next(typeof this.control.value === 'string' ? this.control.value.trim() : '');
    }
  }

  select(company: CompanyOption): void {
    this.companyChange.emit(company);
  }

  clear(): void {
    this.control.setValue('');
    this.control.markAsTouched();
  }

  /** Czyści pole bez emitowania zmiany (np. przy „Wyczyść filtry” rodzica). */
  reset(): void {
    this.control.setValue(null, { emitEvent: false });
  }

  private fetch(searchTerm: string): Observable<CompanyOption[]> {
    let request: Observable<{ result: Company[] }>;

    if (this.source() === 'all') {
      const params = new AdminCompanyParams();
      params.pageSize = SUGGESTIONS_LIMIT;
      params.searchTerm = searchTerm;
      params.orderBy = 'nameAsc';
      request = this.adminService.getCompanies(params);
    } else {
      const params = new CompanyParams();
      params.pageSize = SUGGESTIONS_LIMIT;
      params.searchTerm = searchTerm;
      request = this.companyService.getCompanies(params);
    }

    return request.pipe(
      map(page => page.result.map(({ id, name }) => ({ id, name }))),
      catchError(error => {
        console.error('Error loading companies:', error);
        return of([]);
      })
    );
  }
}
