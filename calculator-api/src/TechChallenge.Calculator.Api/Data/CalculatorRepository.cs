using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using TechChallenge.Common.Exceptions;

namespace TechChallenge.Calculator.Api.Data;

public class CalculatorRepository : ICalculatorRepository
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ApiOptions> _apiSettings;
    private const long IntervalInSeconds = 15 * 60; // 15 minutes to seconds
    private const int DefaultHttpRequestTimeOutInSeconds = 3;


    public CalculatorRepository(IHttpClientFactory httpClientFactory, IOptions<ApiOptions> apiSettings)
    {
        _httpClientFactory = httpClientFactory;
        _apiSettings = apiSettings;
    }

    public async Task<CalculatedTotalEmission> GetCalculatedValue(
        string userId,
        long from,
        long to,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var measurements = await GetMeasurementsDuringTimeFrameOfUser(userId, from, to);
            var emissions = await GetEmissionValuesDuringTimeFrame(from, to);

            var totalEmissions = CalculateTotalEmission(measurements, emissions);

            return new CalculatedTotalEmission
            {
                Value = totalEmissions
            };
        }
        catch (NotFoundException)
        {
            throw;
        }
        catch (BadRequestException)
        {
            throw;
        }
        catch (RequestTimeoutException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new InternalServerErrorException(e.Message);
        }

    }

    private static double CalculateTotalEmission(List<Measurement> measurements, List<Emission> emissions)
    {
        double totalEmission = 0;
        if (measurements.Count == 0 || emissions.Count == 0)
            return totalEmission;

        var currentTimestamp = measurements.First().Timestamp;
        double sum = 0;
        var count = 0;
        var countRepeated = 0;
        foreach (var measurement in measurements)
        {
            if (measurement.Timestamp - currentTimestamp >= IntervalInSeconds)
            {
                var avgInKWh = sum / count / 4 / 1000;
                countRepeated++;
                var matchingEmission = emissions.FirstOrDefault(e => e.Timestamp == IntervalInSeconds * countRepeated);
                if (matchingEmission != null)
                {
                    totalEmission += avgInKWh * matchingEmission.KgPerKiloWattHr;
                }
                currentTimestamp = measurement.Timestamp;
                sum = measurement.Watts;
                count = 1;
            }
            else
            {
                sum += measurement.Watts;
                count++;
            }
        }

        return totalEmission;
    }

    private static long CalculateEmissionMaxTimestamp(long upperLimitTimestamp)
    {
        if (upperLimitTimestamp % IntervalInSeconds != 0)
        {
            return (upperLimitTimestamp / IntervalInSeconds + 1) * IntervalInSeconds;
        }
        return upperLimitTimestamp;
    }

    private async Task<List<Measurement>> GetMeasurementsDuringTimeFrameOfUser(string userId, long from, long to)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(DefaultHttpRequestTimeOutInSeconds);

            var measurementUrl = $"{_apiSettings.Value.MeasurementApiUrl}/{userId}?from={from}&to={to}";
            var measurementResponseMessage = await client.GetAsync(measurementUrl);

            if (measurementResponseMessage.IsSuccessStatusCode)
            {
                var result = await measurementResponseMessage.Content.ReadFromJsonAsync<List<Measurement>>();
                return result ?? [];
            }

            var content = await measurementResponseMessage.Content.ReadAsStringAsync();
            if (measurementResponseMessage.StatusCode == HttpStatusCode.NotFound)
            {
                throw new NotFoundException(content);
            }

            if (measurementResponseMessage.StatusCode == HttpStatusCode.BadRequest)
            {
                throw new BadRequestException(content);
            }
            throw new InternalServerErrorException(content);
        }
        catch (Exception e)
        {
            throw new InternalServerErrorException(e.Message);
        }
    }

    private async Task<List<Emission>> GetEmissionValuesDuringTimeFrame(long from, long to)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(DefaultHttpRequestTimeOutInSeconds);

            var emissionMaxTimestamp = CalculateEmissionMaxTimestamp(to);
            var emissionUrl = $"{_apiSettings.Value.EmissionApiUrl}?from={from}&to={emissionMaxTimestamp}";
            var emissionResponseMessage = await client.GetAsync(emissionUrl);

            if (emissionResponseMessage.IsSuccessStatusCode)
            {
                var result = await emissionResponseMessage.Content.ReadFromJsonAsync<List<Emission>>();
                return result ?? [];
            }

            var content = await emissionResponseMessage.Content.ReadAsStringAsync();
            if (emissionResponseMessage.StatusCode == HttpStatusCode.NotFound)
            {
                throw new NotFoundException(content);
            }
            if (emissionResponseMessage.StatusCode == HttpStatusCode.BadRequest)
            {
                throw new BadRequestException(content);
            }
            throw new InternalServerErrorException(content);
        }
        catch (TaskCanceledException)
        {
            throw new RequestTimeoutException("Server is not responding.");
        }
        catch (Exception e)
        {
            throw new InternalServerErrorException(e.Message);
        }
    }
}