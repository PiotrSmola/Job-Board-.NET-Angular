import { TestBed } from '@angular/core/testing';
import { PasswordStrengthComponent } from './password-strength.component';

describe('PasswordStrengthComponent', () => {
  function render(password: string): HTMLElement {
    const fixture = TestBed.createComponent(PasswordStrengthComponent);
    fixture.componentRef.setInput('password', password);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('describes the strength in words, not only with the colour bar', () => {
    const root = render('Abcdef1');

    expect(root.querySelector('.label')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Siła hasła: Średnie');
    expect(root.querySelector('.bar')?.getAttribute('aria-hidden')).toBe('true');
    expect(root.querySelectorAll('.segment.filled').length).toBe(2);
  });

  it('says "brak" for an empty password', () => {
    expect(render('').querySelector('.label')?.textContent).toContain('brak');
  });

  it('lists each requirement with a textual met / not met state for screen readers', () => {
    const root = render('abcdef');
    const items = Array.from(root.querySelectorAll('.requirements li')).map(li => li.textContent?.replace(/\s+/g, ' ').trim());

    expect(items).toEqual([
      '✓ Co najmniej 6 znaków – spełnione',
      '✓ Mała litera – spełnione',
      '✗ Wielka litera – niespełnione',
      '✗ Cyfra – niespełnione'
    ]);
    expect(root.querySelector('.mark')?.getAttribute('aria-hidden')).toBe('true');
  });
});
