import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SkillSuggestion } from '../models/taxonomy.model';

export const SKILL_SUGGESTIONS_MAX = 20;

@Injectable({
  providedIn: 'root'
})
export class SkillService {
  private http = inject(HttpClient);

  /** Podpowiedzi umiejętności (prefiks lub fragment), od najpopularniejszych; limit API to 20. */
  suggest(search: string, limit = 10): Observable<SkillSuggestion[]> {
    let params = new HttpParams().set('limit', Math.min(Math.max(limit, 1), SKILL_SUGGESTIONS_MAX).toString());
    if (search) {
      params = params.set('search', search);
    }
    return this.http.get<SkillSuggestion[]>('/api/skills', { params });
  }
}
