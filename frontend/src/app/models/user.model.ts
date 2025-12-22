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
