using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

namespace Karar.Api.Services;

public sealed class GeoService : IDisposable
{
    private readonly DatabaseReader? _reader;
    private readonly ILogger<GeoService> _logger;

    public GeoService(IConfiguration configuration, ILogger<GeoService> logger)
    {
        _logger = logger;
        var dbPath = configuration["GeoLite2:DatabasePath"];
        if (!string.IsNullOrEmpty(dbPath) && File.Exists(dbPath))
        {
            try
            {
                _reader = new DatabaseReader(dbPath);
                logger.LogInformation("GeoLite2 database loaded from {Path}", dbPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "GeoLite2 database could not be loaded from {Path}", dbPath);
            }
        }
        else
        {
            logger.LogInformation("GeoLite2 database not configured — city-level trending disabled");
        }
    }

    public string? GetCity(System.Net.IPAddress? ip)
    {
        if (_reader is null || ip is null) return null;
        try
        {
            if (_reader.TryCity(ip, out var response))
                return response?.City.Name;
        }
        catch (AddressNotFoundException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GeoIP lookup failed");
        }
        return null;
    }

    public void Dispose() => _reader?.Dispose();
}
