using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Backend.Helpers;

public static class DbExceptionExtensions
{
    // Zapis przegrał z równoległym żądaniem: niezgodny token współbieżności albo (obronnie) deadlock /
    // przekroczony czas oczekiwania na blokadę - MySQL wycofuje wtedy transakcję, więc nic nie zostało zapisane
    public static bool IsConcurrentChangeConflict(this Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return true;
        }

        // SaveChanges opakowuje błąd w DbUpdateException, ExecuteUpdate i Commit zgłaszają MySqlException wprost
        var mySqlException = exception as MySqlException ?? (exception as DbUpdateException)?.InnerException as MySqlException;

        return mySqlException?.ErrorCode is MySqlErrorCode.LockDeadlock or MySqlErrorCode.LockWaitTimeout;
    }

    public static bool IsDuplicateKey(this Exception exception) =>
        (exception as DbUpdateException)?.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };

    // usunięcie wiersza, do którego odwołuje się klucz obcy z ON DELETE RESTRICT
    public static bool IsForeignKeyViolation(this Exception exception) =>
        (exception as DbUpdateException)?.InnerException is MySqlException
        {
            ErrorCode: MySqlErrorCode.RowIsReferenced2 or MySqlErrorCode.RowIsReferenced
        };

    // zapis wiersza z kluczem obcym wskazującym nieistniejący wiersz (np. oferta usunięta w międzyczasie)
    public static bool IsMissingReference(this Exception exception) =>
        (exception as DbUpdateException)?.InnerException is MySqlException
        {
            ErrorCode: MySqlErrorCode.NoReferencedRow2 or MySqlErrorCode.NoReferencedRow
        };
}
