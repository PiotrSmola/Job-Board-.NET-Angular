import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RemovableChipsComponent } from './removable-chips.component';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('RemovableChipsComponent', () => {
  let fixture: ComponentFixture<RemovableChipsComponent>;
  let removed: string[];

  const root = () => fixture.nativeElement as HTMLElement;
  const buttons = () => Array.from(root().querySelectorAll<HTMLButtonElement>('button'));

  function create(items: { key: string; label: string; removeLabel?: string }[], focusOnEmpty: HTMLElement | null = null): void {
    removed = [];
    fixture = TestBed.createComponent(RemovableChipsComponent);
    fixture.componentRef.setInput('items', items);
    fixture.componentRef.setInput('ariaLabel', 'Aktywne filtry');
    fixture.componentRef.setInput('focusOnEmpty', focusOnEmpty);
    fixture.componentInstance.remove.subscribe(key => removed.push(key));
    fixture.detectChanges();
  }

  beforeEach(() => TestBed.configureTestingModule({ imports: [RemovableChipsComponent] }));

  it('renders nothing for an empty list', () => {
    create([]);

    expect(root().querySelector('ul')).toBeNull();
  });

  it('renders a labelled list with an accessible remove button per chip', () => {
    create([{ key: 'a', label: 'Junior' }, { key: 'b', label: 'B2B', removeLabel: 'Usuń filtr: B2B' }]);

    expect(root().querySelector('ul')?.getAttribute('aria-label')).toBe('Aktywne filtry');
    expect(buttons().map(b => b.getAttribute('aria-label'))).toEqual(['Usuń: Junior', 'Usuń filtr: B2B']);
    expect(buttons().every(b => b.type === 'button')).toBe(true);
  });

  it('emits the key of the removed chip', () => {
    create([{ key: 'a', label: 'Junior' }, { key: 'b', label: 'B2B' }]);

    buttons()[1].click();

    expect(removed).toEqual(['b']);
  });

  it('moves focus to the next chip, or to the previous one for the last chip', () => {
    create([{ key: 'a', label: 'A' }, { key: 'b', label: 'B' }, { key: 'c', label: 'C' }]);
    document.body.appendChild(root());

    buttons()[1].click();
    expect(document.activeElement).toBe(buttons()[2]);

    buttons()[2].click();
    expect(document.activeElement).toBe(buttons()[1]);
    root().remove();
  });

  it('moves focus to the fallback element when the last chip is removed', () => {
    const fallback = document.createElement('input');
    document.body.appendChild(fallback);
    create([{ key: 'a', label: 'A' }], fallback);
    document.body.appendChild(root());

    buttons()[0].click();

    expect(document.activeElement).toBe(fallback);
    fallback.remove();
    root().remove();
  });
});
