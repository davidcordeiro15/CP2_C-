using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Auth;
using ExpenseHub.Api.Configuration;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Expenses;
using ExpenseHub.Api.Initialization;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;
using ExpenseHub.Api.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ExpenseHub.Api;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        string? oracleConnectionString = builder.Configuration.GetConnectionString("Oracle");

        if (string.IsNullOrWhiteSpace(oracleConnectionString))
        {
            throw new InvalidOperationException("Configure ConnectionStrings:Oracle with User Secrets or environment variables before starting ExpenseHub.Api.");
        }

        JwtOptions jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        jwtOptions.Validate();
        builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(jwtOptions));
        builder.Services.AddOptions<SeedAdminOptions>()
            .Bind(builder.Configuration.GetSection(SeedAdminOptions.SectionName));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDbContext<ExpenseHubDbContext>(options => options.UseOracle(oracleConnectionString));
        builder.Services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ExpenseHubDbContext>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        if (!context.Response.HasStarted)
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            await context.Response.WriteAsJsonAsync(CreateProblem(
                                StatusCodes.Status401Unauthorized,
                                "Unauthorized",
                                "Authentication is required or the access token is invalid."));
                        }
                    },
                    OnForbidden = async context =>
                    {
                        if (!context.Response.HasStarted)
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsJsonAsync(CreateProblem(
                                StatusCodes.Status403Forbidden,
                                "Forbidden",
                                "The authenticated user does not have the required role."));
                        }
                    }
                };
            });
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole(ExpenseHubRoles.Admin));
            options.AddPolicy(AuthorizationPolicies.Employee, policy => policy.RequireRole(ExpenseHubRoles.Employee));
            options.AddPolicy(AuthorizationPolicies.Approver, policy => policy.RequireRole(ExpenseHubRoles.Approver));
            options.AddPolicy(AuthorizationPolicies.Finance, policy => policy.RequireRole(ExpenseHubRoles.Finance));
            options.AddPolicy(AuthorizationPolicies.Auditor, policy => policy.RequireRole(ExpenseHubRoles.Auditor));
        });
        builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
        builder.Services.AddScoped<ILoginService, LoginService>();
        builder.Services.AddScoped<IAdminSeeder, AdminSeeder>();
        builder.Services.AddScoped<IUserRegistrationService, UserRegistrationService>();
        builder.Services.AddScoped<IAdminUserService, AdminUserService>();
        builder.Services.AddScoped<IExpenseDraftRepository, EfExpenseDraftRepository>();
        builder.Services.AddScoped<IExpenseDraftService, ExpenseDraftService>();
        builder.Services.AddOpenApi();

        WebApplication app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithName("GetHealth")
            .AllowAnonymous();
        app.MapPost("/login", async (LoginRequest? request, ILoginService loginService, CancellationToken cancellationToken) =>
        {
            List<string> validationErrors = ValidateRequest(request);

            if (validationErrors.Count > 0)
            {
                Dictionary<string, string[]> errors = new()
                {
                    ["Request"] = [.. validationErrors]
                };
                return Results.ValidationProblem(errors);
            }

            LoginResponse? response = await loginService.AuthenticateAsync(request!, cancellationToken);
            return response is null
                ? Results.Problem(
                    title: "Unauthorized",
                    detail: "Invalid email or password.",
                    statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(response);
        }).AllowAnonymous();
        app.MapPost("/register", async (RegisterRequest? request, IUserRegistrationService registrationService, CancellationToken cancellationToken) =>
        {
            List<string> validationErrors = ValidateRegisterRequest(request);

            if (validationErrors.Count > 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Request"] = [.. validationErrors] });
            }

            ServiceResult<RegisterResponse> result = await registrationService.RegisterAsync(request!, cancellationToken);
            return result.Succeeded
                ? Results.Created($"/api/admin/users/{result.Value!.Id}", result.Value)
                : Results.ValidationProblem(new Dictionary<string, string[]> { ["Identity"] = [result.Error!.Message] });
        }).AllowAnonymous();
        app.MapGet("/api/admin/users", async (IAdminUserService adminUserService, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<UserSummary> users = await adminUserService.GetUsersAsync(cancellationToken);
            return Results.Ok(users);
        }).RequireAuthorization(AuthorizationPolicies.Admin);
        app.MapPut("/api/admin/users/{id}/roles", async (string id, UpdateRolesRequest? request, IAdminUserService adminUserService, System.Security.Claims.ClaimsPrincipal user, CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Request"] = ["A request body is required."] });
            }

            ServiceResult<UserSummary> result = await adminUserService.ReplaceRolesAsync(id, request, user, cancellationToken);
            return result.Succeeded
                ? Results.Ok(result.Value)
                : result.Error!.Code == "NotFound"
                    ? Results.NotFound(CreateProblem(StatusCodes.Status404NotFound, "Not Found", result.Error.Message))
                    : result.Error.Code == "SelfAdminRemoval"
                        ? Results.Problem(title: "Forbidden", detail: result.Error.Message, statusCode: StatusCodes.Status403Forbidden)
                        : Results.ValidationProblem(new Dictionary<string, string[]> { [result.Error.Code] = [result.Error.Message] });
        }).RequireAuthorization(AuthorizationPolicies.Admin);
        app.MapPost("/api/expenses", async (ExpenseDraftRequest? request, IExpenseDraftService service, System.Security.Claims.ClaimsPrincipal user, CancellationToken cancellationToken) =>
        {
            string? ownerId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? user.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                return Results.Problem(title: "Unauthorized", detail: "The authenticated user identifier is missing.", statusCode: StatusCodes.Status401Unauthorized);
            }

            List<string> validationErrors = ValidateExpenseRequest(request);
            if (validationErrors.Count > 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Request"] = [.. validationErrors] });
            }

            DraftServiceResult<ExpenseDraftResponse> result = await service.CreateAsync(request!, ownerId, cancellationToken);
            return result.Succeeded
                ? Results.Created($"/api/expenses/{result.Value!.Id}", result.Value)
                : result.ErrorCode == "InvalidCategory"
                    ? Results.ValidationProblem(new Dictionary<string, string[]> { ["CategoryId"] = [result.ErrorMessage!] })
                    : Results.ValidationProblem(new Dictionary<string, string[]> { [result.ErrorCode!] = [result.ErrorMessage!] });
        }).RequireAuthorization(AuthorizationPolicies.Employee);
        app.MapPut("/api/expenses/{id:int}", async (int id, ExpenseDraftRequest? request, IExpenseDraftService service, System.Security.Claims.ClaimsPrincipal user, CancellationToken cancellationToken) =>
        {
            string? ownerId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? user.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                return Results.Problem(title: "Unauthorized", detail: "The authenticated user identifier is missing.", statusCode: StatusCodes.Status401Unauthorized);
            }

            List<string> validationErrors = ValidateExpenseRequest(request);
            if (validationErrors.Count > 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Request"] = [.. validationErrors] });
            }

            DraftServiceResult<ExpenseDraftResponse> result = await service.UpdateAsync(id, request!, ownerId, cancellationToken);
            return result.Succeeded
                ? Results.Ok(result.Value)
                : result.ErrorCode == "NotFound"
                    ? Results.NotFound(CreateProblem(StatusCodes.Status404NotFound, "Not Found", result.ErrorMessage!))
                    : result.ErrorCode == "Conflict"
                        ? Results.Problem(title: "Conflict", detail: result.ErrorMessage!, statusCode: StatusCodes.Status409Conflict)
                        : Results.ValidationProblem(new Dictionary<string, string[]> { [result.ErrorCode!] = [result.ErrorMessage!] });
        }).RequireAuthorization(AuthorizationPolicies.Employee);

        if (builder.Configuration.GetValue<bool>("SeedAdmin:Enabled"))
        {
            using IServiceScope scope = app.Services.CreateScope();
            IAdminSeeder seeder = scope.ServiceProvider.GetRequiredService<IAdminSeeder>();
            await seeder.SeedAsync(CancellationToken.None);
        }

        await app.RunAsync();
    }

    private static List<string> ValidateRequest(LoginRequest? request)
    {
        if (request is null)
        {
            return ["A request body is required."];
        }

        List<ValidationResult> results = [];
        bool isValid = Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return isValid
            ? []
            : [.. results.Select(result => result.ErrorMessage ?? "Invalid value.")];
    }

    private static List<string> ValidateRegisterRequest(RegisterRequest? request)
    {
        if (request is null)
        {
            return ["A request body is required."];
        }

        List<ValidationResult> results = [];
        bool isValid = Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return isValid
            ? []
            : [.. results.Select(result => result.ErrorMessage ?? "Invalid value.")];
    }

    private static List<string> ValidateExpenseRequest(ExpenseDraftRequest? request)
    {
        if (request is null)
        {
            return ["A request body is required."];
        }

        List<ValidationResult> results = [];
        bool isValid = Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return isValid
            ? []
            : [.. results.Select(result => result.ErrorMessage ?? "Invalid value.")];
    }

    private static ProblemDetails CreateProblem(int status, string title, string detail)
    {
        return new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail
        };
    }
}
