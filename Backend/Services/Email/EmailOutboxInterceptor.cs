using System.Data.Common;
using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.Services.Email;

// Budzi procesor outboksu, gdy nowe wiadomości są już widoczne w bazie: zaraz po SaveChanges albo - gdy zapis był
// częścią jawnej transakcji (np. rejestracja z rolą) - dopiero po jej zatwierdzeniu. Bez sygnału wiadomość i tak
// zostanie wysłana w kolejnym cyklu odpytywania.
public sealed class EmailOutboxInterceptor : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly EmailOutboxSignal _signal;

    public EmailOutboxInterceptor(EmailOutboxSignal signal)
    {
        _signal = signal;
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        MarkSaving(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        MarkSaving(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Saved(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => Reset(eventData.Context, saving: true);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Reset(eventData.Context, saving: true);
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Committed(eventData.Context);

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Committed(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Reset(eventData.Context, saving: false);

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Reset(eventData.Context, saving: false);
        return Task.CompletedTask;
    }

    private static void MarkSaving(DbContext? context)
    {
        if (context is ApplicationDbContext db)
        {
            db.SavingEmails = db.ChangeTracker.Entries<EmailMessage>().Any(e => e.State == EntityState.Added);
        }
    }

    private void Saved(DbContext? context)
    {
        if (context is not ApplicationDbContext { SavingEmails: true } db)
        {
            return;
        }

        db.SavingEmails = false;
        if (db.Database.CurrentTransaction == null)
        {
            _signal.Notify();
        }
        else
        {
            db.UncommittedEmails = true;
        }
    }

    private void Committed(DbContext? context)
    {
        if (context is ApplicationDbContext { UncommittedEmails: true } db)
        {
            db.UncommittedEmails = false;
            _signal.Notify();
        }
    }

    private static void Reset(DbContext? context, bool saving)
    {
        if (context is not ApplicationDbContext db)
        {
            return;
        }

        if (saving)
        {
            db.SavingEmails = false;
        }
        else
        {
            db.UncommittedEmails = false;
        }
    }
}
