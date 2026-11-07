import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subscription, filter } from 'rxjs';
import { BackgroundFocus, MAX_BACKGROUND_DIM, MIN_BACKGROUND_DIM, OfferBackground } from '../../../models/company-media.model';
import { Company } from '../../../models/company.model';
import { ConfirmService } from '../../../services/confirm.service';
import {
  DEFAULT_BACKGROUND_DIM, EmployerMediaService, IMAGE_ACCEPT, OfferBackgroundSettings, validateImageFile
} from '../../../services/employer-media.service';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { BlobImageDirective } from './blob-image.directive';

export const FOCUS_OPTIONS: readonly { value: BackgroundFocus; label: string; position: string }[] = [
  { value: BackgroundFocus.Center, label: 'Środek', position: 'center center' },
  { value: BackgroundFocus.Top, label: 'Góra', position: 'center top' },
  { value: BackgroundFocus.Bottom, label: 'Dół', position: 'center bottom' }
];

/**
 * Maska nagłówka oferty dla przyciemnienia `dim` (55–95 %): kolor tła strony o kryciu dim → dim + 12 → pełny.
 * Ten sam przebieg co na stronie oferty (makieta: 70 % → 82 % → #121417).
 */
export function offerHeroMask(dim: number): string {
  const top = Math.min(100, Math.max(0, dim));
  const middle = Math.min(100, top + 12);
  return `linear-gradient(180deg, color-mix(in srgb, var(--color-bg) ${top}%, transparent) 0%, ` +
    `color-mix(in srgb, var(--color-bg) ${middle}%, transparent) 60%, var(--color-bg) 100%)`;
}

/**
 * Tło nagłówka ofert: podgląd na żywo (obraz, maska wg suwaka, punkt ostrości), zmiana i usunięcie obrazu
 * (od razu na serwerze, z postępem wysyłania) oraz zapis przyciemnienia i punktu ostrości.
 */
@Component({
  selector: 'app-offer-background-settings',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatFormFieldModule, MatProgressBarModule, MatSelectModule, BlobImageDirective],
  templateUrl: './offer-background-settings.component.html',
  styleUrl: './offer-background-settings.component.scss'
})
export class OfferBackgroundSettingsComponent implements OnInit {
  private media = inject(EmployerMediaService);
  private confirm = inject(ConfirmService);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  readonly company = input.required<Company>();
  /** Aktualne tło po każdej zmianie (null = brak obrazu) – rodzic aktualizuje dane firmy. */
  readonly backgroundChange = output<OfferBackground | null>();

  readonly minDim = MIN_BACKGROUND_DIM;
  readonly maxDim = MAX_BACKGROUND_DIM;
  readonly accept = IMAGE_ACCEPT;
  readonly focusOptions = FOCUS_OPTIONS;

  readonly url = signal<string | null>(null);
  readonly dim = signal(DEFAULT_BACKGROUND_DIM);
  readonly focus = signal(BackgroundFocus.Center);
  private readonly savedDim = signal(DEFAULT_BACKGROUND_DIM);
  private readonly savedFocus = signal(BackgroundFocus.Center);

  readonly uploadProgress = signal<number | null>(null);
  readonly saving = signal(false);
  readonly removing = signal(false);
  readonly error = signal<string | null>(null);
  readonly announcement = signal('');

  readonly dirty = computed(() => this.dim() !== this.savedDim() || this.focus() !== this.savedFocus());
  readonly busy = computed(() => this.uploadProgress() !== null || this.saving() || this.removing());
  readonly mask = computed(() => offerHeroMask(this.dim()));
  readonly objectPosition = computed(() => FOCUS_OPTIONS.find(o => o.value === this.focus())?.position ?? 'center center');
  readonly previewLabel = computed(() => this.url()
    ? `Podgląd nagłówka oferty z tłem firmy, przyciemnienie ${this.dim()}%, punkt ostrości: ${this.focusLabel()}`
    : 'Podgląd nagłówka oferty bez obrazu tła');
  readonly progressText = computed(() => {
    const percent = this.uploadProgress() ?? 0;
    return percent >= 100 ? 'Przetwarzanie obrazu…' : `Wysyłanie obrazu: ${percent}%`;
  });

  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');
  private upload?: Subscription;

  ngOnInit(): void {
    const background = this.company().offerBackground;
    if (background) {
      this.url.set(background.url);
      this.applySaved(background.dim, background.focus);
    }
  }

  focusLabel(): string {
    return FOCUS_OPTIONS.find(o => o.value === this.focus())?.label ?? '';
  }

  onDimInput(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.dim.set(Math.min(MAX_BACKGROUND_DIM, Math.max(MIN_BACKGROUND_DIM, value)));
  }

  openFilePicker(): void {
    this.fileInput()?.nativeElement.click();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || this.busy()) return;

    const invalid = validateImageFile(file);
    if (invalid) {
      this.error.set(`${file.name}: ${invalid}`);
      return;
    }

    this.error.set(null);
    this.uploadProgress.set(0);
    this.upload = this.media.uploadBackground(file).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: event => {
        if (event.type === 'progress') {
          this.uploadProgress.set(event.percent);
          return;
        }
        this.uploadProgress.set(null);
        this.url.set(event.background.url);
        this.emit(event.background);
        this.done('Obraz tła został zapisany');
      },
      error: error => {
        console.error('Error uploading offer background:', error);
        this.uploadProgress.set(null);
        this.showError(error, 'Nie udało się wysłać obrazu tła');
      }
    });
  }

  cancelUpload(): void {
    this.upload?.unsubscribe();
    this.uploadProgress.set(null);
    this.announcement.set('Wysyłanie obrazu przerwane');
  }

  removeImage(): void {
    this.confirm.confirm({
      title: 'Usunąć tło ofert?',
      message: 'Nagłówki ofert firmy będą miały jednolite tło. Ustawienia przyciemnienia zostaną zachowane.',
      confirmLabel: 'Usuń tło',
      destructive: true
    }).pipe(filter(Boolean), takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.removing.set(true);
      this.error.set(null);
      this.media.deleteBackground().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: () => {
          this.removing.set(false);
          this.url.set(null);
          this.backgroundChange.emit(null);
          this.done('Tło ofert zostało usunięte');
        },
        error: error => {
          console.error('Error deleting offer background:', error);
          this.removing.set(false);
          this.showError(error, 'Nie udało się usunąć tła');
        }
      });
    });
  }

  save(): void {
    if (!this.dirty() || this.busy()) return;

    this.saving.set(true);
    this.error.set(null);
    this.media.updateBackground({ dim: this.dim(), focus: this.focus() }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: settings => {
        this.saving.set(false);
        this.applySaved(settings.dim, settings.focus);
        if (settings.url) this.url.set(settings.url);
        this.emit(settings);
        this.done('Ustawienia tła zostały zapisane');
      },
      error: error => {
        console.error('Error saving offer background:', error);
        this.saving.set(false);
        this.showError(error, 'Nie udało się zapisać ustawień tła');
      }
    });
  }

  private applySaved(dim: number, focus: BackgroundFocus): void {
    this.savedDim.set(dim);
    this.savedFocus.set(focus);
    this.dim.set(dim);
    this.focus.set(focus);
  }

  private emit(settings: OfferBackgroundSettings): void {
    this.backgroundChange.emit(settings.url ? { url: settings.url, dim: settings.dim, focus: settings.focus } : null);
  }

  /** Sukces w snackbarze (ogłaszany przez LiveAnnouncer) – bez dublowania w regionie statusu. */
  private done(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000, politeness: 'polite' });
  }

  private showError(error: unknown, fallback: string): void {
    if (!isHandledByInterceptor(error)) {
      this.error.set(getApiErrorMessage(error, fallback));
    }
  }
}
