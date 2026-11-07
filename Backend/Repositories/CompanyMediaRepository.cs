using Backend.Data;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class CompanyMediaRepository : ICompanyMediaRepository
{
    private readonly ApplicationDbContext _context;

    public CompanyMediaRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Company?> FindCompanyAsync(int companyId, CancellationToken cancellationToken = default)
    {
        return await _context.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
    }

    public async Task<List<CompanyPhoto>> GetPhotosAsync(int companyId, CancellationToken cancellationToken = default)
    {
        return await _context.CompanyPhotos
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<CompanyPhoto?> GetPhotoAsync(int companyId, int photoId, CancellationToken cancellationToken = default)
    {
        return await _context.CompanyPhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == photoId && p.CompanyId == companyId, cancellationToken);
    }

    public async Task<int> CountPhotosAsync(int companyId, CancellationToken cancellationToken = default)
    {
        return await _context.CompanyPhotos.CountAsync(p => p.CompanyId == companyId, cancellationToken);
    }

    public async Task<PhotosAddResult> AddPhotosAsync(int companyId, IReadOnlyList<CompanyPhoto> photos,
        CancellationToken cancellationToken = default)
    {
        // Limit sprawdzany pod blokadą X wiersza firmy: równoległe uploady członków tej samej firmy czekają tu na
        // siebie, a każdy liczy już zatwierdzone zdjęcia poprzednika (najpierw blokada, potem odczyty)
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        if (await CompanyRepository.LockCompanyAsync(_context, companyId, cancellationToken) == 0)
        {
            return PhotosAddResult.CompanyNotFound;
        }

        var existing = await _context.CompanyPhotos
            .Where(p => p.CompanyId == companyId)
            .GroupBy(p => p.CompanyId)
            .Select(g => new { Count = g.Count(), MaxOrder = g.Max(p => p.SortOrder) })
            .FirstOrDefaultAsync(cancellationToken);

        if ((existing?.Count ?? 0) + photos.Count > CompanyMedia.MaxPhotos)
        {
            return PhotosAddResult.TooMany;
        }

        // nowe zdjęcia na końcu galerii, w kolejności z formularza
        var nextOrder = existing == null ? 0 : existing.MaxOrder + 1;
        foreach (var photo in photos)
        {
            photo.CompanyId = companyId;
            photo.SortOrder = nextOrder++;
        }

        _context.CompanyPhotos.AddRange(photos);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PhotosAddResult.Added;
    }

    public async Task<IReadOnlyList<string>> GetExistingPhotoFileKeysAsync(IReadOnlyCollection<string> fileKeys,
        CancellationToken cancellationToken = default)
    {
        if (fileKeys.Count == 0)
        {
            return [];
        }

        return await _context.CompanyPhotos
            .AsNoTracking()
            .Where(p => fileKeys.Contains(p.FileKey))
            .Select(p => p.FileKey)
            .ToListAsync(cancellationToken);
    }

    public async Task<CompanyPhoto?> UpdateCaptionAsync(int companyId, int photoId, string? caption,
        CancellationToken cancellationToken = default)
    {
        var updated = await _context.CompanyPhotos
            .Where(p => p.Id == photoId && p.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Caption, caption), cancellationToken);

        return updated == 0 ? null : await GetPhotoAsync(companyId, photoId, cancellationToken);
    }

    public async Task<PhotoOrderResult> ReorderPhotosAsync(int companyId, IReadOnlyList<int> photoIds,
        CancellationToken cancellationToken = default)
    {
        // pod blokadą firmy - równoległy upload albo usunięcie nie zmieni zestawu zdjęć między sprawdzeniem a zapisem
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        if (await CompanyRepository.LockCompanyAsync(_context, companyId, cancellationToken) == 0)
        {
            return PhotoOrderResult.CompanyNotFound;
        }

        var photos = await _context.CompanyPhotos
            .Where(p => p.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        // dokładnie zdjęcia firmy, każde raz
        if (photoIds.Count != photos.Count
            || photoIds.Distinct().Count() != photoIds.Count
            || !photos.All(p => photoIds.Contains(p.Id)))
        {
            return PhotoOrderResult.Invalid;
        }

        var positions = photoIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        foreach (var photo in photos)
        {
            photo.SortOrder = positions[photo.Id];
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PhotoOrderResult.Reordered;
    }

    public async Task<string?> DeletePhotoAsync(int companyId, int photoId, CancellationToken cancellationToken = default)
    {
        var fileKey = await _context.CompanyPhotos
            .Where(p => p.Id == photoId && p.CompanyId == companyId)
            .Select(p => p.FileKey)
            .FirstOrDefaultAsync(cancellationToken);
        if (fileKey == null)
        {
            return null;
        }

        // klucz zdjęcia się nie zmienia; z równoległych usunięć tylko jedno usuwa wiersz (i plik)
        var deleted = await _context.CompanyPhotos
            .Where(p => p.Id == photoId && p.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? null : fileKey;
    }

    public async Task<BackgroundChange> SetOfferBackgroundAsync(int companyId, string? fileKey, string? contentType,
        CancellationToken cancellationToken = default)
    {
        // pod blokadą firmy: z dwóch równoległych zmian druga widzi plik pierwszej jako poprzedni (i go usuwa),
        // więc żaden plik nie zostaje osierocony
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        if (await CompanyRepository.LockCompanyAsync(_context, companyId, cancellationToken) == 0)
        {
            return new BackgroundChange(false, null);
        }

        var previousKey = await _context.Companies
            .Where(c => c.Id == companyId)
            .Select(c => c.OfferBackgroundKey)
            .FirstAsync(cancellationToken);

        await _context.Companies
            .Where(c => c.Id == companyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.OfferBackgroundKey, fileKey)
                .SetProperty(c => c.OfferBackgroundContentType, contentType), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new BackgroundChange(true, previousKey);
    }

    public async Task<bool> IsOfferBackgroundKeyRecordedAsync(int companyId, string fileKey,
        CancellationToken cancellationToken = default)
    {
        return await _context.Companies.AnyAsync(c => c.Id == companyId && c.OfferBackgroundKey == fileKey, cancellationToken);
    }

    public async Task<bool> UpdateOfferBackgroundSettingsAsync(int companyId, int? dim, OfferBackgroundFocus? focus,
        CancellationToken cancellationToken = default)
    {
        var updated = await _context.Companies
            .Where(c => c.Id == companyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.OfferBackgroundDim, c => dim ?? c.OfferBackgroundDim)
                .SetProperty(c => c.OfferBackgroundFocus, c => focus ?? c.OfferBackgroundFocus), cancellationToken);

        return updated > 0;
    }
}
