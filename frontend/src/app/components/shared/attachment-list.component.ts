import { AfterViewChecked, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { JobApplicationService } from '../../services/job-application.service';
import { ApplicationAttachment, AttachmentKind } from '../../models/job-application.model';
import { formatFileSize, getAttachmentKindLabel } from '../../utils/attachments';
import { getApiErrorMessage, isHandledByInterceptor } from '../../utils/http-errors';

/**
 * Lista załączników aplikacji z pobieraniem i (opcjonalnie) usuwaniem.
 * W trakcie pobierania/usuwania przyciski mają `aria-disabled` zamiast `disabled`, więc fokus nie ginie;
 * po usunięciu fokus przechodzi na następną pozycję (albo poprzednią, albo tekst pustej listy).
 * Zniszczenie komponentu przerywa trwające żądania.
 */
@Component({
  selector: 'app-attachment-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    @if (attachments().length) {
      <ul class="attachments">
        @for (attachment of attachments(); track attachment.id; let i = $index) {
          @let kind = kindLabel(attachment.kind);
          <li class="attachment">
            <button
              mat-button
              type="button"
              class="download-button"
              (click)="download(attachment)"
              [disabled]="downloadingIds().has(attachment.id)"
              [disabledInteractive]="true"
              [attr.aria-label]="'Pobierz ' + kind + ': ' + attachment.fileName">
              @if (downloadingIds().has(attachment.id)) {
                <mat-spinner diameter="18" aria-hidden="true"></mat-spinner>
              } @else {
                <mat-icon aria-hidden="true">download</mat-icon>
              }
              <span class="file-name" aria-hidden="true">{{ attachment.fileName }}</span>
            </button>
            <span class="meta">{{ meta(attachment) }}</span>
            @if (allowDelete()) {
              <button
                mat-icon-button
                type="button"
                class="delete-button"
                (click)="remove(attachment, i)"
                [disabled]="deletingId() === attachment.id"
                [disabledInteractive]="true"
                [attr.aria-label]="'Usuń ' + kind + ': ' + attachment.fileName">
                @if (deletingId() === attachment.id) {
                  <mat-spinner diameter="20" aria-hidden="true"></mat-spinner>
                } @else {
                  <mat-icon aria-hidden="true">delete_outline</mat-icon>
                }
              </button>
            }
          </li>
        }
      </ul>
    } @else {
      <span class="empty" tabindex="-1">{{ emptyText() }}</span>
    }
    @if (error(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
  `,
  styles: [`
    :host { display: block; }
    .attachments { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
    .attachment { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: center; column-gap: 4px; }
    .download-button { max-width: 100%; justify-content: flex-start; }
    .file-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .delete-button { grid-column: 2; grid-row: 1 / span 2; }
    /* #b0b0b0 na #1e1e1e – kontrast ok. 9:1 (WCAG AA) */
    .meta, .empty { color: var(--text-secondary, #b0b0b0); font-size: 0.8rem; }
    .meta { padding-left: 12px; }
    .error { color: #ff8a80; font-size: 0.85rem; margin: 4px 0 0; overflow-wrap: anywhere; }
  `]
})
export class AttachmentListComponent implements AfterViewChecked {
  private readonly applicationService = inject(JobApplicationService);
  private readonly destroyRef = inject(DestroyRef);
  /** Usunięto ostatnią pozycję – po przerysowaniu fokus trafia na tekst pustej listy. */
  private focusEmptyAfterId: number | null = null;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly applicationId = input.required<number>();
  readonly attachments = input<readonly ApplicationAttachment[]>([]);
  /** Usuwanie – tylko dla kandydata (właściciela aplikacji). */
  readonly allowDelete = input(false);
  readonly showDate = input(false);
  readonly emptyText = input('Brak');
  /** Załącznik usunięty na serwerze – rodzic aktualizuje listę. */
  readonly deleted = output<ApplicationAttachment>();

  readonly downloadingIds = signal<ReadonlySet<number>>(new Set());
  readonly deletingId = signal<number | null>(null);
  readonly error = signal<string | null>(null);

  kindLabel(kind: AttachmentKind): string {
    return getAttachmentKindLabel(kind);
  }

  /** Rodzaj, rozmiar i (opcjonalnie) data dodania, np. „CV · 350 KB · 02.01.2026”. */
  meta(attachment: ApplicationAttachment): string {
    const parts = [this.kindLabel(attachment.kind), formatFileSize(attachment.size)];
    if (this.showDate() && attachment.uploadedAt) {
      parts.push(new Date(attachment.uploadedAt).toLocaleDateString('pl-PL', { day: '2-digit', month: '2-digit', year: 'numeric' }));
    }
    return parts.join(' · ');
  }

  download(attachment: ApplicationAttachment): void {
    if (this.downloadingIds().has(attachment.id)) return;

    this.setDownloading(attachment.id, true);
    this.error.set(null);
    this.applicationService.downloadAttachment(this.applicationId(), attachment)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.setDownloading(attachment.id, false),
        error: (error) => {
          console.error('Error downloading attachment:', error);
          this.setDownloading(attachment.id, false);
          if (!isHandledByInterceptor(error)) {
            this.error.set(getApiErrorMessage(error, 'Nie udało się pobrać załącznika'));
          }
        }
      });
  }

  remove(attachment: ApplicationAttachment, index: number): void {
    if (this.deletingId() !== null) return;
    const kind = this.kindLabel(attachment.kind);
    if (!confirm(`Czy na pewno chcesz usunąć załącznik „${attachment.fileName}” (${kind})?`)) return;

    this.deletingId.set(attachment.id);
    this.error.set(null);
    this.applicationService.deleteAttachment(this.applicationId(), attachment.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.onDeleted(attachment, index),
        error: (error) => {
          console.error('Error deleting attachment:', error);
          this.deletingId.set(null);
          // załącznik usunięto już wcześniej (np. w innej karcie) – efekt jest taki sam
          if (error instanceof HttpErrorResponse && error.status === 404 && error.error?.message === 'Attachment not found') {
            this.onDeleted(attachment, index);
            return;
          }
          if (!isHandledByInterceptor(error)) {
            this.error.set(getApiErrorMessage(error, 'Nie udało się usunąć załącznika'));
          }
        }
      });
  }

  /**
   * Fokus przenosimy PRZED usunięciem pozycji na sąsiednią (następną, a gdy jej brak – poprzednią) – zostaje w DOM
   * (śledzenie po id). Po usunięciu ostatniej pozycji fokus trafia na tekst pustej listy, gdy ten się pojawi.
   */
  private onDeleted(attachment: ApplicationAttachment, index: number): void {
    this.deletingId.set(null);
    const buttons = this.host.nativeElement.querySelectorAll<HTMLElement>('.delete-button');
    const neighbour = buttons[index + 1] ?? buttons[index - 1];
    if (neighbour) {
      neighbour.focus();
    } else {
      this.focusEmptyAfterId = attachment.id;
    }
    this.deleted.emit(attachment);
  }

  ngAfterViewChecked(): void {
    const deletedId = this.focusEmptyAfterId;
    if (deletedId === null) return;

    const empty = this.host.nativeElement.querySelector<HTMLElement>('.empty');
    if (empty) {
      this.focusEmptyAfterId = null;
      empty.focus();
    } else if (!this.attachments().some(a => a.id === deletedId)) {
      // lista nie jest pusta (np. odświeżona z serwera) – nie przejmujemy fokusu później
      this.focusEmptyAfterId = null;
    }
  }

  private setDownloading(id: number, downloading: boolean): void {
    this.downloadingIds.update(ids => {
      const next = new Set(ids);
      if (downloading) next.add(id); else next.delete(id);
      return next;
    });
  }
}
