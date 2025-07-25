using System.Net;
using System.Net.Sockets;
using DnsClient;
using MultiSourceContentDelivery.Models;

namespace MultiSourceContentDelivery.Services;

public class DnsResolutionService
{
    private readonly ILogger<DnsResolutionService> _logger;
    private readonly NodeConfig _config;
    private readonly LookupClient _dnsClient;
    private List<string> _nodeAddresses = new();
    private DateTime _lastResolved = DateTime.MinValue;

    public DnsResolutionService(ILogger<DnsResolutionService> logger, NodeConfig config)
    {
        _logger = logger;
        _config = config;
        _dnsClient = new LookupClient(new LookupClientOptions
        {
            UseCache = true,
            MinimumCacheTimeout = TimeSpan.FromMinutes(1),
            MaximumCacheTimeout = TimeSpan.FromMinutes(5)
        });
    }

    public async Task<List<string>> GetNodeAddresses()
    {
        if (DateTime.UtcNow - _lastResolved < TimeSpan.FromMinutes(5))
        {
            return _nodeAddresses;
        }

        try
        {
            var addresses = new HashSet<string>();
            var domain = _config.MainDomain;

            // 首先尝试解析 CNAME
            var cnameResult = await _dnsClient.QueryAsync(domain, QueryType.CNAME);
            if (cnameResult.Answers.CnameRecords().Any())
            {
                _logger.LogDebug("发现 CNAME 记录，domain: {Domain}", domain);
                domain = cnameResult.Answers.CnameRecords().First().CanonicalName;
            }

            // 解析 A 记录
            var aResult = await _dnsClient.QueryAsync(domain, QueryType.A);
            foreach (var aRecord in aResult.Answers.ARecords())
            {
                addresses.Add(aRecord.Address.ToString());
                _logger.LogDebug("添加 A 记录: {Address}", aRecord.Address);
            }

            // 解析 AAAA 记录
            var aaaaResult = await _dnsClient.QueryAsync(domain, QueryType.AAAA);
            foreach (var aaaaRecord in aaaaResult.Answers.AaaaRecords())
            {
                addresses.Add(aaaaRecord.Address.ToString());
                _logger.LogDebug("添加 AAAA 记录: {Address}", aaaaRecord.Address);
            }

            if (!addresses.Any())
            {
                _logger.LogWarning("未找到任何 A 或 AAAA 记录，domain: {Domain}", domain);
                // 作为后备方案，使用系统的 DNS 解析
                var hostAddresses = await Dns.GetHostAddressesAsync(domain);
                foreach (var address in hostAddresses)
                {
                    addresses.Add(address.ToString());
                    _logger.LogDebug("使用系统 DNS 解析添加记录: {Address}", address);
                }
            }

            _nodeAddresses = addresses.ToList();
            _lastResolved = DateTime.UtcNow;
            _logger.LogInformation("DNS 解析完成，找到 {Count} 个节点地址", _nodeAddresses.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析域名 {Domain} 时发生错误", _config.MainDomain);
        }

        return _nodeAddresses;
    }
}