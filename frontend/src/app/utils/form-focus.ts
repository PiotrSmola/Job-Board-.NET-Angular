/**
 * Po nieudanym wysłaniu formularza przenosi fokus na pierwsze niepoprawne pole (WCAG 3.3.1) –
 * czytnik ekranu odczyta wtedy jego etykietę i powiązany komunikat błędu.
 */
export function focusFirstInvalidControl(root: HTMLElement): void {
  const invalid = root.querySelector<HTMLElement>(
    'input.ng-invalid, textarea.ng-invalid, select.ng-invalid, mat-checkbox.ng-invalid input');
  invalid?.focus();
}
