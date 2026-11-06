import { Component, DestroyRef, inject, OnInit, ChangeDetectorRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
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
import { ApplicationAttachment, ApplicationStatus, ApplicationStatusHistoryEntry, JobApplication } from '../../models/job-application.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../utils/http-errors';
import { canAddAttachments, canWithdraw, getApplicationStatusPresentation, StatusPresentation } from '../../utils/status-labels';
import { ATTACHMENTS_MAX_COUNT } from '../../utils/attachments';
import { StatusChipComponent } from '../shared/status-chip.component';
import { StatusTimelineComponent } from '../shared/status-timeline.component';
import { AttachmentListComponent } from '../shared/attachment-list.component';
import {
  ATTACHMENTS_DIALOG_CONFIG,
  ATTACHMENTS_UPLOAD_ABORTED,
  AttachmentsDialogComponent,
  AttachmentsDialogResult
} from './attachments-dialog.component';

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
    StatusTimelineComponent,
    AttachmentListComponent
  ],
  templateUrl: './my-applications.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './my-applications.component.scss'
})
export class MyApplicationsComponent implements OnInit {
  private applicationService = inject(JobApplicationService);
  private router = inject(Router);
  private cdr = inject(ChangeDetectorRef);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);
  private destroyRef = inject(DestroyRef);

  applications: JobApplication[] = [];
  displayedColumns: string[] = ['jobTitle', 'company', 'location', 'status', 'appliedAt', 'note', 'attachments', 'actions'];
  loading = false;
  withdrawingId: number | null = null;

  /** Wiersze śledzone po id – podmiana aplikacji (np. po usunięciu załącznika) nie odtwarza wiersza, więc fokus zostaje. */
  readonly trackById = (_: number, application: JobApplication): number => application.id;

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

  /** Dodawanie załączników: dozwolony status i wolne miejsce w limicie liczby plików (usuwać można zawsze). */
  canAddAttachments(application: JobApplication): boolean {
    return canAddAttachments(application.status) && application.attachments.length < ATTACHMENTS_MAX_COUNT;
  }

  /** Dodanie załączników w dialogu z paskiem postępu. */
  openAttachmentsDialog(application: JobApplication): void {
    const dialogRef = this.dialog.open<AttachmentsDialogComponent, JobApplication, AttachmentsDialogResult>(
      AttachmentsDialogComponent, { ...ATTACHMENTS_DIALOG_CONFIG, data: application });

    dialogRef.afterClosed().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(result => {
      if (result === ATTACHMENTS_UPLOAD_ABORTED) {
        // przerwanie w fazie przetwarzania – serwer mógł już zapisać pliki, więc bierzemy stan z serwera
        this.notify('Przerwano wysyłanie załączników');
        this.refreshApplication(application.id);
        return;
      }
      if (!result) return;
      this.replaceApplication(result);
      this.cdr.detectChanges();
      this.notify('Załączniki zostały dodane do aplikacji');
    });
  }

  /** Załącznik usunięty w liście – aktualizujemy aplikację bez ponownego pobierania. */
  onAttachmentDeleted(applicationId: number, attachment: ApplicationAttachment): void {
    const current = this.applications.find(app => app.id === applicationId);
    if (!current) return;

    this.replaceApplication({
      ...current,
      attachments: current.attachments.filter(a => a.id !== attachment.id),
      attachmentsTotalSize: Math.max(0, current.attachmentsTotalSize - attachment.size)
    });
    this.cdr.detectChanges();
    this.notify('Załącznik został usunięty');
  }

  private refreshApplication(id: number): void {
    this.applicationService.getApplication(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (details) => {
        // szczegóły zawierają też historię – od razu aktualizujemy jej cache
        const { history, ...application } = details;
        this.replaceApplication(application);
        if (history) {
          this.histories = { ...this.histories, [id]: history };
        }
        this.cdr.detectChanges();
      },
      error: (error) => {
        console.error('Error refreshing application:', error);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Nie udało się odświeżyć aplikacji – odśwież stronę'));
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
