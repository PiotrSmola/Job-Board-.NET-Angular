import { Component, DestroyRef, inject, OnInit, ChangeDetectorRef } from '@angular/core';
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
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { HttpErrorResponse } from '@angular/common/http';
import { JobService } from '../../services/job.service';
import { JobApplicationService } from '../../services/job-application.service';
import { AuthService } from '../../services/auth.service';
import { JobOffer, JobOfferStatus, WorkType } from '../../models/job-offer.model';
import { ApplicationStatus, JobApplication } from '../../models/job-application.model';
import { getApplicationStatusPresentation, getJobState, StatusPresentation } from '../../utils/status-labels';
import { StatusChipComponent } from '../shared/status-chip.component';
import { CvUploadComponent } from '../shared/cv-upload.component';
import { getApiErrorMessage, isHandledByInterceptor } from '../../utils/http-errors';

const ALREADY_APPLIED_MESSAGE = 'You have already applied to this job';
const NOT_ACCEPTING_MESSAGE = 'This job offer is not accepting applications';

export const CV_UPLOAD_FAILED_MESSAGE =
  'Aplikacja złożona, ale nie udało się dołączyć CV – możesz dodać je w Moich aplikacjach';

/** Wynik dialogu aplikowania (undefined = anulowano). */
export interface ApplyDialogResult {
  confirmed: boolean;
  note: string;
  cvFile: File | null;
}

@Component({
  selector: 'app-apply-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    CvUploadComponent
  ],
  template: `
    <h2 mat-dialog-title>Aplikuj na stanowisko</h2>
    <mat-dialog-content>
      <p>Dodaj notatkę do swojej aplikacji (opcjonalnie):</p>
      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Notatka</mat-label>
        <textarea matInput [(ngModel)]="note" rows="4" placeholder="Dlaczego jesteś idealnym kandydatem..."></textarea>
      </mat-form-field>
      <p>Dołącz CV (opcjonalnie):</p>
      <app-cv-upload [(file)]="cvFile" />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Anuluj</button>
      <button mat-raised-button color="primary" [mat-dialog-close]="result()">Aplikuj</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .full-width { width: 100%; }
    mat-dialog-content { min-width: 400px; padding: 20px; }
    @media (max-width: 600px) { mat-dialog-content { min-width: 0; } }
  `]
})
export class ApplyDialogComponent {
  note = '';
  cvFile: File | null = null;

  result(): ApplyDialogResult {
    return { confirmed: true, note: this.note, cvFile: this.cvFile };
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
    StatusChipComponent
  ],
  templateUrl: './job-detail.component.html',
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
  checkingApplication = false;
  applying = false;
  /** Postęp wysyłania CV po złożeniu aplikacji (null = CV nie jest wysyłane). */
  cvUploadProgress: number | null = null;

  WorkType = WorkType;

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

  openApplyDialog(): void {
    if (!this.job) return;

    const dialogRef = this.dialog.open(ApplyDialogComponent);

    dialogRef.afterClosed().subscribe((result: ApplyDialogResult | undefined) => {
      if (result?.confirmed && this.job) {
        this.applyToJob(result.note, result.cvFile);
      }
    });
  }

  /** Najpierw składa aplikację, potem (opcjonalnie) dołącza CV – błąd CV nie cofa złożonej aplikacji. */
  applyToJob(note: string, cvFile: File | null = null): void {
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

        if (cvFile) {
          this.uploadCv(application?.id ?? null, cvFile, successMessage);
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

  private uploadCv(applicationId: number | null, file: File, successMessage: string): void {
    if (applicationId === null) {
      this.finishCvUpload(CV_UPLOAD_FAILED_MESSAGE, 8000);
      return;
    }

    this.cvUploadProgress = 0;
    this.cdr.detectChanges();
    // opuszczenie strony przerywa wysyłanie CV (aplikacja jest już złożona)
    this.applicationService.uploadCv(applicationId, file).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (event) => {
        if (event.type === 'progress') {
          this.cvUploadProgress = event.percent;
          this.cdr.detectChanges();
        } else {
          this.finishCvUpload(`${successMessage} CV zostało dołączone.`);
        }
      },
      error: (error) => {
        console.error('Error uploading CV:', error);
        // Aplikacja jest już złożona – informujemy o tym zawsze, także przy 401/403
        this.finishCvUpload(CV_UPLOAD_FAILED_MESSAGE, 8000);
      }
    });
  }

  private finishCvUpload(message: string, duration?: number): void {
    this.applying = false;
    this.cvUploadProgress = null;
    this.cdr.detectChanges();
    this.notify(message, duration);
  }

  private notify(message: string, duration = 5000): void {
    this.snackBar.open(message, 'OK', { duration });
  }

  getWorkTypeLabel(): string {
    if (!this.job) return '';
    switch (this.job.workType) {
      case WorkType.Remote: return 'Zdalna';
      case WorkType.Hybrid: return 'Hybrydowa';
      case WorkType.Onsite: return 'Stacjonarna';
      default: return 'Nieznany';
    }
  }

  formatSalary(): string {
    if (!this.job) return '';
    const { salaryMin, salaryMax } = this.job;
    if (!salaryMin && !salaryMax) return 'Nieokreślona';
    if (salaryMin && salaryMax) return `${salaryMin} - ${salaryMax} PLN`;
    if (salaryMin) return `Od ${salaryMin} PLN`;
    if (salaryMax) return `Do ${salaryMax} PLN`;
    return 'Nieokreślona';
  }

  goBack(): void {
    this.router.navigate(['/jobs']);
  }
}
