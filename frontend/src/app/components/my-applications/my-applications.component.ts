import { Component, inject, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { JobApplicationService } from '../../services/job-application.service';
import { ApplicationStatus, ApplicationStatusHistoryEntry, JobApplication } from '../../models/job-application.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../utils/http-errors';
import { canChangeCv, canWithdraw, getApplicationStatusPresentation, StatusPresentation } from '../../utils/status-labels';
import { formatFileSize } from '../../utils/cv-file';
import { StatusChipComponent } from '../shared/status-chip.component';
import { StatusTimelineComponent } from '../shared/status-timeline.component';
import { CvDialogComponent } from './cv-dialog.component';

@Component({
  selector: 'app-my-applications',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    StatusChipComponent,
    StatusTimelineComponent
  ],
  templateUrl: './my-applications.component.html',
  styleUrl: './my-applications.component.scss'
})
export class MyApplicationsComponent implements OnInit {
  private applicationService = inject(JobApplicationService);
  private router = inject(Router);
  private cdr = inject(ChangeDetectorRef);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);

  applications: JobApplication[] = [];
  displayedColumns: string[] = ['jobTitle', 'company', 'location', 'status', 'appliedAt', 'note', 'cv', 'actions'];
  loading = false;
  withdrawingId: number | null = null;
  downloadingCvId: number | null = null;
  deletingCvId: number | null = null;

  /** Aplikacja z rozwiniętą historią (null = żadna). */
  expandedId: number | null = null;
  historyLoadingId: number | null = null;
  /** Historia pobrana z GET /api/jobapplications/{id}, per aplikacja. */
  histories: Record<number, ApplicationStatusHistoryEntry[]> = {};

  ngOnInit(): void {
    this.loadApplications();
  }

  loadApplications(): void {
    this.loading = true;
    this.applicationService.getMyApplications().subscribe({
      next: (applications) => {
        this.applications = applications;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (error) => {
        console.error('Error loading applications:', error);
        this.loading = false;
        this.cdr.detectChanges();
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas ładowania aplikacji'));
        }
      }
    });
  }

  statusPresentation(status: ApplicationStatus): StatusPresentation {
    return getApplicationStatusPresentation(status);
  }

  canWithdraw(application: JobApplication): boolean {
    return canWithdraw(application.status);
  }

  canChangeCv(application: JobApplication): boolean {
    return canChangeCv(application.status);
  }

  formatFileSize(bytes: number | null): string {
    return formatFileSize(bytes);
  }

  /** Dodanie lub zmiana CV w dialogu z paskiem postępu. */
  openCvDialog(application: JobApplication): void {
    const hadCv = application.hasCv;
    const dialogRef = this.dialog.open(CvDialogComponent, { width: '520px', data: application });

    dialogRef.afterClosed().subscribe((updated: JobApplication | undefined) => {
      if (!updated) return;
      this.replaceApplication(updated);
      this.cdr.detectChanges();
      this.notify(hadCv ? 'CV zostało zastąpione' : 'CV zostało dołączone do aplikacji');
    });
  }

  downloadCv(application: JobApplication): void {
    this.downloadingCvId = application.id;
    this.applicationService.downloadCv(application.id, application.cvFileName).subscribe({
      next: () => {
        this.downloadingCvId = null;
        this.cdr.detectChanges();
      },
      error: (error) => {
        console.error('Error downloading CV:', error);
        this.downloadingCvId = null;
        this.cdr.detectChanges();
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Nie udało się pobrać CV'));
        }
      }
    });
  }

  deleteCv(application: JobApplication): void {
    if (!confirm(`Czy na pewno chcesz usunąć CV „${application.cvFileName}” z aplikacji na stanowisko „${application.jobTitle}”?`)) {
      return;
    }

    this.deletingCvId = application.id;
    this.applicationService.deleteCv(application.id).subscribe({
      next: () => {
        this.replaceApplication({ ...application, hasCv: false, cvFileName: null, cvSize: null, cvUploadedAt: null });
        this.deletingCvId = null;
        this.cdr.detectChanges();
        this.notify('CV zostało usunięte');
      },
      error: (error) => {
        console.error('Error deleting CV:', error);
        this.deletingCvId = null;
        this.cdr.detectChanges();
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Nie udało się usunąć CV'));
        }
      }
    });
  }

  private replaceApplication(updated: JobApplication): void {
    this.applications = this.applications.map(app => app.id === updated.id ? { ...app, ...updated } : app);
  }

  /** Rozwija/zwija historię; przy pierwszym rozwinięciu pobiera ją z API. */
  toggleHistory(application: JobApplication): void {
    if (this.expandedId === application.id) {
      this.expandedId = null;
      return;
    }

    this.expandedId = application.id;
    if (this.histories[application.id]) return;

    this.loadHistory(application.id);
  }

  /**
   * Pobiera historię aplikacji do cache. Odpowiedź może przyjść, gdy użytkownik rozwinął już inny wiersz –
   * spinner i zwijanie dotyczą wtedy tylko wiersza, którego odpowiedź dotyczy.
   */
  private loadHistory(id: number): void {
    this.historyLoadingId = id;
    this.applicationService.getApplication(id).subscribe({
      next: (details) => {
        this.histories = { ...this.histories, [id]: details.history ?? [] };
        if (this.historyLoadingId === id) {
          this.historyLoadingId = null;
        }
        this.cdr.detectChanges();
      },
      error: (error) => {
        console.error('Error loading application history:', error);
        if (this.historyLoadingId === id) {
          this.historyLoadingId = null;
        }
        if (this.expandedId === id) {
          this.expandedId = null;
        }
        this.cdr.detectChanges();
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Nie udało się wczytać historii aplikacji'));
        }
      }
    });
  }

  withdrawApplication(application: JobApplication): void {
    if (!confirm(`Czy na pewno chcesz wycofać aplikację na stanowisko „${application.jobTitle}”? ` +
      'Jeśli oferta będzie nadal aktywna, możesz później aplikować ponownie.')) {
      return;
    }

    this.withdrawingId = application.id;
    this.applicationService.withdraw(application.id).subscribe({
      next: (updated) => {
        this.applications = this.applications.map(app => app.id === updated.id ? { ...app, ...updated } : app);
        // historia w cache jest już nieaktualna – bierzemy ją z odpowiedzi albo pobieramy od nowa
        if (updated.history) {
          this.histories = { ...this.histories, [updated.id]: updated.history };
        } else {
          const { [updated.id]: _stale, ...rest } = this.histories;
          this.histories = rest;
          if (this.expandedId === updated.id) {
            this.loadHistory(updated.id);
          }
        }
        this.withdrawingId = null;
        this.cdr.detectChanges();
        this.notify('Aplikacja została wycofana');
      },
      error: (error) => {
        console.error('Error withdrawing application:', error);
        this.withdrawingId = null;
        this.cdr.detectChanges();
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas wycofywania aplikacji'));
        }
      }
    });
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }

  viewJobDetails(jobId: number): void {
    this.router.navigate(['/jobs', jobId]);
  }

  formatDate(date: Date | string): string {
    return new Date(date).toLocaleDateString('pl-PL', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }
}
