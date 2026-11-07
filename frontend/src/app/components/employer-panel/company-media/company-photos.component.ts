import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, OnInit, afterNextRender, computed, inject, input, signal, viewChild
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CdkDrag, CdkDragDrop, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, Subscription, catchError, filter, of, switchMap, tap } from 'rxjs';
import { CompanyPhoto, MAX_COMPANY_PHOTOS } from '../../../models/company-media.model';
import { Company } from '../../../models/company.model';
import { ConfirmService } from '../../../services/confirm.service';
import { EmployerMediaService, IMAGE_ACCEPT, selectPhotoFiles } from '../../../services/employer-media.service';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { BlobImageDirective } from './blob-image.directive';
import { PhotoCaptionDialogComponent, PhotoCaptionDialogData } from './photo-caption-dialog.component';

/**
 * Galeria firmy w profilu pracodawcy: siatka miniatur z podpisem, edycja podpisu, usuwanie, dodawanie wielu
 * plików z postępem, zmiana kolejności przeciąganiem (CDK) albo przyciskami „Przesuń w lewo/prawo”.
 * Zmiany są ogłaszane w regionie `role="status"`, błędy w `role="alert"`.
 */
@Component({
  selector: 'app-company-photos',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CdkDropList, CdkDrag, MatButtonModule, MatIconModule, MatProgressBarModule, BlobImageDirective],
  templateUrl: './company-photos.component.html',
  styleUrl: './company-photos.component.scss'
})
export class CompanyPhotosComponent implements OnInit {
  private media = inject(EmployerMediaService);
  private confirm = inject(ConfirmService);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);
  private injector = inject(Injector);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly company = input.required<Company>();

  readonly max = MAX_COMPANY_PHOTOS;
  readonly accept = IMAGE_ACCEPT;
  readonly skeleton = [1, 2, 3, 4];

  readonly photos = signal<CompanyPhoto[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly uploadProgress = signal<number | null>(null);
  readonly errors = signal<string[]>([]);
  readonly announcement = signal('');
  /** Id zdjęcia, na którym trwa zapis podpisu lub usuwanie. */
  readonly busyId = signal<number | null>(null);

  readonly uploading = computed(() => this.uploadProgress() !== null);
  readonly full = computed(() => this.photos().length >= MAX_COMPANY_PHOTOS);
  readonly progressText = computed(() => {
    const percent = this.uploadProgress() ?? 0;
    return percent >= 100 ? 'Przetwarzanie zdjęć…' : `Wysyłanie zdjęć: ${percent}%`;
  });

  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');
  private readonly load$ = new Subject<void>();
  private readonly order$ = new Subject<number[]>();
  /** Kolejność ostatnio potwierdzona przez API – do niej wracamy po błędzie zapisu kolejności. */
  private confirmed: CompanyPhoto[] = [];
  private upload?: Subscription;

  constructor() {
    this.load$.pipe(
      tap(() => {
        this.loading.set(true);
        this.loadError.set(null);
      }),
      switchMap(() => this.media.getPhotos(this.company().id).pipe(
        catchError(error => {
          console.error('Error loading company photos:', error);
          this.loadError.set(getApiErrorMessage(error, 'Nie udało się wczytać zdjęć'));
          return of(null);
        })
      )),
      takeUntilDestroyed()
    ).subscribe(photos => {
      if (photos) this.setPhotos(photos);
      this.loading.set(false);
    });

    // switchMap: liczy się ostatnia kolejność (szybkie kolejne przesunięcia nie nadpisują się w złej kolejności)
    this.order$.pipe(
      switchMap(ids => this.media.reorderPhotos(ids).pipe(
        catchError(error => {
          console.error('Error reordering photos:', error);
          this.photos.set(this.confirmed);
          this.showError(error, 'Nie udało się zapisać kolejności zdjęć');
          return of(null);
        })
      )),
      filter((photos): photos is CompanyPhoto[] => photos !== null),
      takeUntilDestroyed()
    ).subscribe(photos => this.confirmed = photos);
  }

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.load$.next();
  }

  label(photo: CompanyPhoto, index: number): string {
    return photo.caption || `Zdjęcie ${index + 1}`;
  }

  openFilePicker(): void {
    this.fileInput()?.nativeElement.click();
  }

  onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = ''; // ten sam plik można wybrać ponownie
    if (!files.length || this.uploading()) return;

    const { accepted, errors } = selectPhotoFiles(files, this.photos().length);
    this.errors.set(errors);
    if (!accepted.length) return;

    this.uploadProgress.set(0);
    this.upload = this.media.uploadPhotos(accepted).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: event => {
        if (event.type === 'progress') {
          this.uploadProgress.set(event.percent);
          return;
        }
        this.uploadProgress.set(null);
        this.setPhotos(event.photos);
        const message = accepted.length === 1 ? 'Dodano 1 zdjęcie' : `Dodano zdjęcia: ${accepted.length}`;
        this.notify(message);
      },
      error: error => {
        console.error('Error uploading photos:', error);
        this.uploadProgress.set(null);
        this.showError(error, 'Nie udało się wysłać zdjęć');
      }
    });
  }

  cancelUpload(): void {
    this.upload?.unsubscribe();
    this.uploadProgress.set(null);
    this.announce('Wysyłanie zdjęć przerwane');
  }

  drop(event: CdkDragDrop<CompanyPhoto[]>): void {
    this.move(event.previousIndex, event.currentIndex);
  }

  /** Przesunięcie z klawiatury; fokus zostaje na przycisku przesuniętego zdjęcia. */
  moveBy(photo: CompanyPhoto, delta: -1 | 1): void {
    const from = this.photos().findIndex(p => p.id === photo.id);
    if (from < 0) return;
    this.move(from, from + delta);
    this.focusAfterRender(photo.id, delta < 0 ? 'move-left' : 'move-right');
  }

  editCaption(photo: CompanyPhoto, index: number): void {
    this.dialog.open<PhotoCaptionDialogComponent, PhotoCaptionDialogData, string | null>(PhotoCaptionDialogComponent, {
      data: { caption: photo.caption },
      width: '480px',
      maxWidth: '95vw',
      ariaModal: true
    }).afterClosed().pipe(
      filter((caption): caption is string | null => caption !== undefined && caption !== photo.caption),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(caption => {
      this.busyId.set(photo.id);
      this.media.updateCaption(photo.id, caption).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: updated => {
          this.busyId.set(null);
          const replace = (list: CompanyPhoto[]) => list.map(p => p.id === updated.id ? updated : p);
          this.photos.update(replace);
          this.confirmed = replace(this.confirmed);
          this.announce(updated.caption ? `Zapisano opis zdjęcia: ${updated.caption}` : `Usunięto opis zdjęcia ${index + 1}`);
        },
        error: error => {
          console.error('Error updating photo caption:', error);
          this.busyId.set(null);
          this.showError(error, 'Nie udało się zapisać opisu zdjęcia');
        }
      });
    });
  }

  remove(photo: CompanyPhoto, index: number): void {
    const label = this.label(photo, index);
    this.confirm.confirm({
      title: 'Usunąć zdjęcie?',
      message: `Zdjęcie „${label}” zniknie z profilu firmy. Tej operacji nie da się cofnąć.`,
      confirmLabel: 'Usuń',
      destructive: true
    }).pipe(filter(Boolean), takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.busyId.set(photo.id);
      this.media.deletePhoto(photo.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: () => {
          this.busyId.set(null);
          this.setPhotos(this.photos().filter(p => p.id !== photo.id));
          this.notify(`Usunięto zdjęcie: ${label}`);
          // fokus na zdjęcie, które zajęło miejsce usuniętego (albo na kafel dodawania)
          const next = this.photos()[Math.min(index, this.photos().length - 1)];
          this.focusAfterRender(next?.id ?? null, 'edit');
        },
        error: error => {
          console.error('Error deleting photo:', error);
          this.busyId.set(null);
          this.showError(error, 'Nie udało się usunąć zdjęcia');
        }
      });
    });
  }

  private move(from: number, to: number): void {
    const list = [...this.photos()];
    if (from === to || to < 0 || to >= list.length) return;
    moveItemInArray(list, from, to);
    this.photos.set(list);
    this.announce(`${this.label(list[to], from)} – pozycja ${to + 1} z ${list.length}`);
    this.order$.next(list.map(p => p.id));
  }

  private setPhotos(photos: CompanyPhoto[]): void {
    this.photos.set(photos);
    this.confirmed = photos;
  }

  private focusAfterRender(photoId: number | null, role: 'move-left' | 'move-right' | 'edit'): void {
    afterNextRender(() => {
      const root = this.host.nativeElement;
      const item = photoId === null ? null : root.querySelector<HTMLElement>(`[data-photo-id="${photoId}"]`);
      const target = item
        ? [role, 'move-left', 'move-right', 'edit']
          .map(name => item.querySelector<HTMLButtonElement>(`button.${name}`))
          .find(button => button && !button.disabled)
        : root.querySelector<HTMLButtonElement>('button.add-tile');
      target?.focus();
    }, { injector: this.injector });
  }

  private showError(error: unknown, fallback: string): void {
    if (!isHandledByInterceptor(error)) {
      this.errors.set([getApiErrorMessage(error, fallback)]);
    }
  }

  private announce(message: string): void {
    this.errors.set([]);
    this.announcement.set(message);
  }

  /** Sukces w snackbarze (ogłaszany przez LiveAnnouncer) – bez dublowania w regionie statusu. */
  private notify(message: string): void {
    this.errors.set([]);
    this.snackBar.open(message, 'OK', { duration: 5000, politeness: 'polite' });
  }
}
