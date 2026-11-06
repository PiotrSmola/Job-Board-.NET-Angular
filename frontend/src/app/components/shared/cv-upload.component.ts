import { Component, computed, input, model, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { CV_ACCEPT, formatFileSize, validateCvFile } from '../../utils/cv-file';

/**
 * Wybór pliku CV (PDF / DOCX, maks. 5 MB) z walidacją po stronie klienta.
 * Sam nie wysyła pliku – rodzic przekazuje postęp wysyłania przez `progress`.
 */
@Component({
  selector: 'app-cv-upload',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule],
  template: `
    <input #fileInput type="file" class="file-input" [accept]="accept" (change)="onFileChange($event)" hidden />

    @if (file(); as selected) {
      <div class="selected-file">
        <mat-icon>description</mat-icon>
        <div class="file-info">
          <span class="file-name" [title]="selected.name">{{ selected.name }}</span>
          <span class="file-size">{{ formatSize(selected.size) }}</span>
        </div>
        @if (!uploading()) {
          <button mat-icon-button type="button" class="remove-button" (click)="clear()" [disabled]="disabled()"
            aria-label="Usuń wybrany plik">
            <mat-icon>close</mat-icon>
          </button>
        }
      </div>
    } @else {
      <button mat-stroked-button type="button" class="choose-button" (click)="fileInput.click()" [disabled]="disabled()">
        <mat-icon>upload_file</mat-icon>
        {{ label() }}
      </button>
    }

    @if (uploading()) {
      @let percent = progress() ?? 0;
      <!-- po wysłaniu całego pliku serwer go jeszcze sprawdza i zapisuje -->
      <div class="progress" role="status">
        <mat-progress-bar [mode]="percent >= 100 ? 'indeterminate' : 'determinate'" [value]="percent" />
        <span>{{ percent >= 100 ? 'Przetwarzanie…' : 'Wysyłanie… ' + percent + '%' }}</span>
      </div>
    }

    @if (error(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    } @else if (!file()) {
      <p class="hint">PDF lub DOCX, maks. 5 MB</p>
    }
  `,
  styles: [`
    :host { display: block; }
    .selected-file { display: flex; align-items: center; gap: 12px; padding: 8px 12px; border: 1px solid var(--border-color, #333); border-radius: 8px; }
    .selected-file > mat-icon { color: var(--primary-color, #00e5ff); flex-shrink: 0; }
    .file-info { display: flex; flex-direction: column; min-width: 0; flex: 1; }
    .file-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .file-size, .hint { color: var(--text-secondary, #b0b0b0); font-size: 0.85rem; }
    .hint, .error { margin: 6px 0 0; }
    .error { color: var(--error-color, #ff1744); font-size: 0.85rem; }
    .progress { display: flex; flex-direction: column; gap: 4px; margin-top: 8px; font-size: 0.85rem; color: var(--text-secondary, #b0b0b0); }
  `]
})
export class CvUploadComponent {
  /** Wybrany (poprawny) plik – wiązanie dwukierunkowe `[(file)]`. */
  readonly file = model<File | null>(null);
  /** Postęp wysyłania w procentach; null = nic nie jest wysyłane. */
  readonly progress = input<number | null>(null);
  readonly disabled = input(false);
  readonly label = input('Wybierz plik CV');

  readonly error = signal<string | null>(null);
  readonly uploading = computed(() => this.progress() !== null);
  readonly accept = CV_ACCEPT;

  onFileChange(event: Event): void {
    const fileInput = event.target as HTMLInputElement;
    const selected = fileInput.files?.[0] ?? null;
    // reset pola – ponowny wybór tego samego pliku też wywoła zdarzenie change
    fileInput.value = '';
    if (!selected) return;

    const error = validateCvFile(selected);
    this.error.set(error);
    if (!error) {
      this.file.set(selected);
    }
  }

  clear(): void {
    this.file.set(null);
    this.error.set(null);
  }

  formatSize(bytes: number): string {
    return formatFileSize(bytes);
  }
}
