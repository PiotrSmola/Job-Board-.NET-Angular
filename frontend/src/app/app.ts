import {
  ChangeDetectionStrategy, Component, ComponentRef, ElementRef, ViewContainerRef, effect, inject, viewChild
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ViewportScroller } from '@angular/common';
import { ActivatedRouteSnapshot, NavigationEnd, Router, RouterOutlet, TitleStrategy } from '@angular/router';
import { filter } from 'rxjs';
import { NavbarComponent } from './components/navbar/navbar.component';
import { AuthService } from './services/auth.service';
import { PageTitleService } from './services/page-title.service';
import { DYNAMIC_TITLE, formatPageTitle } from './utils/page-title';

/** Ścieżka adresu bez parametrów zapytania i fragmentu – zmiana samych filtrów/kotwicy to nie nowa strona. */
function pathOf(url: string): string {
  return url.split(/[?#]/, 1)[0];
}

function hasDynamicTitle(route: ActivatedRouteSnapshot): boolean {
  let current: ActivatedRouteSnapshot | null = route;
  while (current) {
    if (current.data[DYNAMIC_TITLE]) return true;
    current = current.firstChild;
  }
  return false;
}

/**
 * Powłoka aplikacji (bootstrap w main.ts): link „Przejdź do treści”, nagłówek z nawigacją, baner potwierdzenia
 * e-maila i `main` z router-outlet. Po przejściu na inną stronę przewija na górę, przenosi fokus na `main`
 * i ogłasza tytuł strony; zmiana samych parametrów zapytania (filtry, paginacja) nie rusza fokusu ani przewinięcia.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, NavbarComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true
})
export class App {
  readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly scroller = inject(ViewportScroller);
  private readonly pageTitle = inject(PageTitleService);
  private readonly titleStrategy = inject(TitleStrategy);
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');
  private readonly bannerSlot = viewChild('emailBanner', { read: ViewContainerRef });
  private banner: ComponentRef<unknown> | null = null;
  private loadingBanner = false;
  private lastPath: string | null = null;

  constructor() {
    this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe(event => this.onNavigationEnd(event));

    // Baner „Potwierdź adres e-mail” ładowany leniwie (osobny chunk, bez runtime @defer w paczce startowej);
    // miejsce ma minimalną wysokość od pierwszego renderu, więc jego pojawienie się nie przesuwa układu.
    // Zniknięcie slotu niszczy baner.
    effect(() => {
      const slot = this.bannerSlot();
      if (!slot) {
        this.banner = null;
        return;
      }
      if (this.banner || this.loadingBanner) return;

      this.loadingBanner = true;
      import('./components/email-confirmation-banner/email-confirmation-banner.component')
        .then(({ EmailConfirmationBannerComponent }) => {
          // slot mógł zniknąć (np. wylogowanie) albo zostać odtworzony w trakcie ładowania – bierzemy bieżący
          const current = this.bannerSlot();
          if (current && !this.banner) {
            this.banner = current.createComponent(EmailConfirmationBannerComponent);
          }
        })
        .catch(error => console.error('Error loading the e-mail confirmation banner:', error))
        .finally(() => (this.loadingBanner = false));
    });
  }

  /** „Przejdź do treści” – bez zmiany adresu (przy `<base href="/">` sam `#main` przeładowałby stronę). */
  skipToMain(event: Event): void {
    event.preventDefault();
    this.main().nativeElement.focus();
  }

  private onNavigationEnd(event: NavigationEnd): void {
    const path = pathOf(event.urlAfterRedirects);
    const previous = this.lastPath;
    this.lastPath = path;
    // pierwsze wejście: przeglądarka sama ogłasza tytuł, a fokus zostaje na początku dokumentu (link „Przejdź do treści”)
    if (previous === null || previous === path) return;

    this.scroller.scrollToPosition([0, 0]);
    this.main().nativeElement.focus({ preventScroll: true });
    // tytuł z trasy (TitleStrategy ustawia document.title dopiero po NavigationEnd);
    // tytuł zależny od danych ogłasza widok po ich wczytaniu (PageTitleService.set)
    const snapshot = this.router.routerState.snapshot;
    if (!hasDynamicTitle(snapshot.root)) {
      this.pageTitle.announce(formatPageTitle(this.titleStrategy.buildTitle(snapshot)));
    }
  }
}
