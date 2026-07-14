using HappyQOTD.Data;
using HappyQOTD.Quotes;
using HappyQOTD.Security;
using HappyQOTD.Secutiry;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var quoteConnectionString = QuoteDatabase.Initialize();
builder.Services.AddSingleton<IQuoteRepository>(
    _ => new SqliteRepositoryProvider(quoteConnectionString));

builder.Services.Configure<QotdSecurityOptions>(
    builder.Configuration.GetSection(
        QotdSecurityOptions.SectionName));

builder.Services.AddScoped<ApiKeyEndpointFilter>();

builder.Services.AddProblemDetails();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "HappyQOTD");

app.MapPost(
        "/api/quotes",
        async (
            CreateQuoteRequest request,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var errors = QuoteValidator.ValidateQuote(request);

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var created = await repository.InsertQuoteAsync(
                request,
                cancellationToken);

            return Results.Created(
                $"/api/quotes/{created.Id}",
                created);
        })
    .AddEndpointFilter<ApiKeyEndpointFilter>();

app.MapGet(
    "/api/quotes/random",
    async (IQuoteRepository quoteRepo,
CancellationToken cancellationToken) =>
{
    var quote = await quoteRepo.GetRandomQuoteAsync(cancellationToken);

    return quote is null
        ? Results.NotFound()
        : Results.Ok(quote);
});

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Run();