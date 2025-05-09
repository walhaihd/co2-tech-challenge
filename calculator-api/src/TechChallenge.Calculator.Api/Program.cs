using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TechChallenge.Calculator.Api;
using TechChallenge.Calculator.Api.Data;
using TechChallenge.Common.Exceptions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(_ => {});
builder.Services.Configure<ApiOptions>(options => builder.Configuration.GetSection(ApiOptions.SectionName).Bind(options));
builder.Services.AddHttpClient();
builder.Services.AddScoped<ICalculatorRepository, CalculatorRepository>();

var app = builder.Build();

app.UseSwagger();

app.UseSwaggerUI(_ => {});

app.MapGet(
        "/calculators/{userId}",
        async (
            [FromRoute] string userId,
            [FromQuery] long from,
            [FromQuery] long to,
            ICalculatorRepository repository,
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
                if (from >= to)
                    return Results.BadRequest("Invalid request time frame");

                var value = await
                    repository
                        .GetCalculatedValue(
                            userId,
                            from,
                            to,
                            cancellationToken);

                return Results.Ok(new CalculatorResponse(value.Value));
            }
            catch (NotFoundException e)
            {
                return Results.NotFound(e.Message);
            }
            catch (BadRequestException e)
            {
                return Results.BadRequest(e.Message);
            }
            catch (RequestTimeoutException)
            {
                return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
            }
            catch (Exception)
            {
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
        })
    .WithName("GetCalculatedTotalEmission")
    .Produces<IReadOnlyCollection<CalculatorResponse>>()
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status500InternalServerError)
    .Produces(StatusCodes.Status504GatewayTimeout)
    .WithOpenApi();

app.Run();