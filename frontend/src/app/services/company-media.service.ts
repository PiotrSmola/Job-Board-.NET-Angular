import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CompanyPhoto } from '../models/company-media.model';

/** Odczyt galerii firmy – profil publiczny, a przez `EmployerMediaService` także panele (zarządzanie jest tam). */
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
