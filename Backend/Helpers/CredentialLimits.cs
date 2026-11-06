namespace Backend.Helpers;

// Limity długości danych logowania we wszystkich DTO z e-mailem lub hasłem: e-mail jak kolumna Identity
// (varchar(256) - dłuższy dawał 500 przy zapisie), hasło - ogranicza koszt haszowania (PBKDF2) pojedynczego żądania
public static class CredentialLimits
{
    public const int MaxEmailLength = 256;
    public const int MaxPasswordLength = 128;
}
