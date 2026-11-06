import { DestroyRef, Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { AsyncSubject, EMPTY, Observable, Subscription, map, tap } from 'rxjs';
import { SavedJob } from '../models/account.model';
import { PaginatedResult } from '../models/pagination.model';
import { toPaginatedResult } from '../utils/pagination';
import { AuthService } from './auth.service';

export type SavedJobsLoadState = 'idle' | 'loading' | 'loaded' | 'error';

/**
 * Zapisane oferty kandydata. Zbiór zapisanych id (`GET /api/saved-jobs/ids`) ładowany jest raz po zalogowaniu
 * (przy pierwszym użyciu serwisu) i trzymany w sygnale – serduszka na liście nie robią N+1 zapytań.
 * Zmiana użytkownika lub wylogowanie czyści stan; odnowienie tokena (np. po zmianie hasła) go nie przeładowuje.
 */
@Injectable({
  providedIn: 'root'
})
export class SavedJobsService {
  private http = inject(HttpClient);
  private authService = inject(AuthService);
  private readonly API_URL = '/api/saved-jobs';

  private readonly ids = signal<ReadonlySet<number>>(new Set());
  /** Przełączenia w toku: id oferty → stan docelowy (optymistyczny). */
  private readonly pending = signal<ReadonlyMap<number, boolean>>(new Map());
  private readonly state = signal<SavedJobsLoadState>('idle');
  /** Numer „sesji” serwisu – odpowiedzi z poprzedniej sesji nie mogą nadpisać stanu nowej. */
  private generation = 0;
  private loadSubscription: Subscription | null = null;

  readonly savedIds = this.ids.asReadonly();
  readonly loadState = this.state.asReadonly();

  /** Zapisywanie ofert jest dostępne tylko dla zalogowanego kandydata (pracodawca/admin dostają 403). */
  readonly enabled = computed(() =>
    this.authService.currentUser() !== null && !this.authService.isAdmin() && !this.authService.isEmployer());

  // Klucz sesji kandydata – nowy token tego samego użytkownika nie zmienia klucza
  private readonly sessionKey = computed(() => this.enabled() ? this.authService.currentUser()!.email.toLowerCase() : null);

  constructor() {
    effect(() => {
      const key = this.sessionKey();
      untracked(() => this.reset(key));
    });
    inject(DestroyRef).onDestroy(() => this.loadSubscription?.unsubscribe());
  }

  isSaved(jobId: number): boolean {
    return this.ids().has(jobId);
  }

  isPending(jobId: number): boolean {
    return this.pending().has(jobId);
  }

  /** Ponawia wczytanie zbioru id po wcześniejszym błędzie (np. chwilowy brak sieci). */
  ensureLoaded(): void {
    if (this.enabled() && this.state() === 'error') {
      this.loadIds();
    }
  }

  /**
   * Przełącza zapis oferty z optymistyczną aktualizacją i cofnięciem przy błędzie.
   * Żądanie należy do serwisu (nie przerywa go zniszczenie komponentu – PUT/DELETE są idempotentne, a stan
   * serduszek musi zostać spójny po przejściu np. z listy do szczegółów). Zwrócony strumień emituje nowy stan
   * (true = zapisana) albo błąd HTTP; rezygnacja z subskrypcji tylko przestaje nasłuchiwać.
   */
  toggle(jobId: number): Observable<boolean> {
    if (!this.enabled() || this.isPending(jobId)) return EMPTY;

    const generation = this.generation;
    const wasSaved = this.isSaved(jobId);
    const target = !wasSaved;
    this.setSaved(jobId, target);
    this.setPending(jobId, target);

    const result = new AsyncSubject<boolean>();
    const url = `${this.API_URL}/${jobId}`;
    const request = target ? this.http.put<void>(url, null) : this.http.delete<void>(url);

    request.subscribe({
      complete: () => {
        if (generation === this.generation) this.clearPending(jobId);
        result.next(target);
        result.complete();
      },
      error: (error: unknown) => {
        if (generation === this.generation) {
          this.setSaved(jobId, wasSaved);
          this.clearPending(jobId);
        }
        result.error(error);
      }
    });

    return result.asObservable();
  }

  /**
   * Strona zapisanych ofert (od najnowszych zapisów), także wygasłe i zamknięte. Oferty z odpowiedzi są zapisane
   * po stronie serwera, więc trafiają też do zbioru id (serduszka są pełne, nawet zanim wczyta się `/ids`).
   */
  getSavedJobs(pageNumber: number, pageSize: number): Observable<PaginatedResult<SavedJob>> {
    const params = new HttpParams()
      .append('pageNumber', pageNumber.toString())
      .append('pageSize', pageSize.toString());
    const generation = this.generation;

    return this.http.get<SavedJob[]>(this.API_URL, { params, observe: 'response' }).pipe(
      map(response => toPaginatedResult(response, pageSize)),
      tap(page => {
        if (generation !== this.generation) return;
        const pending = this.pending();
        const fresh = page.result.map(item => item.job.id).filter(id => !pending.has(id) && !this.ids().has(id));
        if (fresh.length) {
          this.ids.update(ids => new Set([...ids, ...fresh]));
        }
      })
    );
  }

  private reset(key: string | null): void {
    this.generation++;
    this.loadSubscription?.unsubscribe();
    this.loadSubscription = null;
    this.ids.set(new Set());
    this.pending.set(new Map());
    this.state.set('idle');

    if (key !== null) {
      this.loadIds();
    }
  }

  private loadIds(): void {
    const generation = this.generation;
    this.loadSubscription?.unsubscribe();
    this.state.set('loading');

    this.loadSubscription = this.http.get<number[]>(`${this.API_URL}/ids`).subscribe({
      next: ids => {
        if (generation !== this.generation) return;
        const next = new Set(Array.isArray(ids) ? ids : []);
        // przełączenia kliknięte w trakcie ładowania mają pierwszeństwo przed odpowiedzią
        for (const [jobId, saved] of this.pending()) {
          if (saved) next.add(jobId); else next.delete(jobId);
        }
        this.ids.set(next);
        this.state.set('loaded');
      },
      error: error => {
        if (generation !== this.generation) return;
        console.error('Error loading saved job ids:', error);
        this.state.set('error');
      }
    });
  }

  private setSaved(jobId: number, saved: boolean): void {
    this.ids.update(ids => {
      const next = new Set(ids);
      if (saved) next.add(jobId); else next.delete(jobId);
      return next;
    });
  }

  private setPending(jobId: number, target: boolean): void {
    this.pending.update(pending => new Map(pending).set(jobId, target));
  }

  private clearPending(jobId: number): void {
    this.pending.update(pending => {
      const next = new Map(pending);
      next.delete(jobId);
      return next;
    });
  }
}
