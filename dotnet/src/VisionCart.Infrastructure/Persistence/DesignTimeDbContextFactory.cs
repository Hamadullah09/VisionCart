using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VisionCart.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations</c> construct the context without booting the web
/// application. The connection string here is only ever used to build the model
/// and emit SQL — migrations are applied at deploy time against the real
/// connection string from configuration.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? @"Server=(localdb)\VisionCartDev;Database=VisionCart;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .AddInterceptors(new TimestampInterceptor())
            .Options;

        return new ApplicationDbContext(options);
    }
}
