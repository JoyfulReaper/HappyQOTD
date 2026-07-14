using HappyQOTD.Data;
using HappyQOTD.Quotes;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var quoteConnectionString = QuoteDatabase.Initialize();
builder.Services.AddSingleton<IQuoteRepository>(
    _ => new SqliteRepositoryProvider(quoteConnectionString));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "HappyQOTD");

app.MapPost(
    "/api/quotes",
    async (IQuoteRepository quoteRepo, Quote quote) =>
    {
        var response = await quoteRepo.InsertQuoteAsync(quote);
        return response is null
            ? Results.NotFound()
            : Results.Ok(quote);
    });

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

app.Run();