import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CompanyPhoto } from '../models/company-media.model';

/** Publiczny odczyt galerii firmy (zarządzanie zdjęciami jest w panelu pracodawcy). */
@Injectable({
  providedIn: 'root'
})
export class CompanyMediaService {
  private http = inject(HttpClient);

  /** Zdjęcia firmy w kolejności ustalonej przez pracodawcę; `url` jest względny (/api/...). */
  getPhotos(companyId: number): Observable<CompanyPhoto[]> {
    return this.http.get<CompanyPhoto[]>(`/api/companies/${companyId}/photos`);
  }
}
