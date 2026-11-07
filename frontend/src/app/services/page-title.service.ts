import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { formatPageTitle } from '../utils/page-title';

/**
 * Tytuł strony zależny od danych (np. tytuł oferty) i ogłaszanie tytułu czytnikom ekranu.
 * Trasy z `data: { [DYNAMIC_TITLE]: true }` wywołują `set()` po wczytaniu danych (także po błędzie, np. „Nie znaleziono
 * oferty”); pozostałe trasy ogłasza powłoka aplikacji po nawigacji.
 */
@Injectable({ providedIn: 'root' })
export class PageTitleService {
  private readonly title = inject(Title);
  private readonly announcer = inject(LiveAnnouncer);

  /** Ustawia tytuł „<part> – Job Board” i ogłasza go; ten sam tytuł nie jest ogłaszany ponownie. */
  set(part: string): void {
    const full = formatPageTitle(part);
    if (this.title.getTitle() === full) return;
    this.title.setTitle(full);
    this.announce(full);
  }

  /** Ogłasza tytuł strony (po zmianie strony – powłoka podaje tytuł nowej trasy). */
  announce(text: string): void {
    this.announcer.announce(text, 'polite').catch(() => undefined);
  }
}
