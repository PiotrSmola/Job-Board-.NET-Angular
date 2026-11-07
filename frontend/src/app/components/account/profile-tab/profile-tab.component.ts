import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { map } from 'rxjs';
import { AccountService } from '../../../services/account.service';
import { Profile, UpdateProfile } from '../../../models/account.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { focusFirstInvalidControl } from '../../../utils/form-focus';

// Limity i formaty zgodne z walidacją API (PUT /api/account/profile)
export const PROFILE_LIMITS = {
  firstName: 100,
  lastName: 100,
  phoneNumber: 30,
  headline: 150,
  location: 200,
  linkedInUrl: 300,
  about: 2000
} as const;
/** Cyfry, spacje, „+” i „-”. */
export const PHONE_PATTERN = /^[0-9+\- ]+$/;
/** Tylko profil LinkedIn po https (z „www.” lub bez) – zgodnie z API. */
export const LINKEDIN_PATTERN = /^https:\/\/(www\.)?linkedin\.com\/\S+$/i;

function optional(value: string): string | null {
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

@Component({
  selector: 'app-profile-tab',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './profile-tab.component.html',
  styleUrl: './profile-tab.component.scss'
})
export class ProfileTabComponent {
  private readonly accountService = inject(AccountService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly snackBar = inject(MatSnackBar);

  readonly limits = PROFILE_LIMITS;
  readonly state = signal<'loading' | 'loaded' | 'error'>('loading');
  readonly loadError = signal('');
  readonly profile = signal<Profile | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal('');

  readonly form = inject(FormBuilder).nonNullable.group({
    firstName: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(PROFILE_LIMITS.firstName)]],
    lastName: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(PROFILE_LIMITS.lastName)]],
    phoneNumber: ['', [Validators.maxLength(PROFILE_LIMITS.phoneNumber), Validators.pattern(PHONE_PATTERN)]],
    headline: ['', [Validators.maxLength(PROFILE_LIMITS.headline)]],
    location: ['', [Validators.maxLength(PROFILE_LIMITS.location)]],
    linkedInUrl: ['', [Validators.maxLength(PROFILE_LIMITS.linkedInUrl), Validators.pattern(LINKEDIN_PATTERN)]],
    about: ['', [Validators.maxLength(PROFILE_LIMITS.about)]]
  });

  /** Licznik znaków „O mnie” (podpowiedź pod polem). */
  readonly aboutLength = toSignal(this.form.controls.about.valueChanges.pipe(map(value => value.length)), { initialValue: 0 });

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.accountService.getProfile().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: profile => {
        this.applyProfile(profile);
        this.state.set('loaded');
      },
      error: error => {
        console.error('Error loading profile:', error);
        this.loadError.set(getApiErrorMessage(error, 'Nie udało się wczytać profilu'));
        this.state.set('error');
      }
    });
  }

  save(): void {
    if (this.saving()) return;
    this.saveError.set('');

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.saveError.set('Popraw zaznaczone pola formularza');
      focusFirstInvalidControl(this.host.nativeElement);
      return;
    }

    const value = this.form.getRawValue();
    const body: UpdateProfile = {
      firstName: value.firstName.trim(),
      lastName: value.lastName.trim(),
      phoneNumber: optional(value.phoneNumber),
      headline: optional(value.headline),
      location: optional(value.location),
      linkedInUrl: optional(value.linkedInUrl),
      about: optional(value.about)
    };

    this.saving.set(true);
    this.accountService.updateProfile(body).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: profile => {
        this.saving.set(false);
        this.applyProfile(profile);
        this.snackBar.open('Zapisano zmiany w profilu', 'OK', { duration: 5000, politeness: 'polite' });
      },
      error: error => {
        console.error('Error saving profile:', error);
        this.saving.set(false);
        this.saveError.set(isHandledByInterceptor(error)
          ? 'Nie udało się zapisać profilu'
          : getApiErrorMessage(error, 'Nie udało się zapisać profilu'));
      }
    });
  }

  private applyProfile(profile: Profile): void {
    this.profile.set(profile);
    this.form.reset({
      firstName: profile.firstName ?? '',
      lastName: profile.lastName ?? '',
      phoneNumber: profile.phoneNumber ?? '',
      headline: profile.headline ?? '',
      location: profile.location ?? '',
      linkedInUrl: profile.linkedInUrl ?? '',
      about: profile.about ?? ''
    });
  }
}
