import { JobOffer } from './job-offer.model';

export enum ApplicationStatus {
  Submitted = 0,
  InReview = 1,
  Interview = 2,
  Offered = 3,
  Hired = 4,
  Rejected = 5,
  Withdrawn = 6
}

export interface ApplicationStatusHistoryEntry {
  status: ApplicationStatus;
  changedAt: string;
  /** Komentarz rekrutera – widoczny także dla kandydata. */
  comment: string | null;
  /** Autor zmiany – API wypełnia tylko dla admina i pracodawcy firmy oferty. */
  changedBy: string | null;
}

export interface JobApplication {
  id: number;
  jobOfferId: number;
  userId: string;
  userEmail: string;
  userFullName: string;
  jobTitle: string;
  /** Nazwa firmy oferty. */
  company: string;
  companyId: number;
  note: string | null;
  appliedAt: Date;
  status: ApplicationStatus;
  statusChangedAt: string;
  jobOffer?: JobOffer;
  /** Tylko w szczegółach aplikacji (GET /{id}) i odpowiedziach na zmianę statusu. */
  history?: ApplicationStatusHistoryEntry[] | null;
  /** Załączniki aplikacji (API sortuje je po rodzaju i dacie dodania); pusta lista, gdy brak. */
  attachments: ApplicationAttachment[];
  /** Łączny rozmiar załączników w bajtach (limit 25 MB na aplikację). */
  attachmentsTotalSize: number;
}

/** Rodzaj załącznika – wartości zgodne z enumem API. */
export enum AttachmentKind {
  Cv = 0,
  CoverLetter = 1,
  Portfolio = 2,
  Other = 3
}

/** Metadane załącznika (klucz pliku w magazynie nigdy nie trafia do klienta). */
export interface ApplicationAttachment {
  id: number;
  kind: AttachmentKind;
  /** Oryginalna nazwa pliku (po sanityzacji przez API). */
  fileName: string;
  /** Rozmiar w bajtach. */
  size: number;
  uploadedAt: string;
}

/** Plik wybrany do wysłania razem z jego rodzajem. */
export interface AttachmentUploadItem {
  file: File;
  kind: AttachmentKind;
}

/** Zdarzenie przesyłania załączników: postęp w procentach albo zakończenie z zaktualizowaną aplikacją. */
export type AttachmentUploadEvent =
  | { type: 'progress'; percent: number }
  | { type: 'done'; application: JobApplication };

export interface CreateJobApplication {
  jobOfferId: number;
  note: string | null;
}

export interface UpdateApplicationStatus {
  status: ApplicationStatus;
  comment: string | null;
}

/** Odpowiedź GET /api/jobapplications/check/{jobId}. */
export interface ApplicationCheck {
  /** true, gdy istnieje aplikacja w statusie innym niż wycofana. */
  hasApplied: boolean;
  applicationId: number | null;
  status: ApplicationStatus | null;
}
