using HappyQOTD.Quotes;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IQuoteProvider, InMemoryQuoteProvider>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.MapGet("/", () =>
{
    return "HappyQOTD";
});

app.MapGet(
    "/api/quotes/random",
(IQuoteProvider quoteProvider) =>
{
    var quote = quoteProvider.GetRandomQuote();

    return quote is null
        ? Results.NotFound()
        : Results.Ok(quote);
});

app.Run();