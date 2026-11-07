import { HERO_MIN_OPACITY, offerHeroMask, offerHeroOpacity } from './offer-hero';

// Kontrast WCAG 2.x: luminancja względna sRGB
function luminance([r, g, b]: number[]): number {
  const channel = (value: number) => {
    const c = value / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

function contrast(a: number[], b: number[]): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (light + 0.05) / (dark + 0.05);
}

const rgb = (hex: string) => [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16));
const PAGE_BG = rgb('#121417');
/** Kolor strony z kryciem `opacity` % nałożony na całkowicie biały obraz (najgorszy przypadek). */
const overWhite = (opacity: number) => PAGE_BG.map(c => (opacity / 100) * c + (1 - opacity / 100) * 255);

describe('offerHeroOpacity', () => {
  it('maps the slider range 55–95 onto an effective opacity of 82–95 % (stronger = darker)', () => {
    expect(offerHeroOpacity(55)).toEqual({ top: 82, middle: 89 });
    expect(offerHeroOpacity(75)).toEqual({ top: 89, middle: 93 });
    expect(offerHeroOpacity(95)).toEqual({ top: 95, middle: 97 });
    expect(offerHeroOpacity(80).top).toBeGreaterThan(offerHeroOpacity(70).top);
  });

  it('clamps values outside the API range', () => {
    expect(offerHeroOpacity(10)).toEqual(offerHeroOpacity(55));
    expect(offerHeroOpacity(120)).toEqual(offerHeroOpacity(95));
    expect(offerHeroOpacity(Number.NaN)).toEqual(offerHeroOpacity(55));
  });

  it('keeps every hero text token at 4.5:1 or more even on a white image at the weakest setting', () => {
    const background = overWhite(offerHeroOpacity(55).top);
    expect(offerHeroOpacity(55).top).toBe(HERO_MIN_OPACITY);
    for (const token of ['#e9ebee', '#d4d8dd', '#a7adb5', '#5fd4e6', '#8fe3ef']) {
      expect(contrast(rgb(token), background)).toBeGreaterThanOrEqual(4.5);
    }
  });
});

describe('offerHeroMask', () => {
  it('goes from the effective opacity through a stronger middle stop to the page background', () => {
    expect(offerHeroMask(75)).toBe(
      'linear-gradient(180deg, color-mix(in srgb, var(--color-bg) 89%, transparent) 0%, ' +
      'color-mix(in srgb, var(--color-bg) 93%, transparent) 55%, var(--color-bg) 100%)'
    );
  });
});
