using HappyQOTD.Data;
using HappyQOTD.Quotes;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var quoteConnectionString = QuoteDatabase.Initialize();
builder.Services.AddSingleton<IQuoteProvider>(
    _ => new SqliteQuoteProvider(quoteConnectionString));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "HappyQOTD");

app.MapGet(
    "/api/quotes/random",
async (IQuoteProvider quoteProvider,
CancellationToken cancellationToken) =>
{
    var quote = await quoteProvider.GetRandomQuoteAsync(cancellationToken);

    return quote is null
        ? Results.NotFound()
        : Results.Ok(quote);
});

app.Run();