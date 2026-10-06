using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ExpenseHub.Api.Data;

internal sealed class ExpenseHubDbContextFactory : IDesignTimeDbContextFactory<ExpenseHubDbContext>
{
    public ExpenseHubDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<ExpenseHubDbContext> optionsBuilder = new();
        optionsBuilder.UseOracle(
            "Data Source=localhost:1521/DESIGN_TIME_ONLY;User Id=DESIGN_TIME_ONLY;",
            oracleOptions => oracleOptions.MigrationsAssembly(typeof(ExpenseHubDbContext).Assembly.FullName));

        return new ExpenseHubDbContext(optionsBuilder.Options);
    }
}
