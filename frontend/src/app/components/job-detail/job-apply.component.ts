import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, distinctUntilChanged, filter, map, of, switchMap, tap } from 'rxjs';
import { JobApplicationService } from '../../services/job-application.service';
import { AuthService } from '../../services/auth.service';
import { JobOffer, JobOfferStatus } from '../../models/job-offer.model';
import { ApplicationAttachment, ApplicationStatus, AttachmentUploadItem, JobApplication } from '../../models/job-application.model';
import { ProfileDocument } from '../../models/account.model';
import { getApplicationStatusPresentation } from '../../utils/status-labels';
import { getApiErrorMessage, isHandledByInterceptor } from '../../utils/http-errors';
import { StatusChipComponent } from '../shared/status-chip.component';
import { AttachmentsPickerComponent } from '../shared/attachments-picker.component';
import { ProfileDocumentsPickerComponent } from '../shared/profile-documents-picker.component';

const ALREADY_APPLIED_MESSAGE = 'You have already applied to this job';
const NOT_ACCEPTING_MESSAGE = 'This job offer is not accepting applications';

/** Limit notatki jak w API (CreateJobApplicationDto.Note). */
export const NOTE_MAX_LENGTH = 1000;

export const ATTACHMENTS_UPLOAD_FAILED_MESSAGE =
  'Aplikacja złożona, ale nie udało się dołączyć załączników – możesz dodać je w Moich aplikacjach';

/** Co pokazuje sekcja: formularz, status złożonej aplikacji, zachętę do logowania itd. (hidden = nic). */
export type ApplyMode = 'hidden' | 'guest' | 'staff' | 'checking' | 'applied' | 'form';

/**
 * Sekcja „Twoja aplikacja” na stronie oferty (zamiast modalnego dialogu): dokumenty z profilu, nowe pliki, wiadomość
 * i wysyłka. Najpierw składa aplikację, potem dołącza kopie dokumentów z profilu, a na końcu jednym żądaniem wysyła
 * nowe pliki – błąd dołączania nie cofa aplikacji. Wyjście ze strony w trakcie wysyłania przerywa żądanie
 * i wyjaśnia, jak dodać pliki później. Ponowne aplikowanie po wycofaniu liczy limity razem z zachowanymi plikami.
 */
@Component({
  selector: 'app-job-apply',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    StatusChipComponent,
    AttachmentsPickerComponent,
    ProfileDocumentsPickerComponent
  ],
  templateUrl: './job-apply.component.html',
  styleUrl: './job-apply.component.scss'
})
export class JobApplyComponent {
  private applicationService = inject(JobApplicationService);
  private authService = inject(AuthService);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  readonly job = input.required<JobOffer>();
  /** Oferta przestała przyjmować aplikacje (komunikat API) – rodzic wczytuje ją ponownie, żeby pokazać baner. */
  readonly jobStale = output<void>();
  /** Użytkownik ma aktywną aplikację na tę ofertę (przycisk w nagłówku zmienia etykietę). */
  readonly appliedChange = output<boolean>();

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  readonly noteMaxLength = NOTE_MAX_LENGTH;

  readonly hasApplied = signal(false);
  /** Status aplikacji bieżącego użytkownika (także wycofanej); null = nie aplikował. */
  readonly applicationStatus = signal<ApplicationStatus | null>(null);
  /** Załączniki wycofanej aplikacji – zostają po ponownym aplikowaniu, więc limity liczą się razem z nimi. */
  readonly withdrawnAttachments = signal<readonly ApplicationAttachment[]>([]);
  readonly checking = signal(false);
  readonly submitting = signal(false);
  /** Postęp dołączania załączników po złożeniu aplikacji (null = nic nie jest wysyłane). */
  readonly uploadProgress = signal<number | null>(null);
  /** Tekst regionu `role="status"`: etap wysyłania (bez kolejnych procentów). */
  readonly announcement = signal('');
  /** Błąd złożenia aplikacji pokazywany przy przycisku (formularz zostaje). */
  readonly error = signal<string | null>(null);

  readonly note = signal('');
  readonly attachments = signal<AttachmentUploadItem[]>([]);
  readonly profileDocuments = signal<ProfileDocument[]>([]);

  readonly acceptsApplications = computed(() => {
    const job = this.job();
    return job.status === JobOfferStatus.Active && !job.isExpired;
  });
  readonly isCandidate = computed(() =>
    this.authService.isAuthenticated() && !this.authService.isAdmin() && !this.authService.isEmployer());
  readonly hasWithdrawn = computed(() => !this.hasApplied() && this.applicationStatus() === ApplicationStatus.Withdrawn);
  readonly statusPresentation = computed(() => {
    const status = this.applicationStatus();
    return status === null ? null : getApplicationStatusPresentation(status);
  });
  /** Zachowane pliki wycofanej aplikacji (tylko przy ponownym aplikowaniu). */
  readonly existingAttachments = computed(() => this.hasWithdrawn() ? this.withdrawnAttachments() : []);
  /** Do limitów nowych plików wliczają się zachowane załączniki i wybrane dokumenty z profilu. */
  readonly countedAttachments = computed<readonly ApplicationAttachment[]>(() =>
    [...this.existingAttachments(), ...this.profileDocuments()]);
  readonly returnUrl = computed(() => `/jobs/${this.job().id}`);

  readonly mode = computed<ApplyMode>(() => {
    const accepting = this.acceptsApplications();
    if (!this.authService.isAuthenticated()) return accepting ? 'guest' : 'hidden';
    if (!this.isCandidate()) return accepting ? 'staff' : 'hidden';
    if (this.checking()) return 'checking';
    if (this.submitting()) return 'form';
    if (this.hasApplied()) return 'applied';
    if (!accepting) return this.applicationStatus() !== null ? 'applied' : 'hidden';
    return 'form';
  });

  readonly submitLabel = computed(() => {
    const progress = this.uploadProgress();
    if (progress !== null) return progress >= 100 ? 'Przetwarzanie załączników…' : `Wysyłanie załączników… ${progress}%`;
    if (this.submitting()) return 'Wysyłanie aplikacji…';
    return this.hasWithdrawn() ? 'Aplikuj ponownie' : 'Wyślij aplikację';
  });

  private destroyed = false;

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      // opuszczenie strony w trakcie wysyłania przerywa żądanie (takeUntilDestroyed) – aplikacja jest już złożona
      if (this.uploadProgress() !== null) {
        this.uploadProgress.set(null);
        this.notify(ATTACHMENTS_UPLOAD_FAILED_MESSAGE, 8000);
      }
    });

    // Status aplikacji sprawdzamy tylko dla kandydata i tylko przy zmianie oferty (nie po odświeżeniu tej samej)
    toObservable(computed(() => this.job().id)).pipe(
      distinctUntilChanged(),
      tap(() => this.resetForm()),
      filter(() => this.isCandidate()),
      switchMap(jobId => {
        this.checking.set(true);
        return this.applicationService.checkIfApplied(jobId).pipe(
          switchMap(result => {
            this.hasApplied.set(result.hasApplied);
            this.applicationStatus.set(result.status ?? null);
            this.appliedChange.emit(result.hasApplied);
            // „Aplikuj ponownie” czeka (checking) na załączniki wycofanej aplikacji
            return this.hasWithdrawn() && result.applicationId
              ? this.applicationService.getApplication(result.applicationId).pipe(
                map(application => application.attachments ?? []),
                catchError(error => {
                  // bez listy liczymy tylko nowe pliki – limity łączne i tak sprawdza serwer
                  console.error('Error loading the withdrawn application:', error);
                  return of([] as ApplicationAttachment[]);
                }))
              : of([] as ApplicationAttachment[]);
          }),
          catchError(error => {
            console.error('Error checking application status:', error);
            this.hasApplied.set(false);
            return of([] as ApplicationAttachment[]);
          })
        );
      }),
      takeUntilDestroyed()
    ).subscribe(kept => {
      this.withdrawnAttachments.set(kept);
      this.checking.set(false);
    });
  }

  /** Przewija do sekcji i ustawia na niej fokus (przycisk „Aplikuj” w nagłówku oferty). */
  focusSection(): void {
    const heading = this.heading()?.nativeElement;
    if (!heading) return;
    heading.focus({ preventScroll: true });
    const reduce = typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    heading.scrollIntoView?.({ behavior: reduce ? 'auto' : 'smooth', block: 'start' });
  }

  onNoteInput(event: Event): void {
    this.note.set((event.target as HTMLTextAreaElement).value);
  }

  submit(event?: Event): void {
    event?.preventDefault();
    if (this.submitting()) return;
    this.applyToJob(this.note(), this.attachments(), this.profileDocuments().map(d => d.id));
  }

  /** Składa aplikację, potem (opcjonalnie) dołącza dokumenty z profilu i nowe pliki. */
  applyToJob(note: string, attachments: readonly AttachmentUploadItem[] = [], profileDocumentIds: readonly number[] = []): void {
    const job = this.job();
    this.submitting.set(true);
    this.error.set(null);
    this.announcement.set('');

    // żądanie złożenia NIE jest przerywane przy wyjściu ze strony (stan aplikacji byłby niepewny) – kończy się, a pliki
    // nie są już wysyłane i użytkownik dostaje komunikat, jak dodać je później
    this.applicationService.applyToJob({ jobOfferId: job.id, note: note.trim() || null }).subscribe({
      next: (application: JobApplication | null) => {
        const reapplied = this.hasWithdrawn();
        this.hasApplied.set(true);
        this.applicationStatus.set(application?.status ?? ApplicationStatus.Submitted);
        if (!this.destroyed) this.appliedChange.emit(true);
        const successMessage = reapplied ? 'Aplikacja została złożona ponownie!' : 'Aplikacja wysłana pomyślnie!';

        if (profileDocumentIds.length) {
          this.attachProfileDocuments(application?.id ?? null, profileDocumentIds, attachments, successMessage);
        } else if (attachments.length) {
          this.uploadAttachments(application?.id ?? null, attachments, successMessage);
        } else {
          this.finish(successMessage, 'Aplikacja wysłana');
        }
      },
      error: error => this.onApplyError(error)
    });
  }

  private onApplyError(error: unknown): void {
    this.submitting.set(false);
    const message = error instanceof HttpErrorResponse && error.status === 400 ? error.error?.message : null;
    if (isHandledByInterceptor(error)) return;
    const text = getApiErrorMessage(error, 'Błąd podczas aplikowania');

    // Backend odrzucił duplikat – sekcja pokazuje, że aplikacja już istnieje
    if (message === ALREADY_APPLIED_MESSAGE) {
      this.hasApplied.set(true);
      const status = this.applicationStatus();
      if (status === null || status === ApplicationStatus.Withdrawn) this.applicationStatus.set(ApplicationStatus.Submitted);
      if (!this.destroyed) this.appliedChange.emit(true);
      this.notify(text);
      return;
    }
    // Oferta została w międzyczasie zamknięta lub wygasła – rodzic odświeża ją i pokazuje baner
    if (message === NOT_ACCEPTING_MESSAGE) {
      if (!this.destroyed) this.jobStale.emit();
      this.notify(text);
      return;
    }
    this.error.set(text);
  }

  /** Kopiuje dokumenty z profilu do aplikacji; potem (jeśli są) wysyła nowe pliki. */
  private attachProfileDocuments(
    applicationId: number | null,
    documentIds: readonly number[],
    items: readonly AttachmentUploadItem[],
    successMessage: string
  ): void {
    if (applicationId === null || this.destroyed) {
      this.finish(ATTACHMENTS_UPLOAD_FAILED_MESSAGE, 'Przerwano wysyłanie', 8000);
      return;
    }

    // serwer kopiuje pliki – bez postępu wysyłania, przycisk pokazuje „Przetwarzanie załączników…”
    this.uploadProgress.set(100);
    this.announcement.set('Dołączanie dokumentów z profilu…');
    this.applicationService.attachFromProfile(applicationId, documentIds).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        if (items.length) {
          this.uploadAttachments(applicationId, items, successMessage);
        } else {
          this.finish(`${successMessage} Załączniki zostały dołączone.`, 'Załączniki przesłane');
        }
      },
      error: error => {
        console.error('Error attaching profile documents:', error);
        this.finish(ATTACHMENTS_UPLOAD_FAILED_MESSAGE, 'Przerwano wysyłanie', 8000);
      }
    });
  }

  private uploadAttachments(applicationId: number | null, items: readonly AttachmentUploadItem[], successMessage: string): void {
    // brak id albo użytkownik opuścił już stronę (aplikacja złożona, pliki nie zostały wysłane)
    if (applicationId === null || this.destroyed) {
      this.finish(ATTACHMENTS_UPLOAD_FAILED_MESSAGE, 'Przerwano wysyłanie', 8000);
      return;
    }

    this.uploadProgress.set(0);
    this.announcement.set('Wysyłanie załączników…');
    // opuszczenie strony przerywa wysyłanie (komunikat pokazuje hook onDestroy z konstruktora)
    this.applicationService.uploadAttachments(applicationId, items).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: event => {
        if (event.type === 'progress') {
          this.uploadProgress.set(event.percent);
        } else {
          this.finish(`${successMessage} Załączniki zostały dołączone.`, 'Załączniki przesłane');
        }
      },
      error: error => {
        console.error('Error uploading attachments:', error);
        // Aplikacja jest już złożona – informujemy o tym zawsze, także przy 401/403
        this.finish(ATTACHMENTS_UPLOAD_FAILED_MESSAGE, 'Przerwano wysyłanie', 8000);
      }
    });
  }

  /** Koniec wysyłki: sekcja pokazuje status, fokus przechodzi na jej nagłówek (przycisk znika). */
  private finish(message: string, announcement: string, duration?: number): void {
    this.submitting.set(false);
    this.uploadProgress.set(null);
    if (!this.destroyed) {
      this.announcement.set(announcement);
      this.resetSelection();
      queueMicrotask(() => this.heading()?.nativeElement.focus({ preventScroll: true }));
    }
    this.notify(message, duration);
  }

  private resetSelection(): void {
    this.note.set('');
    this.attachments.set([]);
    this.profileDocuments.set([]);
  }

  private resetForm(): void {
    this.resetSelection();
    this.hasApplied.set(false);
    this.applicationStatus.set(null);
    this.withdrawnAttachments.set([]);
    this.error.set(null);
    this.announcement.set('');
  }

  private notify(message: string, duration = 5000): void {
    // sukces i informacja nie przerywają czytnika ekranu (aria-live polite)
    this.snackBar.open(message, 'OK', { duration, politeness: 'polite' });
  }
}

