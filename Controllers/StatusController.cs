using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MultiSourceContentDelivery.DbContexts;
using MultiSourceContentDelivery.Models;
using MultiSourceContentDelivery.Services;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MultiSourceContentDelivery.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private readonly ILogger<StatusController> _logger;
    private readonly NodeConfig _config;
    private readonly FileInfoContext _context;
    private readonly DnsResolutionService _dnsService;
    private readonly FileStorageService _storageService;
    private static readonly List<NodeStatusHistory> _statusHistory = new();
    private const int MAX_HISTORY_ITEMS = 100;

    public StatusController(
        ILogger<StatusController> logger,
        NodeConfig config,
        FileInfoContext context,
        DnsResolutionService dnsService,
        FileStorageService storageService)
    {
        _logger = logger;
        _config = config;
        _context = context;
        _dnsService = dnsService;
        _storageService = storageService;
    }

    [HttpGet]
    public async Task<ActionResult<NodeStatus>> GetStatus()
    {
        try
        {
            // 获取DNS解析状态
            var nodeAddresses = await _dnsService.GetNodeAddresses();
            var hostName = Dns.GetHostName();

            // 获取存储状态
            var availableStorage = await _storageService.GetAvailableStorageAsync();
            var currentLoad = await _storageService.GetCurrentLoadAsync();

            // 获取文件统计
            var fileStats = await _context.FileInfos
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TotalFiles = g.Count(),
                    TotalSize = g.Sum(f => f.Size),
                    LocalFiles = g.Count(f => f.LocalPath != "")
                })
                .FirstOrDefaultAsync() ?? new { TotalFiles = 0, TotalSize = 0L, LocalFiles = 0 };

            // 获取连接的节点，去除自己
            var connectedNodes = await _context.Nodes
                .Where(n => n.LastSeen > DateTime.UtcNow.AddMinutes(-15))
                .Where(n => n.Url != GetNodeIp()) // 排除自己
                .Select(n => new ConnectedNode
                {
                    Url = $"https://{_config.MainDomain}",
                    LastSeen = n.LastSeen,
                    CurrentLoad = n.CurrentLoad,
                    AvailableStorageBytes = n.AvailableStorageBytes
                })
                .ToListAsync();

            var nodeIp = GetNodeIp();
            var status = new NodeStatus
            {
                Hostname = hostName,
                MainDomain = _config.MainDomain,
                LocalAddresses = new List<string> { nodeIp },
                ResolvedAddresses = nodeAddresses,
                IsPartOfCluster = nodeAddresses.Contains(nodeIp),
                StorageStatus = new StorageStatus
                {
                    AvailableStorageBytes = availableStorage,
                    CurrentLoadPercentage = currentLoad,
                    MaxStorageCapacityBytes = _config.MaxStorageCapacityBytes,
                    TotalFiles = fileStats.TotalFiles,
                    LocalFiles = fileStats.LocalFiles,
                    TotalStoredBytes = fileStats.TotalSize
                },
                ConnectedNodes = connectedNodes,
                LastUpdateTime = DateTime.UtcNow
            };

            // 更新历史记录
            UpdateStatusHistory(status);

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取节点状态时发生错误");
            return StatusCode(500, "内部服务器错误");
        }
    }

    [HttpGet("history")]
    public ActionResult<List<NodeStatusHistory>> GetHistory()
    {
        return _statusHistory;
    }

    private string GetNodeIp()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .FirstOrDefault() ?? "127.0.0.1";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting node IP, falling back to localhost");
            return "127.0.0.1";
        }
    }

    private void UpdateStatusHistory(NodeStatus currentStatus)
    {
        var history = new NodeStatusHistory
        {
            Timestamp = DateTime.UtcNow,
            IsPartOfCluster = currentStatus.IsPartOfCluster,
            ConnectedNodesCount = currentStatus.ConnectedNodes.Count,
            CurrentLoad = currentStatus.StorageStatus.CurrentLoadPercentage,
            LocalFiles = currentStatus.StorageStatus.LocalFiles,
            AvailableStorageBytes = currentStatus.StorageStatus.AvailableStorageBytes
        };

        _statusHistory.Add(history);
        
        // 保持历史记录在限定大小内
        if (_statusHistory.Count > MAX_HISTORY_ITEMS)
        {
            _statusHistory.RemoveAt(0);
        }
    }
}
