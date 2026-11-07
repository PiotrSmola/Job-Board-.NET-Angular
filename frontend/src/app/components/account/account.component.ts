import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { ProfileTabComponent } from './profile-tab/profile-tab.component';
import { DocumentsTabComponent } from './documents-tab/documents-tab.component';
import { SecurityTabComponent } from './security-tab/security-tab.component';
import { NotificationsTabComponent } from './notifications-tab/notifications-tab.component';

/** Wartości `?tab=` – zachowane z czasów zakładek (linki z dialogu aplikowania, e-maili itp.). */
export type AccountSectionId = 'profile' | 'documents' | 'security' | 'notifications';

export interface AccountSection {
  id: AccountSectionId;
  label: string;
  /** Id elementu sekcji w DOM. */
  anchor: string;
}

/** Jak długo po przewinięciu do sekcji pilnujemy jej położenia, gdy sekcje powyżej dociągają treść (ms). */
export const KEEP_IN_VIEW_MS = 4000;

/** Zdarzenia oznaczające, że użytkownik sam przewija lub działa na stronie – wtedy przestajemy poprawiać położenie. */
const USER_SCROLL_EVENTS = ['wheel', 'touchstart', 'pointerdown', 'keydown'] as const;

const ALL_SECTIONS: AccountSection[] = [
  { id: 'profile', label: 'Profil', anchor: 'profil' },
  { id: 'documents', label: 'Dokumenty', anchor: 'dokumenty' },
  { id: 'security', label: 'Hasło i bezpieczeństwo', anchor: 'haslo' },
  { id: 'notifications', label: 'Powiadomienia', anchor: 'powiadomienia' }
];

/**
 * „Moje konto” (makieta Konto.dc.html) – jedna strona z sekcjami i bocznym menu zamiast zakładek.
 * Menu to zwykłe linki `?tab=…` (działa otwieranie w nowej karcie), bieżąca sekcja ma `aria-current`.
 * Wejście z `?tab=documents` (np. link z dialogu aplikowania) albo kliknięcie w menu przewija do sekcji
 * i przenosi fokus na jej nagłówek (położenie jest pilnowane, gdy sekcje powyżej dociągają treść); zmiana sekcji
 * podmienia wpis historii zamiast dokładać nowy.
 */
@Component({
  selector: 'app-account',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
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
  private readonly authService = inject(AuthService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** Dokumenty profilu ma tylko kandydat. */
  readonly sections: AccountSection[] = this.authService.isAdmin() || this.authService.isEmployer()
    ? ALL_SECTIONS.filter(section => section.id !== 'documents')
    : ALL_SECTIONS;
  readonly current = signal<AccountSectionId>('profile');
  private stopKeepingInView?: () => void;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopKeepingInView?.());

    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe(params => {
      const id = this.sections.find(section => section.id === params.get('tab'))?.id ?? 'profile';
      // ten sam stan (np. adres zaktualizowany po kliknięciu w menu albo pierwsze wejście bez ?tab=) – bez przewijania
      if (id === this.current()) return;
      this.current.set(id);
      afterNextRender(() => this.reveal(id), { injector: this.injector });
    });
  }

  /** Kliknięcie w menu – adres aktualizuje routerLink, tu przewijamy i przenosimy fokus (także przy ponownym kliknięciu). */
  select(event: MouseEvent, id: AccountSectionId): void {
    // Ctrl/⌘/Shift/środkowy przycisk – otwarcie w nowej karcie lub oknie, bez zmian na tej stronie
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    this.current.set(id);
    this.reveal(id);
  }

  queryParamsFor(id: AccountSectionId): { tab: string | null } {
    return { tab: id === 'profile' ? null : id };
  }

  private reveal(id: AccountSectionId): void {
    const anchor = this.sections.find(section => section.id === id)?.anchor;
    const section = anchor ? this.host.nativeElement.querySelector<HTMLElement>(`#${anchor}`) : null;
    if (!section) return;
    section.querySelector<HTMLElement>('h2')?.focus({ preventScroll: true });
    // jsdom (testy) nie ma scrollIntoView
    section.scrollIntoView?.({ block: 'start' });
    this.keepInView(section);
  }

  /**
   * Sekcje powyżej (profil, dokumenty) wczytują treść asynchronicznie i rosną ponad zarezerwowane minimum – przez
   * chwilę po przewinięciu ponawiamy `scrollIntoView` przy każdej zmianie wysokości kolumny sekcji, dopóki użytkownik
   * sam nie przewinie lub nie użyje klawiatury/wskaźnika (wtedy nie przeszkadzamy).
   */
  private keepInView(section: HTMLElement): void {
    this.stopKeepingInView?.();
    const column = section.parentElement;
    if (!column || typeof ResizeObserver === 'undefined') return;

    const observer = new ResizeObserver(() => section.scrollIntoView?.({ block: 'start' }));
    const stop = () => {
      observer.disconnect();
      clearTimeout(timer);
      USER_SCROLL_EVENTS.forEach(type => window.removeEventListener(type, stop));
      if (this.stopKeepingInView === stop) this.stopKeepingInView = undefined;
    };
    const timer = setTimeout(stop, KEEP_IN_VIEW_MS);
    USER_SCROLL_EVENTS.forEach(type => window.addEventListener(type, stop, { passive: true }));
    observer.observe(column);
    this.stopKeepingInView = stop;
  }
}
