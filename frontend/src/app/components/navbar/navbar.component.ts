import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { AuthService } from '../../services/auth.service';

/**
 * Górny pasek: logo (link do ofert), główna nawigacja zależna od roli i menu konta z inicjałami.
 * Poniżej 768 px linki pokazują same ikony – etykiety zostają dostępne (ukryte wizualnie, nie `display: none`).
 */
@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, MatIconModule, MatMenuModule],
  templateUrl: './navbar.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './navbar.component.scss'
})
export class NavbarComponent {
  readonly authService = inject(AuthService);
  readonly user = this.authService.currentUser;

  readonly fullName = computed(() => {
    const user = this.user();
    if (!user) return '';
    return [user.firstName, user.lastName].map(part => part?.trim()).filter(Boolean).join(' ') || user.email;
  });

  /** Inicjały do awatara („JK”); bez imienia i nazwiska – pierwsza litera e-maila. */
  readonly initials = computed(() => {
    const user = this.user();
    if (!user) return '';
    const letters = [user.firstName, user.lastName]
      .map(part => part?.trim().charAt(0) ?? '')
      .join('');
    return (letters || user.email.charAt(0)).toLocaleUpperCase('pl-PL');
  });

  logout(): void {
    this.authService.logout();
  }
}
