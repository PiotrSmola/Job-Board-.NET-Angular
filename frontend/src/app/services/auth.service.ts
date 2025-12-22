import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { UserDto, LoginDto, RegisterDto } from '../models/user.model';
import { Router } from '@angular/router';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);

  private readonly API_URL = 'http://localhost:5285/api';
  private readonly TOKEN_KEY = 'jobboard_token';
  private readonly USER_KEY = 'jobboard_user';

  currentUser = signal<UserDto | null>(this.getUserFromStorage());

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

  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.USER_KEY);
    this.currentUser.set(null);
    this.router.navigate(['/login']);
  }

  isAuthenticated(): boolean {
    return !!this.getToken();
  }

  isAdmin(): boolean {
    const token = this.getToken();
    if (!token) return false;

    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      // Check for standard role claim or Microsoft specific role claim
      const roles = payload['role'] || payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
      
      if (!roles) return false;
      
      if (Array.isArray(roles)) {
        return roles.includes('Admin');
      }
      return roles === 'Admin';
    } catch (e) {
      return false;
    }
  }

  getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  private setCurrentUser(user: UserDto): void {
    localStorage.setItem(this.TOKEN_KEY, user.token);
    localStorage.setItem(this.USER_KEY, JSON.stringify(user));
    this.currentUser.set(user);
  }

  private getUserFromStorage(): UserDto | null {
    const userJson = localStorage.getItem(this.USER_KEY);
    return userJson ? JSON.parse(userJson) : null;
  }
}
