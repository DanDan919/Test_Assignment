using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using TestTask.Api.Data;
using TestTask.Api.Models;
using TestTask.Api.Services;
using TestTask.Api.Security;
using TestTask.Api.Validation;

var builder = WebApplication.CreateBuilder(args);
ApiSecurity.Configure(builder);

var postgresConnectionString = ApiSecurity.ReadSecret(builder.Configuration, "ConnectionStrings:Postgres");
if (string.IsNullOrWhiteSpace(postgresConnectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Postgres configuration is required.");
}

builder.Services.AddSingleton(NpgsqlDataSource.Create(postgresConnectionString));
builder.Services.AddSingleton<DatabaseInitializer>();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.MaxDepth = 16;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<IValidator<ProcessRequest>, ProcessRequestValidator>();
builder.Services.AddScoped<IElementStore, PostgresElementStore>();
builder.Services.AddScoped<IHtmlProcessingService, HtmlProcessingService>();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressMapClientErrors = true;
    options.InvalidModelStateResponseFactory = context =>
    {
        var message = builder.Environment.IsDevelopment() ? context.ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => error.ErrorMessage)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
            ?? "The request body is invalid." : "The request body is invalid.";

        var tooLarge = context.ModelState.Values.SelectMany(value => value.Errors)
            .Any(error => error.Exception is BadHttpRequestException { StatusCode: 413 });
        if (tooLarge)
        {
            return new ObjectResult(ProcessResponse.Failure(ErrorCodes.RequestTooLarge,
                "The request body must not exceed 1 MiB.")) { StatusCode = 413 };
        }

        return new BadRequestObjectResult(
            ProcessResponse.Failure(ErrorCodes.ValidationError, message));
    };
});

var app = builder.Build();
ApiSecurity.Use(app);

await using (var scope = app.Services.CreateAsyncScope())
{
    var databaseInitializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await databaseInitializer.InitializeAsync(app.Lifetime.ApplicationStopping);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger(options =>
    {
        options.RouteTemplate = "api/swagger/{documentName}/swagger.json";
    });

    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "api/swagger";
        options.SwaggerEndpoint("/api/swagger/v1/swagger.json", "TestTask API v1");
    });

    app.MapGet("/", () => Results.Redirect("/api/swagger"));
}
app.MapControllers();

app.Run();
