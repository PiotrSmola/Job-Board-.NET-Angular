import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subscription } from 'rxjs';
import { AccountService } from '../../../services/account.service';
import { ProfileDocument } from '../../../models/account.model';
import { ApplicationAttachment, AttachmentUploadItem } from '../../../models/job-application.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { AttachmentListActions, AttachmentListComponent } from '../../shared/attachment-list.component';
import { AttachmentsPickerComponent } from '../../shared/attachments-picker.component';

/** Kolejność jak przy załącznikach aplikacji: rodzaj, potem data dodania. */
function sortDocuments(documents: ProfileDocument[]): ProfileDocument[] {
  return [...documents].sort((a, b) => a.kind - b.kind || a.uploadedAt.localeCompare(b.uploadedAt) || a.id - b.id);
}

/**
 * Dokumenty profilu kandydata (np. domyślne CV) – później dołączane do aplikacji jednym kliknięciem.
 * Lista z pobieraniem/usuwaniem (wspólny komponent załączników) i picker nowych plików z łącznymi limitami profilu.
 */
@Component({
  selector: 'app-documents-tab',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, AttachmentListComponent, AttachmentsPickerComponent],
  templateUrl: './documents-tab.component.html',
  styleUrl: './documents-tab.component.scss'
})
export class DocumentsTabComponent {
  private readonly accountService = inject(AccountService);
  private readonly destroyRef = inject(DestroyRef);
  private uploadSubscription: Subscription | null = null;

  readonly state = signal<'loading' | 'loaded' | 'error'>('loading');
  readonly loadError = signal('');
  readonly documents = signal<ProfileDocument[]>([]);
  readonly items = signal<AttachmentUploadItem[]>([]);
  readonly progress = signal<number | null>(null);
  readonly uploadError = signal<string | null>(null);
  readonly uploadStatus = signal<string | null>(null);

  readonly listActions: AttachmentListActions = {
    download: (document: ApplicationAttachment) => this.accountService.downloadDocument(document),
    delete: (document: ApplicationAttachment) => this.accountService.deleteDocument(document.id)
  };

  constructor() {
    this.load();
    // opuszczenie strony przerywa wysyłanie
    this.destroyRef.onDestroy(() => this.uploadSubscription?.unsubscribe());
  }

  load(): void {
    this.state.set('loading');
    this.accountService.getDocuments().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: documents => {
        this.documents.set(sortDocuments(Array.isArray(documents) ? documents : []));
        this.state.set('loaded');
      },
      error: error => {
        console.error('Error loading profile documents:', error);
        this.loadError.set(getApiErrorMessage(error, 'Nie udało się wczytać dokumentów'));
        this.state.set('error');
      }
    });
  }

  onItemsChange(items: AttachmentUploadItem[]): void {
    this.items.set(items);
    this.uploadError.set(null);
    this.uploadStatus.set(null);
  }

  upload(): void {
    const items = this.items();
    if (!items.length || this.progress() !== null) return;

    this.uploadError.set(null);
    this.uploadStatus.set(null);
    this.progress.set(0);
    this.uploadSubscription = this.accountService.uploadDocuments(items).subscribe({
      next: event => {
        if (event.type === 'progress') {
          this.progress.set(event.percent);
          return;
        }
        // API zwraca listę dokumentów profilu – łączymy po id (odporne także na odpowiedź z samymi nowymi)
        const byId = new Map(this.documents().map(d => [d.id, d] as const));
        for (const document of event.documents ?? []) byId.set(document.id, document);
        this.documents.set(sortDocuments([...byId.values()]));
        this.items.set([]);
        this.progress.set(null);
        this.uploadStatus.set(items.length === 1 ? 'Dokument został dodany' : 'Dokumenty zostały dodane');
      },
      error: error => {
        console.error('Error uploading profile documents:', error);
        this.progress.set(null);
        this.uploadStatus.set('Przerwano wysyłanie');
        this.uploadError.set(isHandledByInterceptor(error)
          ? 'Nie udało się dodać dokumentów'
          : getApiErrorMessage(error, 'Nie udało się dodać dokumentów'));
      }
    });
  }

  onDeleted(document: ApplicationAttachment): void {
    this.documents.update(documents => documents.filter(d => d.id !== document.id));
  }
}
