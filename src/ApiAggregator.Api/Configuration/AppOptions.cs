using System.ComponentModel.DataAnnotations;

namespace ApiAggregator.Api.Configuration;

public sealed class AggregationOptions
{
    public const string SectionName = "Aggregation";

    /// <summary>How long a successful source response is served from cache without calling the API again.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long the last good response is kept as a fallback when the API is down.</summary>
    public TimeSpan FallbackCacheDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Hard upper bound for one live fetch (including retries).</summary>
    public TimeSpan SourceTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public int MaxItemsPerSource { get; set; } = 20;
}

public sealed class StatsOptions
{
    public const string SectionName = "Statistics";

    /// <summary>Requests faster than this are "fast".</summary>
    public double FastThresholdMs { get; set; } = 100;

    /// <summary>Requests up to this are "average"; anything slower is "slow".</summary>
    public double AverageThresholdMs { get; set; } = 200;

    /// <summary>How long individual samples are kept for time-window queries (anomaly detection).</summary>
    public TimeSpan SampleRetention { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Safety cap so memory stays bounded under heavy load.</summary>
    public int MaxSamplesPerApi { get; set; } = 10_000;
}

public sealed class AnomalyDetectionOptions
{
    public const string SectionName = "AnomalyDetection";

    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Anomaly if the recent average is more than this % above the overall average.</summary>
    public double DegradationThresholdPercent { get; set; } = 50;

    /// <summary>Ignore windows with fewer samples than this (avoids noise).</summary>
    public int MinimumSamples { get; set; } = 3;
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer { get; set; } = string.Empty;
    [Required] public string Audience { get; set; } = string.Empty;

    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)] public int ExpiryMinutes { get; set; } = 60;
}

/// <summary>Demo credentials for the token endpoint. Replace with a real identity store in production.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class ExternalApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string? Token { get; set; }
}
