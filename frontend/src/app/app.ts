import { Component, ComponentRef, ViewContainerRef, effect, inject, viewChild } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NavbarComponent } from './components/navbar/navbar.component';
import { AuthService } from './services/auth.service';

/** Powłoka aplikacji (bootstrap w main.ts). */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, NavbarComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  standalone: true
})
export class App {
  readonly authService = inject(AuthService);
  private readonly bannerSlot = viewChild('emailBanner', { read: ViewContainerRef });
  private banner: ComponentRef<unknown> | null = null;
  private loadingBanner = false;

  constructor() {
    // Baner „Potwierdź adres e-mail” ładowany leniwie (osobny chunk, bez runtime @defer w paczce startowej);
    // miejsce ma stałą wysokość, więc jego pojawienie się nie przesuwa układu. Zniknięcie slotu niszczy baner.
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
}
