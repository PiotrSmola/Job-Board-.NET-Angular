import { AttachmentKind } from './job-application.model';
import { JobOffer } from './job-offer.model';

/** Odpowiedź GET/PUT /api/account/profile. */
export interface Profile {
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
  headline: string | null;
  location: string | null;
  linkedInUrl: string | null;
  about: string | null;
  emailConfirmed: boolean;
  roles: string[];
  createdAt: string;
}

/** Body PUT /api/account/profile (e-maila nie zmienia się tym endpointem); puste pola opcjonalne jako null. */
export interface UpdateProfile {
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
  headline: string | null;
  location: string | null;
  linkedInUrl: string | null;
  about: string | null;
}

export interface ChangePassword {
  currentPassword: string;
  newPassword: string;
}

/** Dokument profilu kandydata (np. domyślne CV) – metadane jak przy załącznikach aplikacji. */
export interface ProfileDocument {
  id: number;
  kind: AttachmentKind;
  fileName: string;
  size: number;
  uploadedAt: string;
}

/** Zdarzenie przesyłania dokumentów profilu: postęp w procentach albo zakończenie z listą dokumentów. */
export type ProfileDocumentUploadEvent =
  | { type: 'progress'; percent: number }
  | { type: 'done'; documents: ProfileDocument[] };

/** Preferencje powiadomień e-mail (e-maile bezpieczeństwa wysyłane są zawsze). */
export interface NotificationPreferences {
  /** Kandydat: potwierdzenie złożenia aplikacji i zmiany jej statusu. */
  applicationStatusChanged: boolean;
  /** Pracodawca: nowa aplikacja do oferty firmy. */
  newApplication: boolean;
  /** Pracodawca: decyzja administratora o firmie. */
  companyReview: boolean;
}

/** Pozycja GET /api/saved-jobs – także oferty, które w międzyczasie wygasły lub zamknięto. */
export interface SavedJob {
  savedAt: string;
  job: JobOffer;
}
