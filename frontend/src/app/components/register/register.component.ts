import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Observable } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { UserDto } from '../../models/user.model';
import { getApiErrorMessage } from '../../utils/http-errors';
import { focusFirstInvalidControl } from '../../utils/form-focus';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH, getPasswordErrorMessages, passwordPolicyValidator } from '../../utils/password';
import {
  COMPANY_DESCRIPTION_MAX,
  COMPANY_LOCATION_MAX,
  COMPANY_NAME_MAX,
  COMPANY_WEBSITE_MAX,
  websiteValidator
} from '../../utils/company-form';
import { PasswordStrengthComponent } from '../shared/password-strength.component';

/** „Szukam pracy” – konto kandydata, „Rekrutuję” – konto pracodawcy z firmą do weryfikacji. */
export type AccountType = 'candidate' | 'employer';

const COMPANY_CONTROLS = ['companyName', 'companyDescription', 'website', 'location'] as const;

function optional(value: string | null | undefined): string | null {
  const trimmed = (value ?? '').trim();
  return trimmed ? trimmed : null;
}

/**
 * Rejestracja (makieta Logowanie.dc.html): przełącznik typu konta, siła hasła tekstem, pola firmy w trybie „Rekrutuję”.
 * Przycisk wysyłania jest zawsze aktywny – błędy pól pokazujemy po próbie (fokus na pierwsze niepoprawne pole).
 */
@Component({
  selector: 'app-register',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    MatProgressSpinnerModule,
    PasswordStrengthComponent
  ],
  templateUrl: './register.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './register.component.scss'
})
export class RegisterComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly descriptionMax = COMPANY_DESCRIPTION_MAX;
  readonly passwordMin = PASSWORD_MIN_LENGTH;
  readonly passwordMax = PASSWORD_MAX_LENGTH;

  readonly registerForm = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    password: ['', [
      Validators.required,
      Validators.minLength(PASSWORD_MIN_LENGTH),
      Validators.maxLength(PASSWORD_MAX_LENGTH),
      passwordPolicyValidator
    ]],
    firstName: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(100)]],
    // Pola firmy – aktywne tylko w trybie „Rekrutuję”
    companyName: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(COMPANY_NAME_MAX)]],
    companyDescription: ['', [Validators.maxLength(COMPANY_DESCRIPTION_MAX)]],
    website: ['', [Validators.maxLength(COMPANY_WEBSITE_MAX), websiteValidator]],
    location: ['', [Validators.maxLength(COMPANY_LOCATION_MAX)]]
  });

  readonly accountType = signal<AccountType>('candidate');
  readonly isEmployer = computed(() => this.accountType() === 'employer');
  readonly loading = signal(false);
  readonly errorMessage = signal('');
  readonly hidePassword = signal(true);
  readonly password = toSignal(this.registerForm.controls.password.valueChanges, { initialValue: '' });

  constructor() {
    // Link „/register?type=employer” otwiera od razu formularz pracodawcy
    const type = this.route.snapshot.queryParamMap.get('type');
    this.setAccountType(type === 'employer' ? 'employer' : 'candidate');
  }

  setAccountType(type: AccountType): void {
    this.accountType.set(type);
    this.errorMessage.set('');
    for (const name of COMPANY_CONTROLS) {
      const control = this.registerForm.controls[name];
      if (type === 'employer') {
        control.enable();
      } else {
        control.disable();
      }
    }
  }

  togglePassword(): void {
    this.hidePassword.update(hidden => !hidden);
  }

  onSubmit(): void {
    if (this.loading()) return;
    this.errorMessage.set('');

    if (this.registerForm.invalid) {
      this.registerForm.markAllAsTouched();
      focusFirstInvalidControl(this.host.nativeElement);
      return;
    }

    this.loading.set(true);

    const value = this.registerForm.getRawValue();
    const base = {
      email: value.email.trim(),
      password: value.password,
      firstName: value.firstName.trim(),
      lastName: value.lastName.trim()
    };
    const employer = this.isEmployer();
    const request: Observable<UserDto> = employer
      ? this.authService.registerEmployer({
        ...base,
        companyName: value.companyName.trim(),
        companyDescription: optional(value.companyDescription),
        website: optional(value.website),
        location: optional(value.location)
      })
      : this.authService.register(base);

    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        // Pracodawca trafia do panelu – tam widzi status weryfikacji firmy
        this.router.navigate([employer ? '/employer' : '/jobs']);
      },
      error: (error) => {
        this.loading.set(false);
        const passwordErrors = getPasswordErrorMessages(error);
        this.errorMessage.set(passwordErrors.length
          ? passwordErrors.join('. ')
          : getApiErrorMessage(error, 'Nie udało się założyć konta – spróbuj ponownie za chwilę.'));
      }
    });
  }
}
