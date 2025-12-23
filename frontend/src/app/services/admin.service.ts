import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class AdminService {
  private http = inject(HttpClient);
  private readonly API_URL = 'http://localhost:5285/api/admin';

  refreshSeed(): Observable<{ message: string; timestamp: string }> {
    return this.http.post<{ message: string; timestamp: string }>(`${this.API_URL}/refresh-seed`, {});
  }
}
