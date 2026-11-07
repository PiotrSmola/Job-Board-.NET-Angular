import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BackgroundFocus, CompanyPhoto, MAX_COMPANY_PHOTOS } from '../models/company-media.model';
import { UploadProgressEvent, mapUploadEvents } from '../utils/http-transfer';
import { CompanyMediaService } from './company-media.service';

/** Ustawienia tła ofert z API; `url` = null tylko po PATCH, gdy firma nie ma jeszcze obrazu. */
export interface OfferBackgroundSettings {
  url: string | null;
  dim: number;
  focus: BackgroundFocus;
}

export type PhotoUploadEvent = UploadProgressEvent | { type: 'done'; photos: CompanyPhoto[] };
export type BackgroundUploadEvent = UploadProgressEvent | { type: 'done'; background: OfferBackgroundSettings };

// Ograniczenia zgodne z API (typ i tak sprawdza serwer po zawartości pliku)
export const IMAGE_MAX_FILE_SIZE = 5 * 1024 * 1024;
/** Limit jednego żądania (RequestSizeLimit 26 MB z zapasem na nagłówki multipart). */
export const IMAGE_MAX_BATCH_SIZE = 25 * 1024 * 1024;
export const IMAGE_ALLOWED_EXTENSIONS: readonly string[] = ['.png', '.jpg', '.jpeg'];
export const IMAGE_ACCEPT = 'image/png,image/jpeg,.png,.jpg,.jpeg';
export const PHOTO_CAPTION_MAX = 150;
export const DEFAULT_BACKGROUND_DIM = 75;

/** Komunikat błędu pojedynczego obrazu po stronie klienta albo null, gdy plik jest poprawny. */
export function validateImageFile(file: Pick<File, 'name' | 'size' | 'type'>): string | null {
  const name = file.name.toLowerCase();
  const typeOk = !file.type || file.type === 'image/png' || file.type === 'image/jpeg';
  if (!typeOk || !IMAGE_ALLOWED_EXTENSIONS.some(ext => name.endsWith(ext))) {
    return 'Nieobsługiwany typ pliku – wybierz obraz PNG lub JPEG';
  }
  if (file.size === 0) return 'Wybrany plik jest pusty';
  if (file.size > IMAGE_MAX_FILE_SIZE) return 'Plik jest za duży – maksymalny rozmiar to 5 MB';
  return null;
}

/**
 * Dobiera wybrane zdjęcia do galerii: typ i rozmiar pliku, limit 20 zdjęć firmy i 25 MB na jedno wysłanie.
 * Przyjmuje pliki po kolei, dopóki się mieszczą; dla pozostałych zwraca powód z nazwą pliku.
 */
export function selectPhotoFiles(files: readonly File[], currentCount: number): { accepted: File[]; errors: string[] } {
  const accepted: File[] = [];
  const errors: string[] = [];
  let batchSize = 0;

  for (const file of files) {
    const error = validateImageFile(file)
      ?? (currentCount + accepted.length >= MAX_COMPANY_PHOTOS ? `Firma może mieć maksymalnie ${MAX_COMPANY_PHOTOS} zdjęć` : null)
      ?? (batchSize + file.size > IMAGE_MAX_BATCH_SIZE ? 'Jednorazowo można wysłać do 25 MB – dodaj ten plik osobno' : null);

    if (error) {
      errors.push(`${file.name}: ${error}`);
      continue;
    }
    accepted.push(file);
    batchSize += file.size;
  }

  return { accepted, errors };
}

/**
 * Galeria i tło ofert firmy zalogowanego pracodawcy (`/api/employer/company/...`) oraz odczyt galerii
 * i obrazów. Obrazy firmy niezatwierdzonej są dostępne tylko z JWT, dlatego panel pobiera je jako Blob.
 */
@Injectable({ providedIn: 'root' })
export class EmployerMediaService {
  private http = inject(HttpClient);
  private companyMedia = inject(CompanyMediaService);
  private readonly MEDIA_URL = '/api/employer/company';

  /**
   * Galeria firmy w kolejności wyświetlania – ten sam odczyt co na profilu publicznym (`CompanyMediaService`);
   * członkowie i admin widzą też firmę niezatwierdzoną, obrazy panel i tak wczytuje przez `appBlobSrc`.
   */
  getPhotos(companyId: number): Observable<CompanyPhoto[]> {
    return this.companyMedia.getPhotos(companyId);
  }

  /** Obraz jako Blob (z nagłówkiem Authorization) – do `URL.createObjectURL`. */
  loadImage(url: string): Observable<Blob> {
    return this.http.get(url, { responseType: 'blob' });
  }

  /**
   * Wysyła zdjęcia jednym żądaniem multipart (pole `files`; API zapisuje wszystkie albo żadne).
   * Emituje postęp, a na końcu całą galerię. Rezygnacja z subskrypcji przerywa wysyłanie.
   */
  uploadPhotos(files: readonly File[]): Observable<PhotoUploadEvent> {
    const formData = new FormData();
    for (const file of files) {
      formData.append('files', file, file.name);
    }

    return this.http.post<CompanyPhoto[]>(`${this.MEDIA_URL}/photos`, formData, {
      observe: 'events',
      reportProgress: true
    }).pipe(mapUploadEvents((photos: CompanyPhoto[]): PhotoUploadEvent => ({ type: 'done', photos })));
  }

  /** Podpis zdjęcia (≤ 150 znaków); pusty = brak podpisu. */
  updateCaption(photoId: number, caption: string | null): Observable<CompanyPhoto> {
    return this.http.patch<CompanyPhoto>(`${this.MEDIA_URL}/photos/${photoId}`, { caption });
  }

  /** Nowa kolejność – lista musi zawierać dokładnie wszystkie zdjęcia firmy. */
  reorderPhotos(photoIds: readonly number[]): Observable<CompanyPhoto[]> {
    return this.http.put<CompanyPhoto[]>(`${this.MEDIA_URL}/photos/order`, { photoIds });
  }

  deletePhoto(photoId: number): Observable<void> {
    return this.http.delete<void>(`${this.MEDIA_URL}/photos/${photoId}`);
  }

  /** Nowy obraz tła (pole `file`); przyciemnienie i punkt ostrości zostają bez zmian. */
  uploadBackground(file: File): Observable<BackgroundUploadEvent> {
    const formData = new FormData();
    formData.append('file', file, file.name);

    return this.http.put<OfferBackgroundSettings>(`${this.MEDIA_URL}/offer-background`, formData, {
      observe: 'events',
      reportProgress: true
    }).pipe(mapUploadEvents((background: OfferBackgroundSettings): BackgroundUploadEvent => ({ type: 'done', background })));
  }

  /** Przyciemnienie (55–95) i punkt ostrości; działa też przed wysłaniem obrazu. */
  updateBackground(settings: { dim?: number; focus?: BackgroundFocus }): Observable<OfferBackgroundSettings> {
    return this.http.patch<OfferBackgroundSettings>(`${this.MEDIA_URL}/offer-background`, settings);
  }

  /** Usuwa obraz tła (ustawienia zostają). */
  deleteBackground(): Observable<void> {
    return this.http.delete<void>(`${this.MEDIA_URL}/offer-background`);
  }
}
