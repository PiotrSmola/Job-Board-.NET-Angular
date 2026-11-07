import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogConfig, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { CompanyPhoto } from '../../models/company-media.model';
import { photoAlt } from './company-gallery';

export interface CompanyGalleryDialogData {
  photos: readonly CompanyPhoto[];
  companyName: string;
  /** Indeks zdjęcia otwartego jako pierwsze. */
  startIndex: number;
}

/** Dialog pełnej galerii: modalny dla czytników ekranu, fokus wraca na kafel, który go otworzył. */
export const GALLERY_DIALOG_CONFIG: MatDialogConfig = {
  ariaModal: true,
  // fokus wewnątrz dialogu (pierwszy przycisk) – strzałki obsługuje host komponentu
  autoFocus: 'first-tabbable',
  restoreFocus: true,
  width: 'min(1100px, 96vw)',
  maxWidth: '96vw'
};

/**
 * Pełna galeria zdjęć firmy: jedno zdjęcie naraz, przyciski i strzałki ←/→ (z zawijaniem), Escape zamyka
 * (domyślne zachowanie MatDialog). Numer i podpis bieżącego zdjęcia ogłasza region `role="status"`.
 */
@Component({
  selector: 'app-company-gallery-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  host: {
    '(keydown.arrowleft)': 'onArrow($event, -1)',
    '(keydown.arrowright)': 'onArrow($event, 1)'
  },
  template: `
    <h2 mat-dialog-title>Zdjęcia firmy {{ data.companyName }}</h2>
    <mat-dialog-content>
      @if (photo(); as current) {
        <figure>
          <!-- wymiary z API rezerwują proporcje obrazu przed wczytaniem -->
          <img [src]="current.url" [attr.width]="current.width" [attr.height]="current.height" [alt]="alt()" decoding="async">
          @if (current.caption) {
            <figcaption>{{ current.caption }}</figcaption>
          }
        </figure>
      }
    </mat-dialog-content>
    <mat-dialog-actions>
      @if (count() > 1) {
        <button mat-icon-button type="button" class="prev" aria-label="Poprzednie zdjęcie" (click)="move(-1)">
          <mat-icon aria-hidden="true">chevron_left</mat-icon>
        </button>
        <p class="counter" role="status" aria-live="polite" aria-atomic="true">Zdjęcie {{ index() + 1 }} z {{ count() }}<span class="visually-hidden">: {{ alt() }}</span></p>
        <button mat-icon-button type="button" class="next" aria-label="Następne zdjęcie" (click)="move(1)">
          <mat-icon aria-hidden="true">chevron_right</mat-icon>
        </button>
      }
      <button mat-button type="button" class="close" mat-dialog-close>Zamknij</button>
    </mat-dialog-actions>
  `,
  styles: [`
    figure { display: flex; flex-direction: column; align-items: center; gap: 8px; margin: 0; }
    img { display: block; max-width: 100%; max-height: min(70vh, 800px); width: auto; height: auto;
      border-radius: var(--radius-card); background: var(--color-selected); }
    figcaption { color: var(--color-text-prose); text-align: center; }
    mat-dialog-actions { gap: 4px; }
    .counter { margin: 0 8px; color: var(--color-text-secondary); font-variant-numeric: tabular-nums; }
    .close { margin-left: auto; }
  `]
})
export class CompanyGalleryDialogComponent {
  readonly data = inject<CompanyGalleryDialogData>(MAT_DIALOG_DATA);

  readonly count = computed(() => this.data.photos.length);
  readonly index = signal(Math.min(Math.max(0, this.data.startIndex), Math.max(0, this.data.photos.length - 1)));
  readonly photo = computed(() => this.data.photos[this.index()] ?? null);
  readonly alt = computed(() => {
    const photo = this.photo();
    return photo ? photoAlt(photo, this.data.companyName, this.index()) : '';
  });

  move(step: number): void {
    const count = this.count();
    if (count < 2) return;
    this.index.update(i => (i + step + count) % count);
  }

  onArrow(event: Event, step: number): void {
    event.preventDefault();
    this.move(step);
  }
}
