using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Expenses;

internal interface IExpenseCategorySeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}

internal sealed class ExpenseCategorySeeder : IExpenseCategorySeeder
{
    private static readonly string[] _defaultCategories =
    [
        "Meals",
        "Travel",
        "Office",
        "Entertainment",
        "Health"
    ];

    private readonly ExpenseHubDbContext _dbContext;

    public ExpenseCategorySeeder(ExpenseHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        HashSet<string> existing = await _dbContext.ExpenseCategories
            .Select(category => category.Name)
            .ToListAsync(cancellationToken) is List<string> names
            ? [.. names]
            : [];

        foreach (string name in _defaultCategories)
        {
            if (!existing.Contains(name))
            {
                _dbContext.ExpenseCategories.Add(new ExpenseCategory { Name = name });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
