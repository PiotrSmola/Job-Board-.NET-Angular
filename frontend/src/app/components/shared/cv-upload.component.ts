import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { CV_ACCEPT, formatFileSize, validateCvFile } from '../../utils/cv-file';

let nextId = 0;

/**
 * Wybór pliku CV (PDF / DOCX, maks. 5 MB) z walidacją po stronie klienta.
 * Sam nie wysyła pliku – rodzic przekazuje postęp wysyłania przez `progress` i ewentualny błąd API przez `errorMessage`.
 * Prawdziwe `<input type="file">` (ukryte wizualnie, dostępne z klawiatury) z etykietą w roli przycisku.
 */
@Component({
  selector: 'app-cv-upload',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule],
  template: `
    <div class="picker">
      @if (file(); as selected) {
        <div class="selected-file">
          <mat-icon aria-hidden="true">description</mat-icon>
          <div class="file-info">
            <span class="file-name" [title]="selected.name">{{ selected.name }}</span>
            <span class="file-size">{{ formatSize(selected.size) }}</span>
          </div>
          @if (!uploading()) {
            <button mat-icon-button type="button" class="remove-button" (click)="clear()" [disabled]="disabled()"
              [attr.aria-label]="'Usuń wybrany plik: ' + selected.name">
              <mat-icon aria-hidden="true">close</mat-icon>
            </button>
          }
        </div>
      }

      <input
        type="file"
        class="file-input"
        [id]="inputId"
        [accept]="accept"
        [disabled]="disabled() || uploading()"
        [attr.aria-describedby]="statusId"
        (change)="onFileChange($event)" />
      <label class="choose-button" [for]="inputId" [class.disabled]="disabled() || uploading()">
        <mat-icon aria-hidden="true">upload_file</mat-icon>
        {{ file() ? 'Wybierz inny plik' : label() }}
      </label>
    </div>

    <!-- stałe miejsce na podpowiedź / błąd / postęp – bez przesunięć układu -->
    <div class="status" [id]="statusId">
      @if (uploading()) {
        @let percent = progress() ?? 0;
        <!-- po wysłaniu całego pliku serwer go jeszcze sprawdza i zapisuje -->
        <mat-progress-bar
          [mode]="percent >= 100 ? 'indeterminate' : 'determinate'"
          [value]="percent"
          aria-label="Postęp wysyłania CV"
          [attr.aria-valuetext]="progressText()" />
        <span class="progress-text" aria-hidden="true">{{ progressText() }}</span>
      } @else if (errorText(); as message) {
        <p class="error" role="alert">
          <mat-icon aria-hidden="true">error_outline</mat-icon>
          <span>{{ message }}</span>
        </p>
      } @else {
        <p class="hint">PDF lub DOCX, maks. 5 MB</p>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .picker { display: flex; flex-direction: column; gap: 8px; align-items: flex-start; }
    .selected-file { display: flex; align-items: center; gap: 12px; padding: 4px 4px 4px 12px; width: 100%; box-sizing: border-box;
      border: 1px solid var(--border-color, #333); border-radius: 8px; }
    .selected-file > mat-icon { color: var(--primary-color, #00e5ff); flex-shrink: 0; }
    .file-info { display: flex; flex-direction: column; min-width: 0; flex: 1; }
    .file-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .file-size, .hint, .progress-text { color: var(--text-secondary, #b0b0b0); font-size: 0.85rem; }

    /* ukryte wizualnie, ale fokusowalne i ogłaszane przez czytniki ekranu */
    .file-input { position: absolute; width: 1px; height: 1px; margin: -1px; padding: 0; overflow: hidden;
      clip-path: inset(50%); white-space: nowrap; border: 0; }
    .choose-button { display: inline-flex; align-items: center; gap: 8px; padding: 8px 16px; border-radius: 20px; cursor: pointer;
      border: 1px solid var(--primary-color, #00e5ff); color: var(--primary-color, #00e5ff); font-weight: 500; }
    .choose-button:hover { background-color: rgba(0, 229, 255, 0.08); }
    .file-input:focus-visible + .choose-button { outline: 3px solid var(--primary-color, #00e5ff); outline-offset: 2px; }
    .choose-button.disabled { opacity: 0.5; cursor: default; pointer-events: none; }

    .status { min-height: 44px; margin-top: 6px; display: flex; flex-direction: column; justify-content: flex-start; gap: 4px; }
    .hint, .error { margin: 0; }
    /* jaśniejszy czerwony niż --error-color – kontrast ≥ 4.5:1 na ciemnym tle */
    .error { display: flex; align-items: flex-start; gap: 6px; color: #ff8a80; font-size: 0.85rem; }
    .error mat-icon { font-size: 18px; width: 18px; height: 18px; flex-shrink: 0; }
  `]
})
export class CvUploadComponent {
  /**
   * Wybrany (poprawny) plik – wiązanie dwukierunkowe `[(file)]` (input + output `fileChange`;
   * bez model(), żeby nie dokładać kodu do paczki startowej).
   */
  readonly file = input<File | null>(null);
  readonly fileChange = output<File | null>();
  /** Postęp wysyłania w procentach; null = nic nie jest wysyłane. */
  readonly progress = input<number | null>(null);
  /** Błąd z zewnątrz (np. komunikat API) – pokazywany, gdy nie ma błędu walidacji. */
  readonly errorMessage = input<string | null>(null);
  readonly disabled = input(false);
  readonly label = input('Wybierz plik CV');

  readonly error = signal<string | null>(null);
  readonly errorText = computed(() => this.error() ?? this.errorMessage());
  readonly uploading = computed(() => this.progress() !== null);
  readonly progressText = computed(() => {
    const percent = this.progress() ?? 0;
    return percent >= 100 ? 'Przetwarzanie…' : `Wysyłanie… ${percent}%`;
  });

  readonly accept = CV_ACCEPT;
  readonly inputId = `cv-upload-${nextId++}`;
  readonly statusId = `${this.inputId}-status`;

  onFileChange(event: Event): void {
    const fileInput = event.target as HTMLInputElement;
    const selected = fileInput.files?.[0] ?? null;
    // reset pola – ponowny wybór tego samego pliku też wywoła zdarzenie change (i nie trzymamy listy plików w DOM)
    fileInput.value = '';
    if (!selected) return;

    const error = validateCvFile(selected);
    this.error.set(error);
    if (!error) {
      this.fileChange.emit(selected);
    }
  }

  clear(): void {
    this.fileChange.emit(null);
    this.error.set(null);
  }

  formatSize(bytes: number): string {
    return formatFileSize(bytes);
  }
}
