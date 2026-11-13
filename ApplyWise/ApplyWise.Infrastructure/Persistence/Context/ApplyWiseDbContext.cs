using ApplyWise.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Infrastructure.Persistence.Context;

/// <summary>
/// EF Core unit of work for ApplyWise (code-first).
/// </summary>
/// <remarks>
/// <para>
/// The schema comes from domain entities plus the <see cref="IEntityTypeConfiguration{TEntity}"/>
/// classes in <c>Persistence/Configurations</c>. Keep mapping out of this class: one
/// configuration class per entity keeps the context small and avoids merge conflicts.
/// </para>
/// <para>
/// The Application layer uses this through <see cref="IApplicationDbContext"/>, so it never
/// references Infrastructure directly.
/// </para>
/// </remarks>
public sealed class ApplyWiseDbContext(DbContextOptions<ApplyWiseDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Discover every IEntityTypeConfiguration<T> in this assembly, so new entities
        // only need a configuration class and no edit here.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplyWiseDbContext).Assembly);
    }
}
