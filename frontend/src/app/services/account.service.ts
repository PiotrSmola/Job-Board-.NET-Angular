import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import {
  ChangePassword,
  NotificationPreferences,
  Profile,
  ProfileDocument,
  ProfileDocumentUploadEvent,
  UpdateProfile
} from '../models/account.model';
import { AttachmentUploadItem } from '../models/job-application.model';
import { UserDto } from '../models/user.model';
import { AuthService } from './auth.service';
import { downloadFile, mapUploadEvents } from '../utils/http-transfer';

/** Konto zalogowanego użytkownika: profil, hasło, usunięcie konta, dokumenty profilu i powiadomienia. */
@Injectable({
  providedIn: 'root'
})
export class AccountService {
  private http = inject(HttpClient);
  private authService = inject(AuthService);
  private readonly API_URL = '/api/account';

  getProfile(): Observable<Profile> {
    return this.http.get<Profile>(`${this.API_URL}/profile`);
  }

  /** Zapis profilu; imię i nazwisko trafiają też do sesji (menu użytkownika). */
  updateProfile(profile: UpdateProfile): Observable<Profile> {
    return this.http.put<Profile>(`${this.API_URL}/profile`, profile).pipe(
      tap(saved => this.authService.updateCurrentUser({
        firstName: saved.firstName,
        lastName: saved.lastName,
        emailConfirmed: saved.emailConfirmed
      }))
    );
  }

  /** Zmiana hasła – API unieważnia stare tokeny i zwraca nowy, który od razu zapisujemy (sesja trwa). */
  changePassword(body: ChangePassword): Observable<UserDto> {
    return this.http.post<UserDto>(`${this.API_URL}/change-password`, body).pipe(
      tap(user => this.authService.renewSession(user))
    );
  }

  /** Trwałe usunięcie konta (potwierdzone hasłem); wylogowanie robi wywołujący. */
  deleteAccount(password: string): Observable<void> {
    return this.http.delete<void>(this.API_URL, { body: { password } });
  }

  // --- dokumenty profilu (tylko kandydat) ---

  getDocuments(): Observable<ProfileDocument[]> {
    return this.http.get<ProfileDocument[]>(`${this.API_URL}/documents`);
  }

  /**
   * Przesłanie dokumentów jednym żądaniem multipart (`files` + równoległe `kinds`, jak załączniki aplikacji).
   * Emituje postęp, a na końcu listę dokumentów z odpowiedzi. Rezygnacja z subskrypcji przerywa żądanie.
   */
  uploadDocuments(items: readonly AttachmentUploadItem[]): Observable<ProfileDocumentUploadEvent> {
    const formData = new FormData();
    for (const { file, kind } of items) {
      formData.append('files', file, file.name);
      formData.append('kinds', String(kind));
    }

    return this.http.post<ProfileDocument[]>(`${this.API_URL}/documents`, formData, {
      observe: 'events',
      reportProgress: true
    }).pipe(
      mapUploadEvents((documents: ProfileDocument[]): ProfileDocumentUploadEvent => ({ type: 'done', documents }))
    );
  }

  deleteDocument(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/documents/${id}`);
  }

  /** Pobiera dokument i zapisuje go na dysku (nazwa z Content-Disposition, potem `fileName`). */
  downloadDocument(document: Pick<ProfileDocument, 'id' | 'fileName'>): Observable<void> {
    return downloadFile(this.http, `${this.API_URL}/documents/${document.id}`, document.fileName);
  }

  // --- powiadomienia e-mail ---

  getNotificationPreferences(): Observable<NotificationPreferences> {
    return this.http.get<NotificationPreferences>(`${this.API_URL}/notifications`);
  }

  updateNotificationPreferences(preferences: NotificationPreferences): Observable<NotificationPreferences> {
    return this.http.put<NotificationPreferences>(`${this.API_URL}/notifications`, preferences);
  }
}
