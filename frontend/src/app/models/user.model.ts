export interface UserDto {
  email: string;
  firstName: string;
  lastName: string;
  token: string;
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
