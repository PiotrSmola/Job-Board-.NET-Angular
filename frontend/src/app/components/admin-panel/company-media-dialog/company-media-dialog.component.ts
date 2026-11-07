import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { filter } from 'rxjs';
import { CompanyPhoto } from '../../../models/company-media.model';
import { Company } from '../../../models/company.model';
import { AdminService } from '../../../services/admin.service';
import { ConfirmService } from '../../../services/confirm.service';
import { EmployerMediaService } from '../../../services/employer-media.service';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { BlobImageDirective } from '../../employer-panel/company-media/blob-image.directive';

/**
 * Moderacja galerii i tła ofert firmy przez admina: podgląd i usuwanie zdjęć oraz obrazu tła.
 * Obrazy firmy niezatwierdzonej wymagają JWT, więc są pobierane jako Blob. Zamyka się wartością true po zmianach.
 */
@Component({
  selector: 'app-company-media-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, BlobImageDirective],
  template: `
    <h2 mat-dialog-title>Zdjęcia i tło – {{ company.name }}</h2>
    <mat-dialog-content>
      <section aria-labelledby="media-photos-heading">
        <h3 id="media-photos-heading">Zdjęcia <span class="tertiary num">({{ photos().length }})</span></h3>
        @if (loading()) {
          <div class="state"><mat-spinner diameter="32" aria-label="Wczytywanie zdjęć"></mat-spinner></div>
        } @else if (loadError(); as message) {
          <p class="danger-text" role="alert">{{ message }}</p>
        } @else if (photos().length) {
          <ul class="grid">
            @for (photo of photos(); track photo.id; let i = $index) {
              <li class="tile">
                <img [appBlobSrc]="photo.url" [attr.width]="photo.width" [attr.height]="photo.height"
                  [alt]="label(photo, i)" loading="lazy">
                <button mat-icon-button type="button" class="tool" [disabled]="busy()"
                  (click)="deletePhoto(photo, i)" [attr.aria-label]="'Usuń zdjęcie: ' + label(photo, i)">
                  <mat-icon aria-hidden="true">delete</mat-icon>
                </button>
                @if (photo.caption) {
                  <span class="caption">{{ photo.caption }}</span>
                }
              </li>
            }
          </ul>
        } @else {
          <p class="muted">Firma nie ma zdjęć.</p>
        }
      </section>

      <section aria-labelledby="media-background-heading">
        <h3 id="media-background-heading">Tło ofert</h3>
        @if (backgroundUrl(); as url) {
          <div class="background">
            <img [appBlobSrc]="url" alt="Obraz tła ofert firmy">
          </div>
          <button mat-button color="warn" type="button" (click)="deleteBackground()" [disabled]="busy()">Usuń tło</button>
        } @else {
          <p class="muted">Firma nie ma obrazu tła.</p>
        }
      </section>

      @if (error(); as message) {
        <p class="danger-text" role="alert">{{ message }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" (click)="close()">Zamknij</button>
    </mat-dialog-actions>
  `,
  styles: [`
    h3 { margin: 16px 0 8px; }
    section:first-child h3 { margin-top: 0; }
    .state { display: flex; justify-content: center; min-height: 120px; align-items: center; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(150px, 100%), 1fr)); gap: 10px; margin: 0; padding: 0; list-style: none; }
    .tile { position: relative; aspect-ratio: 4 / 3; border-radius: 10px; overflow: hidden; background: var(--color-selected); }
    .tile img, .background img { display: block; width: 100%; height: 100%; object-fit: cover; }
    .tool { position: absolute; top: 4px; right: 4px; --mat-icon-button-icon-color: var(--color-text);
      background: color-mix(in srgb, var(--color-bg) 78%, transparent); }
    .caption { position: absolute; inset: auto 0 0; padding: 4px 8px; overflow: hidden; font-size: var(--font-size-sm);
      text-overflow: ellipsis; white-space: nowrap; background: color-mix(in srgb, var(--color-bg) 82%, transparent); }
    .background { aspect-ratio: 16 / 5; max-width: 100%; margin-bottom: 8px; border-radius: var(--radius-card); overflow: hidden; background: var(--color-selected); }
  `]
})
export class CompanyMediaDialogComponent {
  private media = inject(EmployerMediaService);
  private adminService = inject(AdminService);
  private confirm = inject(ConfirmService);
  private snackBar = inject(MatSnackBar);
  private dialogRef = inject(MatDialogRef<CompanyMediaDialogComponent, boolean>);
  private destroyRef = inject(DestroyRef);

  readonly company = inject<Company>(MAT_DIALOG_DATA);
  readonly photos = signal<CompanyPhoto[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly backgroundUrl = signal<string | null>(this.company.offerBackground?.url ?? null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  private changed = false;

  constructor() {
    this.media.getPhotos(this.company.id).pipe(takeUntilDestroyed()).subscribe({
      next: photos => {
        this.photos.set(photos);
        this.loading.set(false);
      },
      error: error => {
        console.error('Error loading company photos:', error);
        this.loading.set(false);
        this.loadError.set(getApiErrorMessage(error, 'Nie udało się wczytać zdjęć'));
      }
    });
  }

  label(photo: CompanyPhoto, index: number): string {
    return photo.caption || `Zdjęcie ${index + 1}`;
  }

  deletePhoto(photo: CompanyPhoto, index: number): void {
    const label = this.label(photo, index);
    this.confirmThen(`Usunąć zdjęcie „${label}”?`, 'Zdjęcie zniknie z profilu firmy. Tej operacji nie da się cofnąć.', () =>
      this.adminService.deleteCompanyPhoto(this.company.id, photo.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: () => {
          this.photos.update(list => list.filter(p => p.id !== photo.id));
          this.done(`Usunięto zdjęcie: ${label}`);
        },
        error: error => this.fail(error, 'Nie udało się usunąć zdjęcia')
      }));
  }

  deleteBackground(): void {
    this.confirmThen('Usunąć tło ofert?', 'Nagłówki ofert firmy będą miały jednolite tło.', () =>
      this.adminService.deleteCompanyBackground(this.company.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: () => {
          this.backgroundUrl.set(null);
          this.done('Tło ofert zostało usunięte');
        },
        error: error => this.fail(error, 'Nie udało się usunąć tła')
      }));
  }

  close(): void {
    this.dialogRef.close(this.changed);
  }

  private confirmThen(title: string, message: string, action: () => void): void {
    this.confirm.confirm({ title, message, confirmLabel: 'Usuń', destructive: true })
      .pipe(filter(Boolean), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.busy.set(true);
        this.error.set(null);
        action();
      });
  }

  private done(message: string): void {
    this.busy.set(false);
    this.changed = true;
    this.snackBar.open(message, 'OK', { duration: 5000, politeness: 'polite' });
  }

  private fail(error: unknown, fallback: string): void {
    console.error(fallback, error);
    this.busy.set(false);
    if (!isHandledByInterceptor(error)) {
      this.error.set(getApiErrorMessage(error, fallback));
    }
  }
}
