import { AbstractControl, FormGroupDirective, NgForm } from '@angular/forms';
import { ErrorStateMatcher } from '@angular/material/core';

/**
 * Pole „Powtórz hasło” w stanie błędu także wtedy, gdy grupa ma błąd `passwordMismatch`
 * (passwordsMatchValidator) – mat-error pojawia się przy polu i jest z nim powiązany.
 */
export class PasswordMatchErrorStateMatcher implements ErrorStateMatcher {
  isErrorState(control: AbstractControl | null, form: FormGroupDirective | NgForm | null): boolean {
    if (!control) return false;
    const interacted = control.touched || control.dirty || !!form?.submitted;
    return interacted && (control.invalid || !!control.parent?.hasError('passwordMismatch'));
  }
}
