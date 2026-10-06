using System;
using ExpenseHub.Api.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExpenseHub.Api;

internal static class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        string? oracleConnectionString = builder.Configuration.GetConnectionString("Oracle");

        if (string.IsNullOrWhiteSpace(oracleConnectionString))
        {
            throw new InvalidOperationException(
                "Configure ConnectionStrings:Oracle with User Secrets or environment variables before starting ExpenseHub.Api.");
        }

        builder.Services.AddDbContext<ExpenseHubDbContext>(options => options.UseOracle(oracleConnectionString));
        builder.Services.AddOpenApi();

        WebApplication app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithName("GetHealth");

        app.Run();
    }
}
