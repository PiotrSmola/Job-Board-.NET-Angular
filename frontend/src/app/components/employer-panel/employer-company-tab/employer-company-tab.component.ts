import { Component, OnInit, computed, inject, input, output, signal, ChangeDetectionStrategy } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Company, CompanyStatus, SaveCompany } from '../../../models/company.model';
import { CompanyService } from '../../../services/company.service';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { COMPANY_DESCRIPTION_MAX, buildCompanyForm, toSaveCompany } from '../../../utils/company-form';

const REVERIFICATION_WARNING = 'Zmiana nazwy lub strony www wymaga ponownej weryfikacji przez administratora. ' +
  'Do czasu zatwierdzenia oferty firmy nie będą widoczne publicznie.';

function normalizeName(name: string): string {
  return name.trim().replace(/\s+/g, ' ');
}

interface StatusBanner {
  tone: 'pending' | 'approved' | 'rejected';
  icon: string;
  title: string;
  text: string;
}

/** Profil firmy pracodawcy: baner statusu weryfikacji i formularz danych (ponowne zgłoszenie po odrzuceniu). */
@Component({
  selector: 'app-employer-company-tab',
  standalone: true,
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './employer-company-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './employer-company-tab.component.scss'
})
export class EmployerCompanyTabComponent implements OnInit {
  private fb = inject(FormBuilder);
  private companyService = inject(CompanyService);
  private snackBar = inject(MatSnackBar);

  readonly company = input.required<Company>();
  readonly companyChange = output<Company>();

  readonly descriptionMax = COMPANY_DESCRIPTION_MAX;
  readonly saving = signal(false);
  form!: FormGroup;

  readonly isRejected = computed(() => this.company().status === CompanyStatus.Rejected);
  readonly isApproved = computed(() => this.company().status === CompanyStatus.Approved);

  readonly banner = computed<StatusBanner | null>(() => {
    switch (this.company().status) {
      case CompanyStatus.Pending:
        return {
          tone: 'pending',
          icon: 'hourglass_top',
          title: 'Oczekuje na weryfikację',
          text: 'Administrator sprawdzi dane firmy. Do czasu zatwierdzenia możesz przygotowywać oferty jako szkice.'
        };
      case CompanyStatus.Rejected:
        return {
          tone: 'rejected',
          icon: 'block',
          title: 'Zgłoszenie odrzucone',
          text: 'Popraw dane firmy i kliknij „Wyślij ponownie” – zgłoszenie wróci do weryfikacji.'
        };
      case CompanyStatus.Approved:
        return {
          tone: 'approved',
          icon: 'verified',
          title: 'Firma zatwierdzona',
          text: 'Możesz publikować oferty – są widoczne na publicznym profilu firmy.'
        };
      default:
        return null;
    }
  });

  ngOnInit(): void {
    this.form = buildCompanyForm(this.fb, this.company());
  }

  /** Odrzuconą firmę można wysłać ponownie bez zmian; w pozostałych przypadkach zapis wymaga zmian. */
  canSubmit(): boolean {
    return this.form.valid && !this.saving() && (this.isRejected() || this.form.dirty);
  }

  onSubmit(): void {
    if (!this.canSubmit()) return;

    const data = toSaveCompany(this.form.value);
    const reverifying = this.isApproved() && this.changesIdentity(data);
    if (reverifying && !confirm(REVERIFICATION_WARNING)) return;

    const resubmitting = this.isRejected() || reverifying;
    this.saving.set(true);
    this.companyService.updateMyCompany(data).subscribe({
      next: company => {
        this.saving.set(false);
        this.form.reset(toSaveCompany(company));
        this.companyChange.emit(company);
        this.notify(resubmitting ? 'Zgłoszenie firmy zostało wysłane ponownie do weryfikacji' : 'Dane firmy zostały zapisane');
      },
      error: error => {
        console.error('Error saving company:', error);
        this.saving.set(false);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas zapisywania danych firmy'));
        }
      }
    });
  }

  /** Zmiana nazwy (inna niż wielkość liter / odstępy) lub strony www – jak w API, wymaga ponownej weryfikacji. */
  private changesIdentity(data: SaveCompany): boolean {
    const current = this.company();
    return normalizeName(data.name).toLowerCase() !== normalizeName(current.name).toLowerCase()
      || (data.website ?? null) !== (current.website?.trim() || null);
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }
}
