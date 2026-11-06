import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Category } from '../models/taxonomy.model';

@Injectable({
  providedIn: 'root'
})
export class CategoryService {
  private http = inject(HttpClient);

  /** Publiczna lista kategorii (sortOrder, nazwa) z liczbą aktywnych ofert. */
  getCategories(): Observable<Category[]> {
    return this.http.get<Category[]>('/api/categories');
  }
}
