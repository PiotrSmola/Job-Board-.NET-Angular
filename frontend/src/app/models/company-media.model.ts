/** Punkt ostrości tła ofert (enum po stronie API). */
export enum BackgroundFocus {
  Center = 0,
  Top = 1,
  Bottom = 2
}

/** Zdjęcie z galerii firmy (GET /api/companies/{id}/photos). */
export interface CompanyPhoto {
  id: number;
  caption: string | null;
  width: number;
  height: number;
  /** Względny adres pliku: /api/companies/{id}/photos/{photoId}/file */
  url: string;
}

/** Tło nagłówka ofert firmy; dim = przyciemnienie w procentach (55–95). */
export interface OfferBackground {
  url: string;
  dim: number;
  focus: BackgroundFocus;
}

export const MIN_BACKGROUND_DIM = 55;
export const MAX_BACKGROUND_DIM = 95;
export const MAX_COMPANY_PHOTOS = 20;
