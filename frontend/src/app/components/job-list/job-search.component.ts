import { ChangeDetectionStrategy, Component, input, linkedSignal, output } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Subject, debounceTime } from 'rxjs';
import { JobParams } from '../../models/job-params.model';
import { FilterChange } from './job-filters.component';

const TEXT_DEBOUNCE_MS = 500;

/**
 * Wiersz wyszukiwania nad listą (fraza + lokalizacja + „Szukaj”). Pisanie stosuje filtr po 500 ms bez wpisu
 * w historii; „Szukaj” / Enter – od razu, jako nowy wpis. Wartości z adresu (np. po „wstecz”) nadpisują pola.
 */
@Component({
  selector: 'app-job-search',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule],
  template: `
    <form class="search" role="search" aria-label="Szukaj ofert" (submit)="submit($event)">
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="phrase">
        <mat-icon matPrefix aria-hidden="true">search</mat-icon>
        <input matInput type="search" name="searchTerm" autocomplete="off" maxlength="100"
          aria-label="Stanowisko, firma lub technologia" placeholder="Stanowisko, firma lub technologia"
          [value]="searchInput()" (input)="onSearchInput($event)">
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="place">
        <input matInput name="location" autocomplete="off" maxlength="100" aria-label="Lokalizacja" placeholder="Lokalizacja"
          [value]="locationInput()" (input)="onLocationInput($event)">
      </mat-form-field>
      <button mat-flat-button type="submit">Szukaj</button>
    </form>
  `,
  styles: [`
    .search { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
    .phrase { flex: 3 1 320px; min-width: 0; }
    .place { flex: 1 1 200px; min-width: 0; }
    button { height: 44px; }
    @media (max-width: 600px) { button { flex: 1 1 100%; } }
  `]
})
export class JobSearchComponent {
  readonly params = input.required<JobParams>();
  readonly filterChange = output<FilterChange>();

  readonly searchInput = linkedSignal(() => this.params().searchTerm);
  readonly locationInput = linkedSignal(() => this.params().location);

  private readonly search$ = new Subject<string>();
  private readonly location$ = new Subject<string>();

  constructor() {
    // wartość już zastosowana (np. przez „Szukaj” przed upływem debounce) nie tworzy kolejnej nawigacji
    this.search$.pipe(debounceTime(TEXT_DEBOUNCE_MS), takeUntilDestroyed()).subscribe(value => {
      const searchTerm = value.trim();
      if (searchTerm !== this.params().searchTerm) this.filterChange.emit({ patch: { searchTerm }, replace: true });
    });
    this.location$.pipe(debounceTime(TEXT_DEBOUNCE_MS), takeUntilDestroyed()).subscribe(value => {
      const location = value.trim();
      if (location !== this.params().location) this.filterChange.emit({ patch: { location }, replace: true });
    });
  }

  onSearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.searchInput.set(value);
    this.search$.next(value);
  }

  onLocationInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.locationInput.set(value);
    this.location$.next(value);
  }

  /** „Szukaj” / Enter – stosuje obie wartości od razu. */
  submit(event: Event): void {
    event.preventDefault();
    this.filterChange.emit({
      patch: { searchTerm: this.searchInput().trim(), location: this.locationInput().trim() },
      replace: false
    });
  }
}
