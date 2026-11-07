import { TestBed } from '@angular/core/testing';
import { PasswordStrengthComponent, describePasswordStrength } from './password-strength.component';

describe('PasswordStrengthComponent', () => {
  function render(password: string): HTMLElement {
    const fixture = TestBed.createComponent(PasswordStrengthComponent);
    fixture.componentRef.setInput('password', password);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('describes the strength in words, the bar is decorative only', () => {
    const root = render('Abcdef1');

    expect(root.querySelector('.text')?.textContent?.trim()).toBe('Siła hasła: średnia. Dodaj znak specjalny lub wydłuż hasło.');
    expect(root.querySelector('.bar')?.getAttribute('aria-hidden')).toBe('true');
    expect((root.querySelector('.fill') as HTMLElement).style.width).toBe('50%');
  });

  it('lists the requirements before anything is typed', () => {
    expect(render('').querySelector('.text')?.textContent).toContain('co najmniej 6 znaków, mała i wielka litera oraz cyfra');
  });

  it('names the missing requirements of a weak password', () => {
    expect(describePasswordStrength('abcdef')).toBe('Siła hasła: słaba. Brakuje: wielka litera, cyfra.');
  });

  it('rates long passwords with special characters as strong', () => {
    expect(describePasswordStrength('Abcdefgh1234!xyz')).toBe('Siła hasła: silna.');
    expect(describePasswordStrength('Abcdefgh12')).toBe('Siła hasła: dobra. Dłuższe hasło będzie jeszcze bezpieczniejsze.');
  });
});
