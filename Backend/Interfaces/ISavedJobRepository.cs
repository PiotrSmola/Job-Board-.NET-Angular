using Backend.Helpers;
using Backend.Models;

namespace Backend.Interfaces;

// Zapisane oferty kandydata. Listy pomijają oferty, które przestały być widoczne dla kandydata (szkic, firma
// niezatwierdzona) - pozycja zostaje i wraca, gdy oferta znów będzie widoczna; wygasłe i zamknięte są zwracane.
public interface ISavedJobRepository
{
    // idempotentne; false - oferty już nie ma (usunięta w międzyczasie)
    Task<bool> SaveAsync(string userId, int jobOfferId, DateTime savedAt);

    // idempotentne
    Task RemoveAsync(string userId, int jobOfferId);

    // od najnowszych zapisów, z ofertą i firmą
    Task<PagedList<SavedJob>> GetSavedJobsAsync(string userId, SavedJobParams savedJobParams);

    Task<List<int>> GetSavedJobIdsAsync(string userId);
}
