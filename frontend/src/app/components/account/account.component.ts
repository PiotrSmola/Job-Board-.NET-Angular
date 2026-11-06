import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { AuthService } from '../../services/auth.service';
import { ProfileTabComponent } from './profile-tab/profile-tab.component';
import { DocumentsTabComponent } from './documents-tab/documents-tab.component';
import { SecurityTabComponent } from './security-tab/security-tab.component';
import { NotificationsTabComponent } from './notifications-tab/notifications-tab.component';

export type AccountTabId = 'profile' | 'documents' | 'security' | 'notifications';

interface AccountTab {
  id: AccountTabId;
  label: string;
  icon: string;
}

const ALL_TABS: AccountTab[] = [
  { id: 'profile', label: 'Profil', icon: 'person' },
  { id: 'documents', label: 'Dokumenty', icon: 'folder' },
  { id: 'security', label: 'Bezpieczeństwo', icon: 'lock' },
  { id: 'notifications', label: 'Powiadomienia', icon: 'notifications' }
];

/**
 * „Moje konto” – zakładki ładowane leniwie przy pierwszym wejściu (`matTabContent`) i zachowywane potem
 * (niezapisane zmiany w formularzu nie giną przy przełączaniu). Wybrana zakładka w `?tab=` (link z dialogu
 * aplikowania do dokumentów), zmiana zakładki podmienia wpis historii zamiast dokładać nowy.
 */
@Component({
  selector: 'app-account',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatIconModule,
    MatTabsModule,
    ProfileTabComponent,
    DocumentsTabComponent,
    SecurityTabComponent,
    NotificationsTabComponent
  ],
  templateUrl: './account.component.html',
  styleUrl: './account.component.scss'
})
export class AccountComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);

  /** Dokumenty profilu ma tylko kandydat. */
  readonly tabs: AccountTab[] = this.authService.isAdmin() || this.authService.isEmployer()
    ? ALL_TABS.filter(tab => tab.id !== 'documents')
    : ALL_TABS;
  readonly selectedIndex = signal(0);

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe(params => {
      const index = this.tabs.findIndex(tab => tab.id === params.get('tab'));
      this.selectedIndex.set(Math.max(index, 0));
    });
  }

  onTabChange(index: number): void {
    const tab = this.tabs[index];
    if (!tab || index === this.selectedIndex()) return;

    this.selectedIndex.set(index);
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab: tab.id === 'profile' ? null : tab.id },
      replaceUrl: true
    });
  }
}
