using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flare.Infrastructure.Persistence;

internal sealed class FlareDbContextFactory : IDesignTimeDbContextFactory<FlareDbContext>
{
    public FlareDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FlareDbContext>()
            .UseNpgsql("Host=localhost;Database=flare;Username=postgres;Password=postgres")
            .Options;
        return new FlareDbContext(options);
    }
}
