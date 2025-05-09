using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecurityDriven.Core;
using TechChallenge.ChaosMonkey;
using TechChallenge.Common.Exceptions;
using TechChallenge.DataSimulator;
using TechChallenge.Measurements.Api;
using TechChallenge.Measurements.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(_ => {});

builder.Services.AddSingleton(TimeProvider.System);
var intervalFrom = (int)TimeSpan.FromSeconds(1).TotalSeconds;
var intervalTo = (int)TimeSpan.FromSeconds(10).TotalSeconds;
builder.Services.AddScoped<IMeasurementsRepository, CalculationBasedUserHardcodedMeasurementsRepository>();
builder.Services
    .AddSingleton<IValueCalculator<SeededContext, double>, RandomBasedDeterministicValueCalculator>()
    .AddSingleton<IPointsProvider, PointsProvider>(
        sp => ActivatorUtilities.CreateInstance<PointsProvider>(sp, intervalFrom, intervalTo));

var chaosChance = ChausChance.FromPercentage(builder.Configuration.GetValue<int>("ChaosMonkey:Percentage"));
builder.Services.AddScoped<IChaosMonkey>(sp => new ErrorChaosMonkey(chaosChance, CryptoRandom.Shared));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(_ => {});

app.MapGet(
        "/measurements/{userId}",
        async (
            [FromRoute] string userId,
            [FromQuery] long from,
            [FromQuery] long to,
            IMeasurementsRepository repository,
            IChaosMonkey chaosMonkey,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            using (var _ = logger.BeginScope(
                       new Dictionary<string, object>
                       {
                           ["userId"] = userId,
                           ["from"] = from,
                           ["to"] = to,
                       }));
            try
            {
                await chaosMonkey.UnleashChaos();
                if (from >= to)
                    return Results.BadRequest("Invalid request time frame");

                var measurements =
                    repository
                        .GetMeasurementsAsync(
                            userId,
                            from,
                            to,
                            cancellationToken)
                        .SelectAwait(m => ValueTask.FromResult(new MeasurementResponse(m.Timestamp, m.Watts)));

                return Results.Ok(measurements);
            }
            catch (NotFoundException e)
            {
                return Results.NotFound(e.Message);
            }
        })
    .WithName("GetUserMeasurements")
    .Produces<IReadOnlyCollection<MeasurementResponse>>()
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status500InternalServerError)
    .WithOpenApi();

app.Run();