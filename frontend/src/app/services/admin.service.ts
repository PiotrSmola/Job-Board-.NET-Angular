import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AdminSettings } from '../models/admin-settings.model';
import { SKIP_FORBIDDEN_NOTICE } from '../interceptors/auth.interceptor';

@Injectable({
  providedIn: 'root'
})
export class AdminService {
  private http = inject(HttpClient);
  private readonly API_URL = '/api/admin';

  getSettings(): Observable<AdminSettings> {
    return this.http.get<AdminSettings>(`${this.API_URL}/settings`);
  }

  refreshSeed(): Observable<{ message: string; timestamp: string }> {
    // 403 (odświeżanie wyłączone) obsługuje komponent własnym komunikatem
    return this.http.post<{ message: string; timestamp: string }>(`${this.API_URL}/refresh-seed`, {}, {
      context: new HttpContext().set(SKIP_FORBIDDEN_NOTICE, true)
    });
  }
}
