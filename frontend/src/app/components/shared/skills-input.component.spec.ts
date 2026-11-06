import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SkillsInputComponent } from './skills-input.component';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('SkillsInputComponent', () => {
  let fixture: ComponentFixture<SkillsInputComponent>;
  let component: SkillsInputComponent;
  let http: HttpTestingController;

  const root = () => fixture.nativeElement as HTMLElement;
  const input = () => root().querySelector<HTMLInputElement>('input')!;
  const requests = () => http.match(r => r.url === '/api/skills');

  function create(options: { skills?: string[]; allowCreate?: boolean; max?: number } = {}): void {
    fixture = TestBed.createComponent(SkillsInputComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('skills', options.skills ?? []);
    fixture.componentRef.setInput('allowCreate', options.allowCreate ?? false);
    if (options.max !== undefined) fixture.componentRef.setInput('max', options.max);
    fixture.detectChanges();
  }

  function type(text: string): void {
    input().value = text;
    input().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function press(key: string): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key, cancelable: true, bubbles: true });
    input().dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  /** Czeka na debounce i odpowiada podpowiedziami. */
  function suggest(names: string[]): void {
    vi.advanceTimersByTime(250);
    requests().forEach(r => r.flush(names.map((name, i) => ({ name, jobsCount: 10 - i }))));
    fixture.detectChanges();
  }

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      imports: [SkillsInputComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    fixture?.destroy();
    http.verify({ ignoreCancelled: true });
    vi.useRealTimers();
  });

  describe('suggestions', () => {
    it('loads the most popular skills on first focus (debounced) and only once', () => {
      create();

      input().dispatchEvent(new Event('focus'));
      input().dispatchEvent(new Event('focus'));
      vi.advanceTimersByTime(250);

      const reqs = requests();
      expect(reqs.length).toBe(1);
      expect(reqs[0].request.params.has('search')).toBe(false);
    });

    it('debounces typing by 250 ms and sends only the last phrase', () => {
      create();

      type('a');
      type('an');
      vi.advanceTimersByTime(249);
      expect(requests().length).toBe(0);
      type('ang');
      vi.advanceTimersByTime(250);

      const reqs = requests();
      expect(reqs.length).toBe(1);
      expect(reqs[0].request.params.get('search')).toBe('ang');
    });

    it('cancels the previous request when a newer phrase is sent (switchMap)', () => {
      create();
      type('ang');
      vi.advanceTimersByTime(250);
      const [first] = requests();

      type('angu');
      vi.advanceTimersByTime(250);
      const [second] = requests();

      expect(first.cancelled).toBe(true);
      second.flush([{ name: 'Angular', jobsCount: 5 }]);
      expect(component.suggestions().map(s => s.name)).toEqual(['Angular']);
    });

    it('cancels the pending request when the component is destroyed', () => {
      create();
      type('ang');
      vi.advanceTimersByTime(250);
      const [req] = requests();

      fixture.destroy();

      expect(req.cancelled).toBe(true);
    });

    it('hides skills that are already selected (case-insensitive)', () => {
      create({ skills: ['angular'] });
      type('a');
      suggest(['Angular', 'Apache']);

      expect(component.suggestions().map(s => s.name)).toEqual(['Apache']);
    });

    it('keeps working without suggestions when the API fails', () => {
      create();
      type('a');
      vi.advanceTimersByTime(250);
      requests().forEach(r => r.flush(null, { status: 500, statusText: 'Error' }));

      expect(component.suggestions()).toEqual([]);
    });
  });

  describe('selecting from the list', () => {
    it('adds the chosen suggestion and clears the text', async () => {
      create();
      type('ang');

      component.onSelected({ option: { value: 'Angular' } } as never);
      // MatAutocompleteTrigger zapisuje wartość do pola asynchronicznie (mikrozadanie)
      await Promise.resolve();
      fixture.detectChanges();

      expect(component.skills()).toEqual(['Angular']);
      expect(input().value).toBe('');
      expect(root().querySelector('[role=status]')?.textContent).toBe('Dodano umiejętność Angular');
    });
  });

  describe('filter mode (no creating)', () => {
    it('Enter adds a typed name only when it matches a suggestion and uses its canonical spelling', () => {
      create();
      type('angular');
      suggest(['Angular']);

      press('Enter');

      expect(component.skills()).toEqual(['Angular']);
    });

    it('Enter with an unknown name shows a hint and adds nothing', () => {
      create();
      type('nieistnieje');
      suggest([]);

      press('Enter');

      expect(component.skills()).toEqual([]);
      expect(root().querySelector('.help')?.textContent).toContain('Wybierz umiejętność z listy');
      expect(root().querySelector('.help')?.classList.contains('error')).toBe(true);
    });

    it('does not offer the "create" option', () => {
      create();
      type('Rust');
      suggest([]);

      expect(component.createOption()).toBeNull();
    });

    it('the comma key does not add a skill', () => {
      create();
      type('Rust');

      const event = press(',');

      expect(event.defaultPrevented).toBe(false);
      expect(component.skills()).toEqual([]);
    });
  });

  describe('create mode', () => {
    it('offers "Dodaj" for an unknown name and not for a known one', () => {
      create({ allowCreate: true });
      type('Rust');
      suggest(['Rustc']);
      expect(component.createOption()).toBe('Rust');

      type('rustc');
      suggest(['Rustc']);
      expect(component.createOption()).toBeNull();
    });

    it('Enter creates a trimmed new skill and prevents form submission', () => {
      create({ allowCreate: true });
      type('  Rust  ');

      const event = press('Enter');

      expect(event.defaultPrevented).toBe(true);
      expect(component.skills()).toEqual(['Rust']);
    });

    it('the comma key confirms the typed name', () => {
      create({ allowCreate: true });
      type('Kotlin');

      const event = press(',');

      expect(event.defaultPrevented).toBe(true);
      expect(component.skills()).toEqual(['Kotlin']);
    });

    it('rejects a duplicate regardless of case', () => {
      create({ allowCreate: true, skills: ['Angular'] });
      type('angular');

      press('Enter');

      expect(component.skills()).toEqual(['Angular']);
      expect(root().querySelector('.help')?.textContent).toContain('jest już dodana');
    });

    it('rejects names with a comma (it separates lists in the API and URLs)', () => {
      create({ allowCreate: true });
      type('a,b');

      component.onSelected({ option: { value: 'a,b' } } as never);
      fixture.detectChanges();

      expect(component.skills()).toEqual([]);
      expect(root().querySelector('.help')?.textContent).toContain('przecinka');
    });

    it('enforces the limit of 15 skills', () => {
      const full = Array.from({ length: 15 }, (_, i) => `S${i}`);
      create({ allowCreate: true, skills: full });
      type('Extra');

      press('Enter');

      expect(component.skills().length).toBe(15);
      expect(root().querySelector('.help')?.textContent).toContain('Maksymalnie 15');
    });

    it('limits the length of the typed name to 50 characters', () => {
      create({ allowCreate: true });

      expect(input().getAttribute('maxlength')).toBe('50');
      type('x'.repeat(60));
      component.onSelected({ option: { value: 'x'.repeat(60) } } as never);

      expect(component.skills()).toEqual([]);
    });
  });

  describe('selected skills', () => {
    it('shows the counter and removable chips with accessible remove buttons', () => {
      create({ skills: ['Angular', 'Docker'] });

      expect(root().querySelector('.help')?.textContent).toBe('Wybrano 2 z 15');
      const labels = Array.from(root().querySelectorAll('app-removable-chips button')).map(b => b.getAttribute('aria-label'));
      expect(labels).toEqual(['Usuń umiejętność Angular', 'Usuń umiejętność Docker']);
    });

    it('removing a chip updates the model, announces it and moves focus to the next chip', () => {
      create({ skills: ['Angular', 'Docker'] });
      const buttons = root().querySelectorAll<HTMLButtonElement>('app-removable-chips button');

      buttons[0].click();
      fixture.detectChanges();

      expect(component.skills()).toEqual(['Docker']);
      expect(root().querySelector('[role=status]')?.textContent).toBe('Usunięto umiejętność Angular');
      expect(document.activeElement).toBe(buttons[1]);
    });

    it('removing the last chip returns focus to the text field', () => {
      create({ skills: ['Angular'] });
      document.body.appendChild(root());

      root().querySelector<HTMLButtonElement>('app-removable-chips button')!.click();

      expect(document.activeElement).toBe(input());
      root().remove();
    });

    it('associates the hint with the input for assistive technology', () => {
      create();

      const helpId = input().getAttribute('aria-describedby');
      expect(helpId).toBeTruthy();
      expect(root().querySelector(`#${helpId}`)).not.toBeNull();
    });
  });
});
