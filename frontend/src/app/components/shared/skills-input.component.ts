import { ChangeDetectionStrategy, Component, computed, inject, input, model, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent, MatAutocompleteTrigger } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Subject, catchError, debounceTime, of, switchMap } from 'rxjs';
import { SkillSuggestion } from '../../models/taxonomy.model';
import { SkillService } from '../../services/skill.service';
import { MAX_SKILLS, MAX_SKILL_LENGTH, isValidSkillName } from '../../utils/job-query';
import { ChipItem, RemovableChipsComponent } from './removable-chips.component';

/**
 * Pole umiejętności: podpowiedzi z API + wybrane umiejętności jako usuwalne chipy.
 * `allowCreate` (formularz oferty) pozwala dodać nową nazwę; bez niego (filtry) tylko istniejące.
 */
@Component({
  selector: 'app-skills-input',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatAutocompleteModule, MatFormFieldModule, MatInputModule, RemovableChipsComponent],
  template: `
    <mat-form-field appearance="outline" class="skills-field" subscriptSizing="dynamic">
      <mat-label>{{ label() }}</mat-label>
      <input matInput #textInput [formControl]="control" [matAutocomplete]="auto" [attr.maxlength]="maxLength"
        [placeholder]="allowCreate() ? 'Wpisz i naciśnij Enter' : 'Zacznij wpisywać...'"
        autocomplete="off" [aria-describedby]="helpId"
        (focus)="onFocus()" (keydown.enter)="onEnter($event)" (keydown)="onKeydown($event)">
      <mat-autocomplete #auto="matAutocomplete" (optionSelected)="onSelected($event)">
        @for (suggestion of suggestions(); track suggestion.name) {
          <mat-option [value]="suggestion.name">{{ suggestion.name }} <span class="count">({{ suggestion.jobsCount }})</span></mat-option>
        }
        @if (createOption(); as term) {
          <mat-option [value]="term" class="create-option">Dodaj „{{ term }}”</mat-option>
        }
      </mat-autocomplete>
    </mat-form-field>
    <p [id]="helpId" class="help" [class.error]="isError()" aria-live="polite">{{ help() }}</p>
    <app-removable-chips [items]="chips()" ariaLabel="Wybrane umiejętności" [focusOnEmpty]="textInput" (remove)="remove($event)" />
    <p class="sr-only" role="status">{{ announcement() }}</p>
  `,
  styles: [`
    :host { display: block; }
    .skills-field { width: 100%; }
    .count { color: var(--text-secondary); font-size: 0.8rem; }
    .help { margin: 4px 0 8px; min-height: 1.25em; font-size: 0.8rem; color: var(--text-secondary); }
    .help.error { color: #ff8a80; }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
  `]
})
export class SkillsInputComponent {
  private static nextId = 0;

  private skillService = inject(SkillService);

  readonly skills = model<string[]>([]);
  readonly label = input('Umiejętności');
  readonly allowCreate = input(false);
  readonly max = input(MAX_SKILLS);

  readonly maxLength = MAX_SKILL_LENGTH;
  readonly helpId = `skills-help-${SkillsInputComponent.nextId++}`;
  readonly control = new FormControl('', { nonNullable: true });
  readonly rawSuggestions = signal<SkillSuggestion[]>([]);
  readonly announcement = signal('');
  readonly errorMessage = signal('');
  private readonly term = signal('');
  private readonly trigger = viewChild(MatAutocompleteTrigger);
  private readonly search$ = new Subject<string>();
  private focused = false;

  /** Podpowiedzi bez już wybranych umiejętności. */
  readonly suggestions = computed(() => {
    const selected = new Set(this.skills().map(s => s.toLowerCase()));
    return this.rawSuggestions().filter(s => !selected.has(s.name.toLowerCase()));
  });

  /** Opcja „Dodaj …” – tylko w trybie tworzenia, dla poprawnej nazwy spoza podpowiedzi i wybranych. */
  readonly createOption = computed(() => {
    const term = this.term();
    if (!this.allowCreate() || !isValidSkillName(term)) return null;
    const lower = term.toLowerCase();
    const known = this.rawSuggestions().some(s => s.name.toLowerCase() === lower)
      || this.skills().some(s => s.toLowerCase() === lower);
    return known ? null : term;
  });

  readonly chips = computed<ChipItem[]>(() =>
    this.skills().map(name => ({ key: name, label: name, removeLabel: `Usuń umiejętność ${name}` })));

  readonly isError = computed(() => this.errorMessage() !== '');
  readonly help = computed(() => this.errorMessage() || `Wybrano ${this.skills().length} z ${this.max()}`);

  constructor() {
    // switchMap anuluje poprzednie zapytanie – stare podpowiedzi nie nadpiszą nowszych
    this.search$.pipe(
      debounceTime(250),
      switchMap(term => this.skillService.suggest(term).pipe(catchError(() => of([] as SkillSuggestion[])))),
      takeUntilDestroyed()
    ).subscribe(suggestions => this.rawSuggestions.set(suggestions));

    this.control.valueChanges.pipe(takeUntilDestroyed()).subscribe(value => {
      const term = value.trim();
      this.term.set(term);
      this.errorMessage.set('');
      this.search$.next(term);
    });
  }

  onFocus(): void {
    if (!this.focused) {
      this.focused = true;
      this.search$.next(this.term());
    }
  }

  onSelected(event: MatAutocompleteSelectedEvent): void {
    this.add(String(event.option.value));
  }

  onEnter(event: Event): void {
    // Enter nie może wysłać formularza (np. dialogu oferty)
    event.preventDefault();
    // Aktywna opcja z listy jest obsługiwana przez autocomplete
    if (this.trigger()?.activeOption) return;

    const term = this.term();
    if (!term) return;

    if (this.allowCreate()) {
      this.add(term);
      return;
    }
    const match = this.rawSuggestions().find(s => s.name.toLowerCase() === term.toLowerCase());
    if (match) {
      this.add(match.name);
    } else {
      this.errorMessage.set('Wybierz umiejętność z listy podpowiedzi');
    }
  }

  onKeydown(event: KeyboardEvent): void {
    // Przecinek zatwierdza wpisaną nazwę (w nazwie jest niedozwolony – rozdziela listy w API)
    if (event.key === ',' && this.allowCreate()) {
      event.preventDefault();
      this.add(this.term());
    }
  }

  remove(name: string): void {
    this.skills.update(list => list.filter(s => s !== name));
    this.errorMessage.set('');
    this.announcement.set(`Usunięto umiejętność ${name}`);
  }

  private add(rawName: string): void {
    const name = rawName.trim();
    if (!name) return;

    const error = this.validationError(name);
    if (error) {
      // Wpisany tekst zostaje, żeby można go poprawić
      this.errorMessage.set(error);
      return;
    }

    this.control.setValue('');
    this.skills.update(list => [...list, name]);
    this.announcement.set(`Dodano umiejętność ${name}`);
  }

  private validationError(name: string): string | null {
    if (name.includes(',')) return 'Nazwa umiejętności nie może zawierać przecinka';
    if (name.length > MAX_SKILL_LENGTH) return `Nazwa umiejętności może mieć maksymalnie ${MAX_SKILL_LENGTH} znaków`;
    if (this.skills().some(s => s.toLowerCase() === name.toLowerCase())) return `Umiejętność „${name}” jest już dodana`;
    if (this.skills().length >= this.max()) return `Maksymalnie ${this.max()} umiejętności`;
    return null;
  }
}
