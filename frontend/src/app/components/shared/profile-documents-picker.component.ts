import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { AccountService } from '../../services/account.service';
import { ProfileDocument } from '../../models/account.model';
import { ApplicationAttachment, AttachmentUploadItem } from '../../models/job-application.model';
import {
  ATTACHMENTS_MAX_COUNT, ATTACHMENTS_MAX_TOTAL_SIZE, formatFileSize, getAttachmentKindLabel
} from '../../utils/attachments';
import { getApiErrorMessage, isHandledByInterceptor } from '../../utils/http-errors';

let nextId = 0;

/**
 * „Dołącz z profilu” – checkboxy dokumentów z profilu kandydata (np. domyślne CV) w sekcji aplikowania na stronie oferty.
 * Limity aplikacji (10 plików, 25 MB) liczone są łącznie z plikami wybranymi w pickerze (`pendingItems`)
 * i załącznikami już dołączonymi (`existing`). Dokument, który by je przekroczył, jest zablokowany z wyjaśnieniem.
 * Dokumenty wczytuje sam przy utworzeniu; zniszczenie przerywa żądanie.
 */
@Component({
  selector: 'app-profile-documents-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCheckboxModule, RouterLink],
  template: `
    <fieldset class="profile-documents" [attr.aria-describedby]="hintId">
      <legend>Dołącz z profilu</legend>
      <!-- stałe minimum wysokości – wczytanie listy nie przesuwa reszty dialogu -->
      <div class="body" [attr.aria-busy]="state() === 'loading'">
        @switch (state()) {
          @case ('loading') {
            <p class="hint">Wczytywanie dokumentów z profilu…</p>
          }
          @case ('error') {
            <p class="error" role="alert">{{ errorMessage() }}</p>
          }
          @default {
            @if (documents().length) {
              <ul class="documents">
                @for (document of documents(); track document.id) {
                  <li>
                    <mat-checkbox
                      [checked]="isSelected(document.id)"
                      [disabled]="disabled() || (!isSelected(document.id) && !fits(document))"
                      (change)="toggle(document, $event.checked)">
                      {{ document.fileName }}
                      <span class="meta">({{ describe(document) }})</span>
                    </mat-checkbox>
                  </li>
                }
              </ul>
            } @else {
              <p class="hint">
                Nie masz jeszcze dokumentów w profilu. Możesz je dodać w
                <!-- nowa karta – przejście na konto nie porzuca wypełnianej aplikacji -->
                <a routerLink="/account" [queryParams]="{ tab: 'documents' }" target="_blank" rel="noopener">Moim koncie<span class="visually-hidden"> (otwiera się w nowej karcie)</span></a>.
              </p>
            }
          }
        }
      </div>
      <p class="hint" [id]="hintId">{{ limitHint() }}</p>
    </fieldset>
  `,
  styles: [`
    :host { display: block; margin-bottom: 12px; }
    /* fieldset bez ramki – grupę wyznacza legenda (jak filtry w makiecie) */
    .profile-documents { border: 0; padding: 0; margin: 0; min-width: 0; }
    legend { padding: 0; margin-bottom: 6px; font-weight: 500; }
    .body { min-height: 40px; }
    .documents { list-style: none; margin: 0; padding: 0; }
    .meta, .hint { color: var(--color-text-tertiary, #8b929b); font-size: var(--font-size-sm, 13px); }
    .hint { margin: 4px 0; }
    .hint a { color: var(--color-accent, #5fd4e6); text-decoration: underline; text-underline-offset: 2px; }
    .error { color: var(--color-danger, #f2a39c); font-size: var(--font-size-sm, 13px); margin: 4px 0; }
  `]
})
export class ProfileDocumentsPickerComponent {
  private readonly accountService = inject(AccountService);

  /** Nowe pliki wybrane w pickerze załączników – wliczane do limitów. */
  readonly pendingItems = input<readonly AttachmentUploadItem[]>([]);
  /** Załączniki już dołączone do aplikacji – wliczane do limitów. */
  readonly existing = input<readonly ApplicationAttachment[]>([]);
  readonly disabled = input(false);
  /** Wybrane dokumenty (kolejność jak na liście). */
  readonly selectionChange = output<ProfileDocument[]>();

  readonly documents = signal<ProfileDocument[]>([]);
  readonly state = signal<'loading' | 'loaded' | 'error'>('loading');
  readonly errorMessage = signal('');
  private readonly selectedIds = signal<ReadonlySet<number>>(new Set());

  readonly hintId = `profile-documents-${nextId++}-hint`;

  private readonly selected = computed(() => this.documents().filter(d => this.selectedIds().has(d.id)));
  private readonly usedCount = computed(() =>
    this.existing().length + this.pendingItems().length + this.selected().length);
  private readonly usedSize = computed(() =>
    this.existing().reduce((sum, a) => sum + a.size, 0)
    + this.pendingItems().reduce((sum, i) => sum + i.file.size, 0)
    + this.selected().reduce((sum, d) => sum + d.size, 0));

  readonly limitHint = computed(() => {
    if (this.usedCount() >= ATTACHMENTS_MAX_COUNT) return 'Osiągnięto limit 10 załączników – odznacz dokument lub usuń plik, aby wybrać inny';
    const blocked = this.documents().some(d => !this.selectedIds().has(d.id) && !this.fits(d));
    return blocked
      ? 'Część dokumentów przekroczyłaby łączny limit 25 MB – odznacz inny dokument lub usuń plik'
      : 'Kopie wybranych dokumentów zostaną dołączone do aplikacji (limit łącznie z nowymi plikami: 10 plików, 25 MB)';
  });

  constructor() {
    this.accountService.getDocuments().pipe(takeUntilDestroyed()).subscribe({
      next: documents => {
        this.documents.set(Array.isArray(documents) ? documents : []);
        this.state.set('loaded');
      },
      error: error => {
        console.error('Error loading profile documents:', error);
        this.errorMessage.set(isHandledByInterceptor(error)
          ? 'Nie udało się wczytać dokumentów z profilu'
          : getApiErrorMessage(error, 'Nie udało się wczytać dokumentów z profilu'));
        this.state.set('error');
      }
    });
  }

  isSelected(id: number): boolean {
    return this.selectedIds().has(id);
  }

  /** Czy dokument zmieści się w limitach aplikacji razem z tym, co już wybrano. */
  fits(document: ProfileDocument): boolean {
    return this.usedCount() < ATTACHMENTS_MAX_COUNT && this.usedSize() + document.size <= ATTACHMENTS_MAX_TOTAL_SIZE;
  }

  toggle(document: ProfileDocument, checked: boolean): void {
    if (checked && !this.isSelected(document.id) && !this.fits(document)) return;

    this.selectedIds.update(ids => {
      const next = new Set(ids);
      if (checked) next.add(document.id); else next.delete(document.id);
      return next;
    });
    this.selectionChange.emit(this.selected());
  }

  describe(document: ProfileDocument): string {
    return `${getAttachmentKindLabel(document.kind)}, ${formatFileSize(document.size)}`;
  }
}
