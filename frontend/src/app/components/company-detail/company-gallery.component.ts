import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { CompanyPhoto } from '../../models/company-media.model';
import { photoAlt, photosNoun } from './company-gallery';
import { CompanyGalleryDialogComponent, CompanyGalleryDialogData, GALLERY_DIALOG_CONFIG } from './company-gallery-dialog.component';

/** Liczba kafli na stronie profilu: 1 duży + 4 małe (ostatni z „+N zdjęć”, gdy jest ich więcej). */
const VISIBLE_TILES = 5;

/**
 * Galeria na profilu firmy: pierwsze zdjęcie duże, kolejne cztery małe; przy większej liczbie piąty kafel
 * („+N zdjęć”) otwiera pełną galerię. Każdy kafel to przycisk powiększający zdjęcie w dialogu.
 * Obrazy mają wymiary z API (bez przesunięć układu) i – poza pierwszym – ładują się leniwie;
 * dialog zamyka się sam przy nawigacji (closeOnNavigation).
 */
@Component({
  selector: 'app-company-gallery',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="gallery" [class.single]="photos().length === 1" aria-label="Zdjęcia firmy">
      @for (tile of tiles(); track tile.photo.id) {
        <li [class.large]="tile.index === 0">
          <button type="button" class="photo" [attr.aria-label]="tile.label" (click)="open(tile.index)">
            <img
              [src]="tile.photo.url"
              [attr.width]="tile.photo.width"
              [attr.height]="tile.photo.height"
              [alt]="tile.more ? '' : tile.alt"
              [attr.loading]="tile.index === 0 ? 'eager' : 'lazy'"
              decoding="async">
            @if (tile.more) {
              <span class="more" aria-hidden="true">{{ tile.more }}</span>
            }
          </button>
        </li>
      }
    </ul>
  `,
  styles: [`
    :host { display: block; }
    .gallery { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); grid-auto-rows: 120px; gap: 8px;
      margin: 0; padding: 0; list-style: none; }
    .gallery.single { grid-template-columns: minmax(0, 1fr); grid-auto-rows: 240px; }
    .large { grid-column: span 2; grid-row: span 2; }
    .single .large { grid-column: auto; grid-row: auto; }
    .photo { position: relative; display: block; width: 100%; height: 100%; padding: 0; border: 0; overflow: hidden;
      border-radius: 10px; background: var(--color-selected); cursor: zoom-in; }
    .photo img { display: block; width: 100%; height: 100%; object-fit: cover; }
    .more { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center;
      background: color-mix(in srgb, var(--color-bg) 72%, transparent); color: var(--color-text); font-size: 15px; font-weight: 500; }
    .photo:hover img { opacity: 0.9; }
    @media (max-width: 600px) {
      .gallery { grid-template-columns: repeat(2, minmax(0, 1fr)); grid-auto-rows: 100px; }
    }
  `]
})
export class CompanyGalleryComponent {
  private dialog = inject(MatDialog);

  readonly photos = input.required<readonly CompanyPhoto[]>();
  readonly companyName = input.required<string>();

  readonly tiles = computed(() => {
    const photos = this.photos();
    const name = this.companyName();
    const hasMore = photos.length > VISIBLE_TILES;
    return photos.slice(0, VISIBLE_TILES).map((photo, index) => {
      const alt = photoAlt(photo, name, index);
      const more = hasMore && index === VISIBLE_TILES - 1 ? photos.length - (VISIBLE_TILES - 1) : 0;
      return {
        photo,
        index,
        alt,
        more: more ? `+${more} ${photosNoun(more)}` : '',
        // nazwa zaczyna się od widocznego „+N zdjęć” (WCAG 2.5.3 – głosowe „kliknij +8 zdjęć” działa)
        label: more ? `+${more} ${photosNoun(more)}, pokaż wszystkie (${photos.length})` : `Powiększ zdjęcie: ${alt}`
      };
    });
  });

  open(index: number): void {
    const data: CompanyGalleryDialogData = { photos: this.photos(), companyName: this.companyName(), startIndex: index };
    this.dialog.open(CompanyGalleryDialogComponent, { ...GALLERY_DIALOG_CONFIG, data });
  }
}
