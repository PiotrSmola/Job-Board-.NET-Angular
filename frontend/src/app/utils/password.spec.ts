import { HttpErrorResponse } from '@angular/common/http';
import { FormControl, FormGroup } from '@angular/forms';
import {
  getPasswordErrorMessages, getPasswordRequirements, getPasswordStrength, passwordPolicyValidator, passwordsMatchValidator
} from './password';

describe('password utils', () => {
  describe('getPasswordRequirements', () => {
    it('reports every API rule separately', () => {
      expect(getPasswordRequirements('abc')).toEqual([
        { label: 'Co najmniej 6 znaków', met: false },
        { label: 'Mała litera', met: true },
        { label: 'Wielka litera', met: false },
        { label: 'Cyfra', met: false }
      ]);
      expect(getPasswordRequirements('Abcdef1').every(r => r.met)).toBe(true);
    });
  });

  describe('passwordPolicyValidator', () => {
    it('accepts an empty value (required is checked separately) and a valid password', () => {
      expect(passwordPolicyValidator(new FormControl(''))).toBeNull();
      expect(passwordPolicyValidator(new FormControl('Haslo123'))).toBeNull();
    });

    it('lists the missing character classes', () => {
      expect(passwordPolicyValidator(new FormControl('haslo'))).toEqual({
        passwordPolicy: { missing: ['wielka litera', 'cyfra'] }
      });
    });
  });

  describe('passwordsMatchValidator', () => {
    function group(password: string, confirm: string): FormGroup {
      return new FormGroup(
        { password: new FormControl(password), confirm: new FormControl(confirm) },
        { validators: passwordsMatchValidator('password', 'confirm') });
    }

    it('reports a mismatch only when the confirmation is filled in', () => {
      expect(group('Abc123', '').errors).toBeNull();
      expect(group('Abc123', 'Abc123').errors).toBeNull();
      expect(group('Abc123', 'Abc124').errors).toEqual({ passwordMismatch: true });
    });
  });

  describe('getPasswordStrength', () => {
    it.each([
      ['', 0, ''],
      ['abc', 1, 'Słabe'],
      ['Abcdef1', 2, 'Średnie'],
      ['Abcdefgh12', 3, 'Dobre'],
      ['Abcdefgh12!', 4, 'Silne'],
      ['Abcdefghijkl12', 4, 'Silne']
    ])('rates "%s" as level %i (%s)', (password, level, label) => {
      expect(getPasswordStrength(password)).toEqual({ level, label });
    });
  });

  describe('getPasswordErrorMessages', () => {
    function validationProblem(errors: unknown): HttpErrorResponse {
      return new HttpErrorResponse({ status: 400, statusText: 'Bad Request', error: { errors } });
    }

    it('translates the default Identity password errors and removes duplicates', () => {
      const error = validationProblem({
        NewPassword: [
          'Passwords must be at least 6 characters.',
          "Passwords must have at least one digit ('0'-'9').",
          "Passwords must have at least one uppercase ('A'-'Z')."
        ],
        PasswordRequiresDigit: ["Passwords must have at least one digit ('0'-'9')."]
      });

      expect(getPasswordErrorMessages(error)).toEqual([
        'Hasło musi mieć co najmniej 6 znaków',
        'Hasło musi zawierać cyfrę',
        'Hasło musi zawierać wielką literę'
      ]);
    });

    it('returns nothing for unrelated errors', () => {
      expect(getPasswordErrorMessages(validationProblem({ FirstName: ['The FirstName field is required.'] }))).toEqual([]);
      expect(getPasswordErrorMessages(new HttpErrorResponse({ status: 500 }))).toEqual([]);
      expect(getPasswordErrorMessages(new Error('x'))).toEqual([]);
      expect(getPasswordErrorMessages(new HttpErrorResponse({ status: 400, error: { message: 'Invalid' } }))).toEqual([]);
    });
  });
});
