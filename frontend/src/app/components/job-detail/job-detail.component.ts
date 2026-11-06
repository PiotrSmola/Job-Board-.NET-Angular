import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, ChangeDetectorRef, computed, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { MAT_DIALOG_DATA, MatDialog, MatDialogConfig, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { HttpErrorResponse } from '@angular/common/http';
import { JobService } from '../../services/job.service';
import { JobApplicationService } from '../../services/job-application.service';
import { AuthService } from '../../services/auth.service';
import { JobOffer, JobOfferStatus, WorkType } from '../../models/job-offer.model';
import { ApplicationAttachment, ApplicationStatus, AttachmentUploadItem, JobApplication } from '../../models/job-application.model';
import { getApplicationStatusPresentation, getJobState, StatusPresentation } from '../../utils/status-labels';
import { StatusChipComponent } from '../shared/status-chip.component';
import { JobTaxonomyComponent } from '../shared/job-taxonomy.component';
import { formatJobSalary, getContractTypeLabels, getSeniorityLabel, getWorkTypeLabel } from '../../utils/job-labels';
import { AttachmentsPickerComponent } from '../shared/attachments-picker.component';
import { ProfileDocumentsPickerComponent } from '../shared/profile-documents-picker.component';
import { SaveJobButtonComponent } from '../shared/save-job-button.component';
import { ProfileDocument } from '../../models/account.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../utils/http-errors';

const ALREADY_APPLIED_MESSAGE = 'You have already applied to this job';
const NOT_ACCEPTING_MESSAGE = 'This job offer is not accepting applications';

export const ATTACHMENTS_UPLOAD_FAILED_MESSAGE =
  'Aplikacja złożona, ale nie udało się dołączyć załączników – możesz dodać je w Moich aplikacjach';

/** Konfiguracja dialogu aplikowania (modalny dla czytników ekranu, miejsce na listę plików). */
export const APPLY_DIALOG_CONFIG: MatDialogConfig = { width: '600px', maxWidth: '95vw', ariaModal: true };

/** Dane dialogu aplikowania: załączniki, które aplikacja już ma (ponowne aplikowanie po wycofaniu je zachowuje). */
export interface ApplyDialogData {
  existing: readonly ApplicationAttachment[];
}

/** Wynik dialogu aplikowania (undefined = anulowano). */
export interface ApplyDialogResult {
  confirmed: boolean;
  note: string;
  attachments: AttachmentUploadItem[];
  /** Dokumenty z profilu do skopiowania do aplikacji. */
  profileDocumentIds: number[];
}

@Component({
  selector: 'app-apply-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    AttachmentsPickerComponent,
    ProfileDocumentsPickerComponent
  ],
  template: `
    <h2 mat-dialog-title>Aplikuj na stanowisko</h2>
    <mat-dialog-content>
      <p>Dodaj notatkę do swojej aplikacji (opcjonalnie):</p>
      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Notatka</mat-label>
        <textarea matInput [(ngModel)]="note" rows="4" placeholder="Dlaczego jesteś idealnym kandydatem..."></textarea>
      </mat-form-field>
      <app-profile-documents-picker [pendingItems]="attachments" [existing]="existing" (selectionChange)="profileDocuments.set($event)" />
      <p>Dołącz CV, list motywacyjny lub inne pliki (opcjonalnie):</p>
      <!-- zachowane załączniki i wybrane dokumenty z profilu wliczają się do limitów nowych plików -->
      <app-attachments-picker [(items)]="attachments" [existing]="countedAttachments()" />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Anuluj</button>
      <button mat-raised-button color="primary" [mat-dialog-close]="result()">Aplikuj</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .full-width { width: 100%; }
    mat-dialog-content { min-width: 460px; padding: 20px; }
    @media (max-width: 600px) { mat-dialog-content { min-width: 0; } }
  `]
})
export class ApplyDialogComponent {
  /** Limity (10 plików, 25 MB) i domyślny rodzaj nowych plików liczone łącznie z tymi załącznikami. */
  readonly existing = inject<ApplyDialogData | null>(MAT_DIALOG_DATA, { optional: true })?.existing ?? [];
  note = '';
  attachments: AttachmentUploadItem[] = [];
  readonly profileDocuments = signal<ProfileDocument[]>([]);
  readonly countedAttachments = computed<readonly ApplicationAttachment[]>(() => [...this.existing, ...this.profileDocuments()]);

  result(): ApplyDialogResult {
    return {
      confirmed: true,
      note: this.note,
      attachments: this.attachments,
      profileDocumentIds: this.profileDocuments().map(d => d.id)
    };
  }
}

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatDividerModule,
    MatDialogModule,
    RouterModule,
    StatusChipComponent,
    JobTaxonomyComponent,
    SaveJobButtonComponent
  ],
  templateUrl: './job-detail.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './job-detail.component.scss'
})
export class JobDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private jobService = inject(JobService);
  private applicationService = inject(JobApplicationService);
  authService = inject(AuthService);
  private dialog = inject(MatDialog);
  private cdr = inject(ChangeDetectorRef);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  job: JobOffer | null = null;
  loading = false;
  hasApplied = false;
  /** Status aplikacji bieżącego użytkownika (także wycofanej); null = nie aplikował. */
  applicationStatus: ApplicationStatus | null = null;
  /** Załączniki wycofanej aplikacji – zostają po ponownym aplikowaniu, więc dialog liczy limity razem z nimi. */
  private withdrawnAttachments: readonly ApplicationAttachment[] = [];
  checkingApplication = false;
  applying = false;
  /** Postęp wysyłania załączników po złożeniu aplikacji (null = nic nie jest wysyłane). */
  attachmentsUploadProgress: number | null = null;
  /** Tekst regionu `role="status"`: etap wysyłania załączników (bez kolejnych procentów). */
  uploadAnnouncement = '';
  private destroyed = false;

  WorkType = WorkType;

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      // opuszczenie strony w trakcie wysyłania przerywa żądanie (takeUntilDestroyed) – aplikacja jest już złożona
      if (this.attachmentsUploadProgress !== null) {
        this.attachmentsUploadProgress = null;
        this.notify(ATTACHMENTS_UPLOAD_FAILED_MESSAGE, 8000);
      }
    });
  }

  /** Oferta przyjmuje aplikacje: aktywna i niewygasła. */
  get acceptsApplications(): boolean {
    return !!this.job && this.job.status === JobOfferStatus.Active && !this.job.isExpired;
  }

  /** Zalogowany kandydat – admin i pracodawca nie aplikują na oferty (API zwraca 403). */
  get canApplyAsCandidate(): boolean {
    return this.authService.isAuthenticated() && !this.authService.isAdmin() && !this.authService.isEmployer();
  }

  /** Użytkownik wycofał wcześniej aplikację – może aplikować ponownie. */
  get hasWithdrawn(): boolean {
    return !this.hasApplied && this.applicationStatus === ApplicationStatus.Withdrawn;
  }

  /** Baner dla oferty, która nie przyjmuje aplikacji (null = brak banera). */
  get banner(): { icon: string; text: string } | null {
    if (!this.job) return null;

    switch (getJobState(this.job)) {
      case 'closed': return { icon: 'lock', text: 'Rekrutacja na to stanowisko została zakończona – oferta nie przyjmuje już aplikacji.' };
      case 'expired': return { icon: 'event_busy', text: 'Ta oferta wygasła i nie przyjmuje już aplikacji.' };
      case 'draft': return { icon: 'edit_note', text: 'Szkic – oferta nie jest widoczna publicznie.' };
      default: return null;
    }
  }

  get applicationStatusPresentation(): StatusPresentation | null {
    return this.applicationStatus === null ? null : getApplicationStatusPresentation(this.applicationStatus);
  }

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      const id = params.get('id');
      if (id) {
        this.loadJob(+id);
        // Pracodawca nie aplikuje – nie sprawdzamy statusu aplikacji
        if (this.authService.isAuthenticated() && !this.authService.isEmployer()) {
          this.checkIfApplied(+id);
        }
      }
    });
  }

  loadJob(id: number): void {
    this.loading = true;
    this.jobService.getJob(id).subscribe({
      next: (job) => {
        this.job = job;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Error loading job:', err);
        this.loading = false;
        this.cdr.detectChanges();
        if (!isHandledByInterceptor(err)) {
          this.notify(getApiErrorMessage(err, 'Nie udało się wczytać oferty'));
        }
      }
    });
  }

  checkIfApplied(jobId: number): void {
    this.checkingApplication = true;
    this.applicationService.checkIfApplied(jobId).subscribe({
      next: (result) => {
        this.hasApplied = result.hasApplied;
        this.applicationStatus = result.status ?? null;
        this.withdrawnAttachments = [];
        if (this.hasWithdrawn && result.applicationId) {
          // przycisk „Aplikuj ponownie” czeka (checkingApplication) na załączniki wycofanej aplikacji
          this.loadWithdrawnAttachments(result.applicationId);
          return;
        }
        this.checkingApplication = false;
        this.cdr.detectChanges();
      },
      error: (error) => {
        console.error('Error checking application status:', error);
        this.checkingApplication = false;
        this.hasApplied = false;
        this.cdr.detectChanges();
      }
    });
  }

  private loadWithdrawnAttachments(applicationId: number): void {
    this.applicationService.getApplication(applicationId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (application) => {
        this.withdrawnAttachments = application.attachments ?? [];
        this.checkingApplication = false;
        this.cdr.detectChanges();
      },
      error: (error) => {
        // bez listy dialog liczy tylko nowe pliki – limity łączne i tak sprawdza serwer
        console.error('Error loading the withdrawn application:', error);
        this.checkingApplication = false;
        this.cdr.detectChanges();
      }
    });
  }

  openApplyDialog(): void {
    if (!this.job) return;

    const data: ApplyDialogData = { existing: this.hasWithdrawn ? this.withdrawnAttachments : [] };
    const dialogRef = this.dialog.open(ApplyDialogComponent, { ...APPLY_DIALOG_CONFIG, data });

    dialogRef.afterClosed().subscribe((result: ApplyDialogResult | undefined) => {
      if (result?.confirmed && this.job) {
        this.applyToJob(result.note, result.attachments, result.profileDocumentIds ?? []);
      }
    });
  }

  /**
   * Najpierw składa aplikację, potem (opcjonalnie) dołącza kopie dokumentów z profilu, a na końcu jednym żądaniem
   * przesyła nowe pliki – błąd dołączania nie cofa aplikacji.
   */
  applyToJob(note: string, attachments: readonly AttachmentUploadItem[] = [], profileDocumentIds: readonly number[] = []): void {
    if (!this.job) return;

    this.applying = true;
    this.applicationService.applyToJob({
      jobOfferId: this.job.id,
      note: note || null
    }).subscribe({
      next: (application: JobApplication | null) => {
        const reapplied = this.hasWithdrawn;
        this.hasApplied = true;
        this.applicationStatus = application?.status ?? ApplicationStatus.Submitted;
        const successMessage = reapplied ? 'Aplikacja została złożona ponownie!' : 'Aplikacja wysłana pomyślnie!';

        if (profileDocumentIds.length) {
          this.attachProfileDocuments(application?.id ?? null, profileDocumentIds, attachments, successMessage);
          return;
        }

        if (attachments.length) {
          this.uploadAttachments(application?.id ?? null, attachments, successMessage);
          return;
        }

        this.applying = false;
        this.cdr.detectChanges();
        this.notify(successMessage);
      },
      error: (error) => {
        this.applying = false;
        const message = error instanceof HttpErrorResponse && error.status === 400 ? error.error?.message : null;
        // Backend odrzucił duplikat – przycisk powinien pokazać, że aplikacja już istnieje
        if (message === ALREADY_APPLIED_MESSAGE) {
          this.hasApplied = true;
          if (this.applicationStatus === null || this.applicationStatus === ApplicationStatus.Withdrawn) {
            this.applicationStatus = ApplicationStatus.Submitted;
          }
        }
        // Oferta została w międzyczasie zamknięta lub wygasła – odświeżamy ją, żeby pokazać baner
        if (message === NOT_ACCEPTING_MESSAGE && this.job) {
          this.loadJob(this.job.id);
        }
        this.cdr.detectChanges();
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas aplikowania'));
        }
      }
    });
  }

  /** Kopiuje dokumenty z profilu do aplikacji; potem (jeśli są) wysyła nowe pliki. */
  private attachProfileDocuments(
    applicationId: number | null,
    documentIds: readonly number[],
    items: readonly AttachmentUploadItem[],
    successMessage: string
  ): void {
    if (applicationId === null || this.destroyed) {
      this.finishAttachmentsUpload(ATTACHMENTS_UPLOAD_FAILED_MESSAGE, 'Przerwano wysyłanie', 8000);
      return;
    }

    // serwer kopiuje pliki – bez postępu wysyłania, przycisk pokazuje „Przetwarzanie załączników…”
    this.attachmentsUploadProgress = 100;
    this.uploadAnnouncement = 'Dołączanie dokumentów z profilu…';
    this.cdr.detectChanges();
    this.applicationService.attachFromProfile(applicationId, documentIds).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        if (items.length) {
          this.uploadAttachments(applicationId, items, successMessage);
        } else {
          this.finishAttachmentsUpload(`${successMessage} Załączniki zostały dołączone.`, 'Załączniki przesłane');
        }
      },
      error: (error) => {
        console.error('Error attaching profile documents:', error);
        this.finishAttachmentsUpload(ATTACHMENTS_UPLOAD_FAILED_MESSAGE, 'Przerwano wysyłanie', 8000);
      }
    });
  }

  private uploadAttachments(applicationId: number | null, items: readonly AttachmentUploadItem[], successMessage: string): void {
    // brak id albo użytkownik opuścił już stronę (aplikacja złożona, pliki nie zostały wysłane)
    if (applicationId === null || this.destroyed) {
      this.finishAttachmentsUpload(ATTACHMENTS_UPLOAD_FAILED_MESSAGE, 'Przerwano wysyłanie', 8000);
      return;
    }

    this.attachmentsUploadProgress = 0;
    this.uploadAnnouncement = 'Wysyłanie załączników…';
    this.cdr.detectChanges();
    // opuszczenie strony przerywa wysyłanie (komunikat pokazuje hook onDestroy z konstruktora)
    this.applicationService.uploadAttachments(applicationId, items).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (event) => {
        if (event.type === 'progress') {
          this.attachmentsUploadProgress = event.percent;
          this.cdr.detectChanges();
        } else {
          this.finishAttachmentsUpload(`${successMessage} Załączniki zostały dołączone.`, 'Załączniki przesłane');
        }
      },
      error: (error) => {
        console.error('Error uploading attachments:', error);
        // Aplikacja jest już złożona – informujemy o tym zawsze, także przy 401/403
        this.finishAttachmentsUpload(ATTACHMENTS_UPLOAD_FAILED_MESSAGE, 'Przerwano wysyłanie', 8000);
      }
    });
  }

  private finishAttachmentsUpload(message: string, announcement: string, duration?: number): void {
    this.applying = false;
    this.attachmentsUploadProgress = null;
    if (!this.destroyed) {
      this.uploadAnnouncement = announcement;
      this.cdr.detectChanges();
    }
    this.notify(message, duration);
  }

  private notify(message: string, duration = 5000): void {
    this.snackBar.open(message, 'OK', { duration });
  }

  getWorkTypeLabel(): string {
    return this.job ? getWorkTypeLabel(this.job.workType) : '';
  }

  formatSalary(): string {
    return this.job ? formatJobSalary(this.job) : '';
  }

  get seniorityLabel(): string | null {
    return this.job ? getSeniorityLabel(this.job.seniority) : null;
  }

  get contractLabels(): string[] {
    return this.job ? getContractTypeLabels(this.job.contractTypes) : [];
  }

  goBack(): void {
    this.router.navigate(['/jobs']);
  }
}
