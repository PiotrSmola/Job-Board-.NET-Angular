import { Component, inject, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CommonModule } from '@angular/common';
import { Observable } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { UserDto } from '../../models/user.model';
import { getApiErrorMessage } from '../../utils/http-errors';
import {
  COMPANY_DESCRIPTION_MAX,
  COMPANY_LOCATION_MAX,
  COMPANY_NAME_MAX,
  COMPANY_WEBSITE_MAX,
  websiteValidator
} from '../../utils/company-form';

/** „Szukam pracy” – konto kandydata, „Rekrutuję” – konto pracodawcy z firmą do weryfikacji. */
export type AccountType = 'candidate' | 'employer';

const COMPANY_CONTROLS = ['companyName', 'companyDescription', 'website', 'location'] as const;

function optional(value: string | null | undefined): string | null {
  const trimmed = (value ?? '').trim();
  return trimmed ? trimmed : null;
}

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss'
})
export class RegisterComponent {
  private fb = inject(FormBuilder);
  private authService = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private cdr = inject(ChangeDetectorRef);

  readonly descriptionMax = COMPANY_DESCRIPTION_MAX;

  registerForm: FormGroup;
  accountType: AccountType = 'candidate';
  loading = false;
  errorMessage = '';
  hidePassword = true;

  constructor() {
    this.registerForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(6)]],
      firstName: ['', [Validators.required, Validators.maxLength(100)]],
      lastName: ['', [Validators.required, Validators.maxLength(100)]],
      // Pola firmy – aktywne tylko w trybie „Rekrutuję”
      companyName: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(COMPANY_NAME_MAX)]],
      companyDescription: ['', [Validators.maxLength(COMPANY_DESCRIPTION_MAX)]],
      website: ['', [Validators.maxLength(COMPANY_WEBSITE_MAX), websiteValidator]],
      location: ['', [Validators.maxLength(COMPANY_LOCATION_MAX)]]
    });

    // Link „/register?type=employer” otwiera od razu formularz pracodawcy
    const type = this.route.snapshot.queryParamMap.get('type');
    this.setAccountType(type === 'employer' ? 'employer' : 'candidate');
  }

  get isEmployer(): boolean {
    return this.accountType === 'employer';
  }

  setAccountType(type: AccountType): void {
    this.accountType = type;
    this.errorMessage = '';
    for (const name of COMPANY_CONTROLS) {
      const control = this.registerForm.get(name)!;
      if (type === 'employer') {
        control.enable();
      } else {
        control.disable();
      }
    }
  }

  onSubmit(): void {
    if (this.registerForm.invalid) {
      return;
    }

    this.loading = true;
    this.errorMessage = '';

    const { email, password, firstName, lastName } = this.registerForm.value;
    const employer = this.isEmployer;
    const request: Observable<UserDto> = employer
      ? this.authService.registerEmployer({
        email,
        password,
        firstName,
        lastName,
        companyName: this.registerForm.value.companyName.trim(),
        companyDescription: optional(this.registerForm.value.companyDescription),
        website: optional(this.registerForm.value.website),
        location: optional(this.registerForm.value.location)
      })
      : this.authService.register({ email, password, firstName, lastName });

    request.subscribe({
      next: () => {
        // Pracodawca trafia do panelu – tam widzi status weryfikacji firmy
        this.router.navigate([employer ? '/employer' : '/jobs']);
      },
      error: (error) => {
        this.loading = false;
        this.errorMessage = getApiErrorMessage(error, 'Błąd podczas rejestracji');
        this.cdr.detectChanges();
      }
    });
  }
}
