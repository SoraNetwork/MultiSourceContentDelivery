using System.Net;
using MultiSourceContentDelivery.Models;

namespace MultiSourceContentDelivery.Services;

public class DnsResolutionService
{
    private readonly ILogger<DnsResolutionService> _logger;
    private readonly NodeConfig _config;
    private List<string> _nodeAddresses = new();
    private DateTime _lastResolved = DateTime.MinValue;

    public DnsResolutionService(ILogger<DnsResolutionService> logger, NodeConfig config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task<List<string>> GetNodeAddresses()
    {
        if (DateTime.UtcNow - _lastResolved < TimeSpan.FromMinutes(5))
        {
            return _nodeAddresses;
        }

        try
        {
            var hostAddresses = await Dns.GetHostAddressesAsync(_config.MainDomain);
            _nodeAddresses = hostAddresses.Select(a => a.ToString()).ToList();
            _lastResolved = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve DNS for domain {Domain}", _config.MainDomain);
        }

        return _nodeAddresses;
    }
}