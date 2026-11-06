import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatsFunnelComponent } from './stats-funnel.component';
import { FunnelStage, buildFunnelStages } from '../../utils/stats-view';

vi.setConfig({ testTimeout: 20000 });

describe('StatsFunnelComponent', () => {
  let fixture: ComponentFixture<StatsFunnelComponent>;

  function create(stages: FunnelStage[]): HTMLElement {
    fixture = TestBed.createComponent(StatsFunnelComponent);
    fixture.componentRef.setInput('stages', stages);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => TestBed.configureTestingModule({ imports: [StatsFunnelComponent] }).compileComponents());

  it('renders a described image with a labelled row per stage', () => {
    const root = create(buildFunnelStages({ submitted: 8, inReview: 4, interview: 2, offered: 1, hired: 0 }));
    const figure = root.querySelector('.funnel') as HTMLElement;

    expect(figure.getAttribute('role')).toBe('img');
    expect(figure.getAttribute('aria-label')).toContain('Złożone: 8 (100%)');
    expect(figure.getAttribute('aria-label')).toContain('W analizie: 4 (50%)');
    const rows = Array.from(root.querySelectorAll('.row'));
    expect(rows.length).toBe(5);
    expect(rows[1].textContent).toContain('W analizie');
    expect(rows[1].textContent).toContain('4');
    expect(rows[1].textContent).toContain('50%');
  });

  it('scales bars relative to submitted and keeps non-empty stages visible', () => {
    const root = create(buildFunnelStages({ submitted: 1000, inReview: 500, interview: 2, offered: 0, hired: 0 }));
    const widths = Array.from(root.querySelectorAll('rect.fill')).map(rect => Number(rect.getAttribute('width')));

    expect(widths).toEqual([100, 50, 1, 0, 0]);
    expect(root.querySelector('svg.bar')?.getAttribute('viewBox')).toBe('0 0 100 14');
  });

  it('handles an empty funnel without NaN', () => {
    const root = create(buildFunnelStages({ submitted: 0, inReview: 0, interview: 0, offered: 0, hired: 0 }));

    expect(root.innerHTML).not.toContain('NaN');
    expect(Array.from(root.querySelectorAll('rect.fill')).every(rect => rect.getAttribute('width') === '0')).toBe(true);
  });

  it('offers a data table with percentages as an alternative', () => {
    const root = create(buildFunnelStages({ submitted: 8, inReview: 4, interview: 2, offered: 1, hired: 0 }));
    (root.querySelector('mat-slide-toggle button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(root.querySelector('.funnel')).toBeNull();
    const rows = Array.from(root.querySelectorAll('tbody tr'));
    expect(rows.length).toBe(5);
    expect(Array.from(rows[2].children).map(cell => cell.textContent?.trim())).toEqual(['Rozmowa', '2', '25%']);
    expect(root.querySelector('table caption')).not.toBeNull();
    expect(Array.from(root.querySelectorAll('thead th')).every(th => th.getAttribute('scope') === 'col')).toBe(true);
  });
});
