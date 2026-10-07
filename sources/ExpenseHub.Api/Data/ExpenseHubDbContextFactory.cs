using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ExpenseHub.Api.Data;

internal sealed class ExpenseHubDbContextFactory : IDesignTimeDbContextFactory<ExpenseHubDbContext>
{
    public ExpenseHubDbContext CreateDbContext(string[] args)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        string? oracleConnectionString = configuration.GetConnectionString("Oracle");
        if (string.IsNullOrWhiteSpace(oracleConnectionString))
        {
            throw new InvalidOperationException("Configure ConnectionStrings:Oracle with User Secrets or environment variables before using EF Core design-time tools.");
        }

        DbContextOptionsBuilder<ExpenseHubDbContext> optionsBuilder = new();
        optionsBuilder.UseOracle(
            oracleConnectionString,
            oracleOptions => oracleOptions.MigrationsAssembly(typeof(ExpenseHubDbContext).Assembly.FullName));
        return new ExpenseHubDbContext(optionsBuilder.Options);
    }
}
