using Backend.Models;

namespace Backend.Interfaces;

// Konto użytkownika: dokumenty profilu kandydata i usunięcie konta
public interface IAccountRepository
{
    // dokumenty profilu użytkownika (bez śledzenia)
    Task<List<ProfileDocument>> GetDocumentsAsync(string userId, CancellationToken cancellationToken = default);

    // dokument użytkownika userId albo null (cudzy wygląda jak nieistniejący)
    Task<ProfileDocument?> GetDocumentAsync(string userId, int documentId, CancellationToken cancellationToken = default);

    // dokumenty użytkownika o podanych id (brakujące i cudze są pomijane)
    Task<List<ProfileDocument>> GetDocumentsAsync(string userId, IReadOnlyCollection<int> documentIds,
        CancellationToken cancellationToken = default);

    // liczba dokumentów profilu - wstępna kontrola limitu przed odczytem treści żądania uploadu
    Task<int> CountDocumentsAsync(string userId, CancellationToken cancellationToken = default);

    // Zapisuje rekordy dokumentów (pliki są już w magazynie) w transakcji z blokadą wiersza konta: tylko gdy po dodaniu
    // liczba i suma rozmiarów dokumentów mieszczą się w limitach profilu. Inny wynik niż Added - nic nie zapisano.
    Task<DocumentsAddResult> AddDocumentsAsync(string userId, IReadOnlyList<ProfileDocument> documents,
        CancellationToken cancellationToken = default);

    // które z podanych kluczy plików mają rekord dokumentu (sprzątanie po błędzie zapisu)
    Task<IReadOnlyList<string>> GetExistingDocumentFileKeysAsync(IReadOnlyCollection<string> fileKeys,
        CancellationToken cancellationToken = default);

    // usuwa dokument użytkownika; klucz pliku do usunięcia z magazynu albo null, gdy dokumentu nie ma
    Task<string?> DeleteDocumentAsync(string userId, int documentId, CancellationToken cancellationToken = default);

    // Usuwa konto razem z aplikacjami (historia, załączniki), dokumentami profilu, zapisanymi ofertami i e-mailami
    // w outboksie. Ostatni członek firmy z ofertami nie może usunąć konta; firma bez członków i ofert jest usuwana
    // razem z kontem. FileKeys - pliki do usunięcia z magazynu po zatwierdzeniu.
    Task<AccountDeletion> DeleteAccountAsync(string userId);
}

public enum DocumentsAddResult
{
    Added,
    UserNotFound,
    TooMany,
    TotalTooLarge
}

public enum AccountDeleteStatus
{
    Deleted,
    NotFound,
    LastCompanyMember
}

public sealed record AccountDeletion(AccountDeleteStatus Status, IReadOnlyList<string> FileKeys)
{
    public static AccountDeletion Failed(AccountDeleteStatus status) => new(status, []);
}
