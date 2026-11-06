export interface UserDto {
  email: string;
  firstName: string;
  lastName: string;
  token: string;
  /**
   * Czy adres e-mail jest potwierdzony (login, rejestracja, zmiana hasła).
   * Brak pola (np. sesja zapisana przed tą zmianą) traktujemy jak potwierdzony – bez banera.
   */
  emailConfirmed?: boolean;
}

export interface LoginDto {
  email: string;
  password: string;
}

export interface RegisterDto {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
}

/** Rejestracja pracodawcy – konto i firma (status „oczekuje”) tworzone razem. */
export interface RegisterEmployerDto extends RegisterDto {
  companyName: string;
  companyDescription: string | null;
  website: string | null;
  location: string | null;
}

/** Body POST /api/auth/reset-password (email i token z linku w e-mailu). */
export interface ResetPasswordDto {
  email: string;
  token: string;
  newPassword: string;
}

/** Body POST /api/auth/confirm-email (userId i token z linku w e-mailu). */
export interface ConfirmEmailDto {
  userId: string;
  token: string;
}
