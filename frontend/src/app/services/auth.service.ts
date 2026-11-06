import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { ActivatedRouteSnapshot, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import {
  ConfirmEmailDto, LoginDto, RegisterDto, RegisterEmployerDto, ResetPasswordDto, UserDto
} from '../models/user.model';
import { getJwtExpiration, getJwtRoles, getJwtUserId, isJwtExpired } from '../utils/jwt';

// setTimeout przyjmuje maksymalnie ~24,8 dnia
const MAX_TIMER_DELAY = 2_147_483_647;

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);

  private readonly API_URL = '/api';
  private readonly TOKEN_KEY = 'jobboard_token';
  private readonly USER_KEY = 'jobboard_user';

  private expiryTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly session = signal<UserDto | null>(this.restoreSession());

  /** Zalogowany użytkownik – tylko z ważnym tokenem (sesja czyszczona przy starcie i w chwili wygaśnięcia). */
  readonly currentUser = this.session.asReadonly();

  /** Zalogowany użytkownik z niepotwierdzonym e-mailem – baner „Potwierdź adres e-mail” (nie blokuje aplikacji). */
  readonly emailConfirmationPending = computed(() => this.session()?.emailConfirmed === false);

  constructor() {
    this.scheduleExpiry(this.session()?.token ?? null);
    inject(DestroyRef).onDestroy(() => this.clearExpiryTimer());
  }

  login(loginDto: LoginDto): Observable<UserDto> {
    return this.http.post<UserDto>(`${this.API_URL}/auth/login`, loginDto).pipe(
      tap(user => this.setCurrentUser(user))
    );
  }

  register(registerDto: RegisterDto): Observable<UserDto> {
    return this.http.post<UserDto>(`${this.API_URL}/auth/register`, registerDto).pipe(
      tap(user => this.setCurrentUser(user))
    );
  }

  /** Rejestracja pracodawcy razem z firmą (token z rolą Employer). */
  registerEmployer(registerDto: RegisterEmployerDto): Observable<UserDto> {
    return this.http.post<UserDto>(`${this.API_URL}/auth/register-employer`, registerDto).pipe(
      tap(user => this.setCurrentUser(user))
    );
  }

  logout(): void {
    this.clearSession();
    this.router.navigate(['/login']);
  }

  /** Zawsze 202 – API nie ujawnia, czy konto istnieje. */
  forgotPassword(email: string): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/auth/forgot-password`, { email });
  }

  /**
   * Ustawienie nowego hasła z linku z e-maila. Reset unieważnia stare tokeny – jeśli w tej przeglądarce
   * jest zalogowany ten sam użytkownik, jego sesja jest już nieważna, więc ją czyścimy.
   */
  resetPassword(dto: ResetPasswordDto): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/auth/reset-password`, dto).pipe(
      tap(() => {
        const user = this.session();
        if (user && user.email.toLowerCase() === dto.email.trim().toLowerCase()) {
          this.clearSession();
        }
      })
    );
  }

  /** Potwierdzenie adresu e-mail z linku; zalogowany ten sam użytkownik od razu traci baner. */
  confirmEmail(dto: ConfirmEmailDto): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/auth/confirm-email`, dto).pipe(
      tap(() => {
        if (getJwtUserId(this.session()?.token) === dto.userId) {
          this.updateCurrentUser({ emailConfirmed: true });
        }
      })
    );
  }

  /** Ponowne wysłanie linku potwierdzającego (zalogowany; limit żądań po stronie API). */
  resendConfirmation(): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/auth/resend-confirmation`, null);
  }

  /**
   * Nowy token po zmianie hasła (API unieważnia poprzednie) – sesja trwa dalej.
   * Gdy odpowiedź nie zawiera `emailConfirmed`, zostaje dotychczasowa wartość.
   */
  renewSession(user: UserDto): void {
    const current = this.session();
    this.setCurrentUser({ ...user, emailConfirmed: user.emailConfirmed ?? current?.emailConfirmed });
  }

  /** Aktualizacja danych zalogowanego użytkownika (np. imię po edycji profilu) bez zmiany tokena. */
  updateCurrentUser(patch: Partial<Pick<UserDto, 'firstName' | 'lastName' | 'emailConfirmed'>>): void {
    const current = this.session();
    if (!current) return;
    this.setCurrentUser({ ...current, ...patch });
  }

  /** Zakończenie sesji z komunikatem (np. po usunięciu konta) i przejście na listę ofert. */
  endSession(message: string): void {
    this.clearSession();
    this.notify(message);
    this.router.navigate(['/jobs']);
  }

  isAuthenticated(): boolean {
    return this.getToken() !== null;
  }

  isAdmin(): boolean {
    return getJwtRoles(this.getToken()).includes('Admin');
  }

  isEmployer(): boolean {
    return getJwtRoles(this.getToken()).includes('Employer');
  }

  /** Zwraca token tylko jeśli jest ważny. Czyta sygnał sesji, więc szablony reagują na zmiany. */
  getToken(): string | null {
    const token = this.session()?.token ?? null;
    return token && !isJwtExpired(token) ? token : null;
  }

  /**
   * Reakcja na 401 z API dla żądania wysłanego z tokenem `requestToken`.
   * Spóźnione 401 dla starego tokena (lub żądania bez tokena) nie może wylogować nowej sesji.
   */
  handleUnauthorized(requestToken: string | null): void {
    if (!requestToken || requestToken !== this.session()?.token) return;

    if (this.isAuthPage(this.router.url)) {
      this.clearSession();
      return;
    }

    this.expireSession();
  }

  private redirectToLogin(returnUrl: string): void {
    this.router.navigate(['/login'], { queryParams: { returnUrl } });
  }

  private setCurrentUser(user: UserDto): void {
    localStorage.setItem(this.TOKEN_KEY, user.token);
    localStorage.setItem(this.USER_KEY, JSON.stringify(user));
    this.session.set(user);
    this.scheduleExpiry(user.token);
  }

  private clearSession(): void {
    this.clearExpiryTimer();
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.USER_KEY);
    this.session.set(null);
  }

  private restoreSession(): UserDto | null {
    try {
      const userJson = localStorage.getItem(this.USER_KEY);
      const token = localStorage.getItem(this.TOKEN_KEY);
      const user: UserDto | null = userJson ? JSON.parse(userJson) : null;

      if (user && token && user.token === token && !isJwtExpired(token)) {
        return user;
      }
    } catch {
      // Uszkodzone dane w localStorage – traktujemy jak brak sesji
    }

    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.USER_KEY);
    return null;
  }

  private scheduleExpiry(token: string | null): void {
    this.clearExpiryTimer();

    const expiration = getJwtExpiration(token);
    if (expiration === null) return;

    const delay = Math.max(0, expiration - Date.now());
    this.expiryTimer = setTimeout(() => {
      this.expiryTimer = null;
      if (isJwtExpired(token)) {
        this.expireSession();
      } else {
        // Timer ograniczony do MAX_TIMER_DELAY – planujemy kolejne sprawdzenie
        this.scheduleExpiry(token);
      }
    }, Math.min(delay, MAX_TIMER_DELAY));
  }

  private clearExpiryTimer(): void {
    if (this.expiryTimer !== null) {
      clearTimeout(this.expiryTimer);
      this.expiryTimer = null;
    }
  }

  /** Wspólna obsługa wygaśnięcia sesji – z timera i z 401 z API. */
  private expireSession(): void {
    this.clearSession();
    this.notify('Sesja wygasła, zaloguj się ponownie');

    // Ze stron publicznych nie wyrzucamy – przekierowanie tylko z tras chronionych guardem
    if (this.isOnProtectedRoute()) {
      this.redirectToLogin(this.router.url);
    }
  }

  private isOnProtectedRoute(): boolean {
    let route: ActivatedRouteSnapshot | null = this.router.routerState.snapshot.root;
    while (route) {
      if (route.routeConfig?.canActivate?.length) return true;
      route = route.firstChild;
    }
    return false;
  }

  private isAuthPage(url: string): boolean {
    return /^\/(login|register)(?=[/?#]|$)/.test(url);
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }
}
