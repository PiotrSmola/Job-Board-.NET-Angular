import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { filter, switchMap, tap } from 'rxjs';
import { JobApplicationService } from '../../services/job-application.service';
import { ConfirmService } from '../../services/confirm.service';
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

/** Statusy końcowe – aplikacje w zakładce „Zakończone”; pozostałe są „W toku”. */
export const CLOSED_APPLICATION_STATUSES: readonly ApplicationStatus[] = [
  ApplicationStatus.Hired,
  ApplicationStatus.Rejected,
  ApplicationStatus.Withdrawn
];

export function isClosedApplication(status: ApplicationStatus): boolean {
  return CLOSED_APPLICATION_STATUSES.includes(status);
}

/** „3 listopada” (z rokiem, gdy inny niż bieżący) – od kiedy aplikacja ma obecny status. */
export function formatStatusSince(date: string | Date, now: Date = new Date()): string {
  const value = new Date(date);
  if (Number.isNaN(value.getTime())) return '';
  return value.toLocaleDateString('pl-PL', {
    day: 'numeric',
    month: 'long',
    ...(value.getFullYear() === now.getFullYear() ? {} : { year: 'numeric' })
  });
}

type LoadState = 'loading' | 'loaded' | 'error';

/**
 * „Moje aplikacje” (makieta MojeAplikacje.dc.html): zakładki „W toku” / „Zakończone” (podział po statusie),
 * karty z przyciskiem rozwijania (`aria-expanded`/`aria-controls`), a w szczegółach oś czasu, pełna treść wiadomości,
 * załączniki z pobieraniem/usuwaniem, „Dodaj załączniki” i „Wycofaj aplikację” (potwierdzenie przez ConfirmService).
 * Historia pobierana przy pierwszym rozwinięciu i trzymana w pamięci podręcznej komponentu.
 */
@Component({
  selector: 'app-my-applications',
  standalone: true,
  imports: [
    NgTemplateOutlet,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTabsModule,
    StatusChipComponent,
    StatusTimelineComponent,
    AttachmentListComponent
  ],
  templateUrl: './my-applications.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './my-applications.component.scss'
})
export class MyApplicationsComponent {
  private readonly applicationService = inject(JobApplicationService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);
  private readonly confirmService = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly heading = viewChild.required<ElementRef<HTMLElement>>('heading');

  readonly applications = signal<JobApplication[]>([]);
  readonly state = signal<LoadState>('loading');
  readonly loadError = signal('');
  /** 0 = „W toku”, 1 = „Zakończone”. */
  readonly selectedTab = signal(0);
  readonly activeApplications = computed(() => this.applications().filter(a => !isClosedApplication(a.status)));
  readonly closedApplications = computed(() => this.applications().filter(a => isClosedApplication(a.status)));

  /** Rozwinięte karty (można rozwinąć kilka naraz). */
  readonly expandedIds = signal<ReadonlySet<number>>(new Set());
  readonly historyLoadingIds = signal<ReadonlySet<number>>(new Set());
  /** Historia pobrana z GET /api/jobapplications/{id}, per aplikacja. */
  readonly histories = signal<Readonly<Record<number, ApplicationStatusHistoryEntry[]>>>({});
  readonly withdrawingId = signal<number | null>(null);

  constructor() {
    this.loadApplications();
  }

  loadApplications(): void {
    this.state.set('loading');
    this.applicationService.getMyApplications().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: applications => {
        this.applications.set(applications);
        // nic w toku, ale są zakończone – od razu pokazujemy zakończone
        if (!this.activeApplications().length && this.closedApplications().length) {
          this.selectedTab.set(1);
        }
        this.state.set('loaded');
      },
      error: error => {
        console.error('Error loading applications:', error);
        this.loadError.set(isHandledByInterceptor(error)
          ? 'Nie udało się wczytać aplikacji'
          : getApiErrorMessage(error, 'Nie udało się wczytać aplikacji – spróbuj ponownie'));
        this.state.set('error');
      }
    });
  }

  statusPresentation(status: ApplicationStatus): StatusPresentation {
    return getApplicationStatusPresentation(status);
  }

  statusSince(application: JobApplication): string {
    return formatStatusSince(application.statusChangedAt || application.appliedAt);
  }

  canWithdraw(application: JobApplication): boolean {
    return canWithdraw(application.status);
  }

  /** Dodawanie załączników: dozwolony status i wolne miejsce w limicie liczby plików (usuwać można zawsze). */
  canAddAttachments(application: JobApplication): boolean {
    return canAddAttachments(application.status) && application.attachments.length < ATTACHMENTS_MAX_COUNT;
  }

  isExpanded(id: number): boolean {
    return this.expandedIds().has(id);
  }

  /** Rozwija/zwija szczegóły; przy pierwszym rozwinięciu pobiera historię z API. */
  toggleDetails(application: JobApplication): void {
    const id = application.id;
    if (this.isExpanded(id)) {
      this.expandedIds.update(ids => without(ids, id));
      return;
    }
    this.expandedIds.update(ids => new Set(ids).add(id));
    if (!this.histories()[id] && !this.historyLoadingIds().has(id)) {
      this.loadHistory(id);
    }
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
      this.notifySuccess('Załączniki zostały dodane do aplikacji');
    });
  }

  /** Załącznik usunięty w liście – aktualizujemy aplikację bez ponownego pobierania. */
  onAttachmentDeleted(applicationId: number, attachment: ApplicationAttachment): void {
    const current = this.applications().find(app => app.id === applicationId);
    if (!current) return;

    this.replaceApplication({
      ...current,
      attachments: current.attachments.filter(a => a.id !== attachment.id),
      attachmentsTotalSize: Math.max(0, current.attachmentsTotalSize - attachment.size)
    });
    this.notifySuccess('Załącznik został usunięty');
  }

  withdrawApplication(application: JobApplication): void {
    if (this.withdrawingId() !== null) return;

    this.confirmService.confirm({
      title: 'Wycofać aplikację?',
      message: `Aplikacja na stanowisko „${application.jobTitle}” zostanie wycofana. ` +
        'Jeśli oferta będzie nadal aktywna, możesz później aplikować ponownie.',
      confirmLabel: 'Wycofaj aplikację',
      destructive: true
    }).pipe(
      filter(Boolean),
      tap(() => this.withdrawingId.set(application.id)),
      switchMap(() => this.applicationService.withdraw(application.id)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: updated => {
        const { history, ...rest } = updated;
        this.replaceApplication(rest);
        // historia w cache jest już nieaktualna – bierzemy ją z odpowiedzi albo pobieramy od nowa
        if (history) {
          this.histories.update(all => ({ ...all, [updated.id]: history }));
        } else {
          this.histories.update(all => {
            const { [updated.id]: _stale, ...others } = all;
            return others;
          });
          if (this.isExpanded(updated.id)) {
            this.loadHistory(updated.id);
          }
        }
        this.withdrawingId.set(null);
        // karta przeszła do „Zakończone” i znika z bieżącej zakładki – fokus na nagłówek strony, żeby nie zginął
        this.heading().nativeElement.focus();
        this.notifySuccess('Aplikacja została wycofana – znajdziesz ją w zakładce „Zakończone”');
      },
      error: error => {
        console.error('Error withdrawing application:', error);
        this.withdrawingId.set(null);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Nie udało się wycofać aplikacji'));
        }
      }
    });
  }

  private refreshApplication(id: number): void {
    this.applicationService.getApplication(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: details => {
        // szczegóły zawierają też historię – od razu aktualizujemy jej cache
        const { history, ...application } = details;
        this.replaceApplication(application);
        if (history) {
          this.histories.update(all => ({ ...all, [id]: history }));
        }
      },
      error: error => {
        console.error('Error refreshing application:', error);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Nie udało się odświeżyć aplikacji – odśwież stronę'));
        }
      }
    });
  }

  private replaceApplication(updated: JobApplication): void {
    this.applications.update(all => all.map(app => app.id === updated.id ? { ...app, ...updated } : app));
  }

  /** Pobiera historię aplikacji do cache; przy błędzie zwija tylko kartę, której odpowiedź dotyczy. */
  private loadHistory(id: number): void {
    this.historyLoadingIds.update(ids => new Set(ids).add(id));
    this.applicationService.getApplication(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: details => {
        this.histories.update(all => ({ ...all, [id]: details.history ?? [] }));
        this.historyLoadingIds.update(ids => without(ids, id));
      },
      error: error => {
        console.error('Error loading application history:', error);
        this.historyLoadingIds.update(ids => without(ids, id));
        this.expandedIds.update(ids => without(ids, id));
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Nie udało się wczytać historii aplikacji'));
        }
      }
    });
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }

  private notifySuccess(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000, politeness: 'polite' });
  }
}

function without(ids: ReadonlySet<number>, id: number): ReadonlySet<number> {
  const next = new Set(ids);
  next.delete(id);
  return next;
}
