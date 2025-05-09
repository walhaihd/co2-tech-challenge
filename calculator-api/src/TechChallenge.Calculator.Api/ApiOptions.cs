namespace TechChallenge.Calculator.Api;

public class ApiOptions
{
    public const string SectionName = nameof(ApiOptions);

    public string MeasurementApiUrl { get; set; } = string.Empty;
    public string EmissionApiUrl { get; set; } = string.Empty;
}