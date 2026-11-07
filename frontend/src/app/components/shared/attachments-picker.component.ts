import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApplicationAttachment, AttachmentKind, AttachmentUploadItem } from '../../models/job-application.model';
import {
  ATTACHMENT_ACCEPT, ATTACHMENT_KIND_OPTIONS, ATTACHMENTS_MAX_COUNT, defaultAttachmentKind, formatAttachmentsUsage,
  formatFileSize, PROFILE_COUNT_LIMIT_MESSAGE, PROFILE_TOTAL_SIZE_LIMIT_MESSAGE, selectAttachmentFiles
} from '../../utils/attachments';

let nextId = 0;

/**
 * Wybór wielu załączników (PDF / DOCX / PNG / JPEG) z rodzajem dla każdego pliku i walidacją po stronie klienta:
 * maks. 5 MB na plik, 25 MB i 10 plików łącznie z załącznikami już dołączonymi do aplikacji (`existing`).
 * Sam nie wysyła plików – rodzic przekazuje postęp przez `progress`, błąd API przez `errorMessage`,
 * a końcowy stan wysyłania (np. „Przerwano wysyłanie”) przez `statusMessage`.
 * Prawdziwe `<input type="file" multiple>` (ukryte wizualnie, dostępne z klawiatury) z etykietą w roli przycisku.
 */
@Component({
  selector: 'app-attachments-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule],
  template: `
    @if (items().length) {
      <ul class="files" aria-label="Pliki do wysłania">
        @for (item of items(); track item.file; let i = $index) {
          <li class="file">
            <mat-icon aria-hidden="true">{{ isImage(item.file) ? 'image' : 'description' }}</mat-icon>
            <span class="info">
              <span class="file-name" [id]="id('name', i)" [title]="item.file.name">{{ item.file.name }}</span>
              <span class="file-size">{{ formatSize(item.file.size) }}</span>
            </span>
            <span class="kind">
              <label [id]="id('kind-label', i)" [for]="id('kind', i)">Rodzaj</label>
              <select
                class="kind-select"
                [id]="id('kind', i)"
                [attr.aria-labelledby]="id('kind-label', i) + ' ' + id('name', i)"
                [disabled]="locked()"
                (change)="setKind(i, $event)">
                @for (option of kindOptions; track option.value) {
                  <option [value]="option.value" [selected]="option.value === item.kind">{{ option.label }}</option>
                }
              </select>
            </span>
            @if (!uploading()) {
              <button mat-icon-button type="button" class="remove-button" (click)="remove(i)" [disabled]="disabled()"
                [attr.aria-label]="'Usuń z listy: ' + item.file.name">
                <mat-icon aria-hidden="true">close</mat-icon>
              </button>
            }
          </li>
        }
      </ul>
    }

    <input
      type="file"
      multiple
      class="file-input"
      [id]="inputId"
      [accept]="accept"
      [disabled]="locked() || full()"
      [attr.aria-describedby]="statusId + ' ' + usageId"
      (change)="onFilesChange($event)" />
    <label class="choose-button" [for]="inputId" [class.disabled]="locked() || full()">
      <mat-icon aria-hidden="true">upload_file</mat-icon>
      {{ items().length ? 'Dodaj kolejne pliki' : label() }}
    </label>
    <p class="usage" [id]="usageId">{{ usageText() }}</p>

    <!-- stałe miejsce na podpowiedź / błąd / postęp – bez przesunięć układu -->
    <div class="status" [id]="statusId">
      @if (uploading()) {
        @let percent = progress() ?? 0;
        <!-- po wysłaniu całego żądania serwer jeszcze sprawdza i zapisuje pliki -->
        <mat-progress-bar
          [mode]="percent >= 100 ? 'indeterminate' : 'determinate'"
          [value]="percent"
          aria-label="Postęp wysyłania załączników"
          [attr.aria-valuetext]="progressText()" />
        <span class="progress-text" aria-hidden="true">{{ progressText() }}</span>
      } @else if (errorTexts().length) {
        <div class="error" role="alert">
          <mat-icon aria-hidden="true">error_outline</mat-icon>
          <span>
            @for (message of errorTexts(); track $index) {
              <span class="error-line">{{ message }}</span>
            }
          </span>
        </div>
      } @else {
        <p class="hint">{{ full() ? 'Osiągnięto limit 10 załączników' : 'PDF, DOCX, PNG lub JPEG – maks. 5 MB na plik' }}</p>
      }
    </div>

    <!-- jedyny region ogłaszający zmiany listy i stan wysyłania (bez kolejnych procentów) -->
    <p class="sr-only" role="status">{{ announcement() }}</p>
  `,
  styles: [`
    :host { display: block; }
    .files { list-style: none; margin: 0 0 8px; padding: 0; display: flex; flex-direction: column; gap: 6px; }
    /* pozycja = wypełnienie bez ramki (tło o stopień jaśniejsze niż karta/dialog) */
    .file { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; padding: 6px 4px 6px 12px;
      background: var(--color-selected, #262a30); border-radius: var(--radius-input, 8px); }
    .file > mat-icon { color: var(--color-text-secondary, #a7adb5); flex-shrink: 0; }
    .info { display: flex; flex-direction: column; min-width: 0; flex: 1 1 160px; }
    .file-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .file-size, .hint, .progress-text, .usage, .kind label { color: var(--color-text-secondary, #a7adb5); font-size: var(--font-size-sm, 13px); }
    .kind { display: flex; align-items: center; gap: 6px; }
    /* ramka --color-input-border – kontrast ≥ 3:1 z tłem (WCAG 1.4.11) */
    .kind-select { color-scheme: dark; background: var(--color-input-bg, #16191d); color: inherit; font: inherit; padding: 6px 8px;
      border: 1px solid var(--color-input-border, #6b727b); border-radius: var(--radius-input, 8px); min-height: 36px; }
    .kind-select:focus-visible { outline: 2px solid var(--color-accent, #5fd4e6); outline-offset: 2px; }

    /* ukryte wizualnie, ale fokusowalne i ogłaszane przez czytniki ekranu */
    .file-input, .sr-only { position: absolute; width: 1px; height: 1px; margin: -1px; padding: 0; overflow: hidden;
      clip-path: inset(50%); white-space: nowrap; border: 0; }
    /* wygląd przycisku obrysowanego (mat-stroked-button): wysokość 40, promień 20, cienka ramka */
    .choose-button { display: inline-flex; align-items: center; gap: 8px; min-height: 40px; padding: 0 20px;
      border-radius: var(--radius-button, 20px); cursor: pointer; box-shadow: inset 0 0 0 1px var(--color-outline-button, #4a5058);
      color: var(--color-text, #e9ebee); font-weight: 500; }
    .choose-button mat-icon { color: var(--color-text-secondary, #a7adb5); }
    .choose-button:hover { background-color: var(--color-selected, #262a30); }
    .file-input:focus-visible + .choose-button { outline: 2px solid var(--color-accent, #5fd4e6); outline-offset: 2px; }
    .choose-button.disabled { opacity: 0.5; cursor: default; pointer-events: none; }

    .usage { margin: 6px 0 0; }
    .status { min-height: 44px; margin-top: 4px; display: flex; flex-direction: column; gap: 4px; }
    .hint { margin: 0; }
    .error { display: flex; align-items: flex-start; gap: 6px; color: var(--color-danger, #f2a39c); font-size: var(--font-size-sm, 13px); }
    .error mat-icon { font-size: 18px; width: 18px; height: 18px; flex-shrink: 0; }
    .error-line { display: block; overflow-wrap: anywhere; }
  `]
})
export class AttachmentsPickerComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /**
   * Wybrane (poprawne) pliki z rodzajami – wiązanie dwukierunkowe `[(items)]` (input + output `itemsChange`;
   * bez model(), żeby nie dokładać kodu do paczki startowej).
   */
  readonly items = input<readonly AttachmentUploadItem[]>([]);
  readonly itemsChange = output<AttachmentUploadItem[]>();
  /** Załączniki już dołączone do aplikacji – wliczane do limitów liczby i łącznego rozmiaru. */
  readonly existing = input<readonly ApplicationAttachment[]>([]);
  /** Postęp wysyłania w procentach; null = nic nie jest wysyłane. */
  readonly progress = input<number | null>(null);
  /** Błąd z zewnątrz (np. komunikat API) – pokazywany, gdy nie ma błędu walidacji. */
  readonly errorMessage = input<string | null>(null);
  /** Komunikat dla czytników ekranu o zakończeniu wysyłania (np. „Przerwano wysyłanie”). */
  readonly statusMessage = input<string | null>(null);
  readonly disabled = input(false);
  readonly label = input('Wybierz pliki');
  /** Komunikaty limitów: pliki aplikacji (domyślnie) albo dokumenty profilu. */
  readonly limitContext = input<'application' | 'profile'>('application');

  readonly errors = signal<string[]>([]);
  private readonly selectionMessage = signal('');

  readonly errorTexts = computed(() => {
    const external = this.errorMessage();
    return this.errors().length ? this.errors() : external ? [external] : [];
  });
  readonly uploading = computed(() => this.progress() !== null);
  readonly locked = computed(() => this.disabled() || this.uploading());
  readonly count = computed(() => this.existing().length + this.items().length);
  readonly totalSize = computed(() =>
    this.existing().reduce((sum, a) => sum + a.size, 0) + this.items().reduce((sum, i) => sum + i.file.size, 0));
  readonly full = computed(() => this.count() >= ATTACHMENTS_MAX_COUNT);
  readonly usageText = computed(() =>
    `${formatAttachmentsUsage(this.totalSize())} · plików: ${this.count()} z ${ATTACHMENTS_MAX_COUNT}`);
  readonly progressText = computed(() => {
    const percent = this.progress() ?? 0;
    return percent >= 100 ? 'Przetwarzanie…' : `Wysyłanie… ${percent}%`;
  });
  /** Tekst regionu `role="status"` – zmienia się tylko przy zmianie etapu, nie przy każdym procencie. */
  readonly announcement = computed(() => {
    const final = this.statusMessage();
    if (final) return final;
    if (this.uploading()) {
      return (this.progress() ?? 0) >= 100 ? 'Przetwarzanie załączników…' : 'Wysyłanie załączników…';
    }
    return this.selectionMessage();
  });

  readonly accept = ATTACHMENT_ACCEPT;
  readonly kindOptions = ATTACHMENT_KIND_OPTIONS;
  readonly inputId = `attachments-picker-${nextId++}`;
  readonly statusId = `${this.inputId}-status`;
  readonly usageId = `${this.inputId}-usage`;

  id(part: string, index: number): string {
    return `${this.inputId}-${part}-${index}`;
  }

  onFilesChange(event: Event): void {
    const fileInput = event.target as HTMLInputElement;
    const files = Array.from(fileInput.files ?? []);
    // reset pola – ponowny wybór tych samych plików też wywoła change (i nie trzymamy listy plików w DOM)
    fileInput.value = '';
    if (!files.length) return;

    const current = this.items();
    const { accepted, errors } = selectAttachmentFiles(files, {
      count: this.count(),
      totalSize: this.totalSize(),
      files: current.map(item => item.file)
    }, this.limitContext() === 'profile'
      ? { count: PROFILE_COUNT_LIMIT_MESSAGE, totalSize: PROFILE_TOTAL_SIZE_LIMIT_MESSAGE }
      : undefined);
    this.errors.set(errors);
    if (!accepted.length) return;

    const usedKinds = [...this.existing().map(a => a.kind), ...current.map(item => item.kind)];
    const added = accepted.map(file => {
      const kind = defaultAttachmentKind(usedKinds);
      usedKinds.push(kind);
      return { file, kind };
    });
    this.itemsChange.emit([...current, ...added]);
    this.selectionMessage.set(`Dodano do listy: ${accepted.map(file => file.name).join(', ')}`);
  }

  setKind(index: number, event: Event): void {
    const kind = Number((event.target as HTMLSelectElement).value) as AttachmentKind;
    this.itemsChange.emit(this.items().map((item, i) => i === index ? { ...item, kind } : item));
  }

  /**
   * Usuwa pozycję. Fokus przenosimy PRZED usunięciem na sąsiednią pozycję (następną, a gdy jej brak – poprzednią)
   * albo na pole wyboru plików – te elementy zostają w DOM (śledzenie po pliku), więc fokus nie ginie.
   */
  remove(index: number): void {
    const removed = this.items()[index];
    if (!removed) return;

    const root = this.host.nativeElement;
    const buttons = root.querySelectorAll<HTMLElement>('.remove-button');
    const input = root.querySelector<HTMLInputElement>('.file-input');
    const target = buttons[index + 1] ?? buttons[index - 1] ?? input;
    // pełna lista (10/10) – po usunięciu pole i tak stanie się aktywne, włączamy je od razu, żeby przyjęło fokus
    if (target === input && input && this.full() && !this.disabled()) {
      input.disabled = false;
    }
    target?.focus();

    this.itemsChange.emit(this.items().filter((_, i) => i !== index));
    this.errors.set([]);
    this.selectionMessage.set(`Usunięto z listy: ${removed.file.name}`);
  }

  isImage(file: File): boolean {
    return /\.(png|jpe?g)$/i.test(file.name);
  }

  formatSize(bytes: number): string {
    return formatFileSize(bytes);
  }
}
