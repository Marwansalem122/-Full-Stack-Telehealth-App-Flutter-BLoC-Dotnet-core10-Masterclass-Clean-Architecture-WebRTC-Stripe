namespace TelehealthPlatform.Application.Common.Interfaces;

/// <summary>Wraps DbContext.SaveChangesAsync — keeps handlers from
/// depending on EF Core directly, and gives an explicit commit point for
/// the transactional flows (e.g. User + Profile created together).</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}