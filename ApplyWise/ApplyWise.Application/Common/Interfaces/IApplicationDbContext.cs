namespace ApplyWise.Application.Common.Interfaces;

/// <summary>
/// Abstraction over the persistence context so the Application layer
/// does not depend on Infrastructure. Expose entity sets here as
/// <c>DbSet&lt;TEntity&gt;</c> properties as domain entities are added.
/// </summary>
public interface IApplicationDbContext
{
    /// <summary>
    /// Saves all pending changes in one database transaction (the unit-of-work commit).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the save, for example when the request is aborted.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
