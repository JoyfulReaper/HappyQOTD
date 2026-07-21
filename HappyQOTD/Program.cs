using HappyQOTD;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHappyQotdServices(builder.Configuration);

var app = builder.Build();

app.UseHappyQotdMiddleware();
app.MapHappyQotdEndpoints();

app.Run();
