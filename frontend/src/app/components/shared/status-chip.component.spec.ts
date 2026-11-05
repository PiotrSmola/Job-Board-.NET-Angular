import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatusChipComponent } from './status-chip.component';
import { StatusTone } from '../../utils/status-labels';

describe('StatusChipComponent', () => {
  let fixture: ComponentFixture<StatusChipComponent>;

  function chip(): HTMLElement {
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('.status-chip') as HTMLElement;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [StatusChipComponent] }).compileComponents();
    fixture = TestBed.createComponent(StatusChipComponent);
  });

  it('renders the label', () => {
    fixture.componentRef.setInput('label', 'W analizie');

    expect(chip().textContent?.trim()).toBe('W analizie');
  });

  it('uses the neutral tone by default', () => {
    fixture.componentRef.setInput('label', 'Złożona');

    expect(chip().classList).toContain('tone-neutral');
  });

  it.each<StatusTone>(['neutral', 'info', 'accent', 'warning', 'success', 'danger', 'muted'])(
    'applies the tone-%s class',
    tone => {
      fixture.componentRef.setInput('label', 'X');
      fixture.componentRef.setInput('tone', tone);

      expect(chip().classList).toContain('status-chip');
      expect(chip().classList).toContain(`tone-${tone}`);
    }
  );

  it('updates when the inputs change', () => {
    fixture.componentRef.setInput('label', 'Rozmowa');
    fixture.componentRef.setInput('tone', 'accent');
    expect(chip().classList).toContain('tone-accent');

    fixture.componentRef.setInput('label', 'Odrzucona');
    fixture.componentRef.setInput('tone', 'danger');

    expect(chip().textContent?.trim()).toBe('Odrzucona');
    expect(chip().classList).toContain('tone-danger');
    expect(chip().classList).not.toContain('tone-accent');
  });
});
