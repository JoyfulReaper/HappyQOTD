/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


using HappyQOTD;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHappyQotdServices(builder.Configuration);

var app = builder.Build();

app.UseHappyQotdMiddleware();
app.MapHappyQotdEndpoints();

app.Run();

public partial class Program;
