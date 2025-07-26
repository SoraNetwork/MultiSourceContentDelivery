using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MultiSourceContentDelivery.DbContexts;
using MultiSourceContentDelivery.Models;
using MultiSourceContentDelivery.Services;

namespace MultiSourceContentDelivery.Controllers;

[ApiController]
[Route("content")]
public class ContentController : ControllerBase
{
    private readonly ILogger<ContentController> _logger;
    private readonly FileStorageService _fileStorage;
    private readonly FileInfoContext _context;
    private readonly NodeConfig _config;

    public ContentController(
        ILogger<ContentController> logger,
        FileStorageService fileStorage,
        FileInfoContext context,
        NodeConfig config)
    {
        _logger = logger;
        _fileStorage = fileStorage;
        _context = context;
        _config = config;
    }

    [HttpGet("{hash}")]
    public async Task<IActionResult> GetFile(string hash)
    {
        var fileInfo = await _context.FileInfos.FirstOrDefaultAsync(f => f.Hash == hash);
        if (fileInfo == null)
        {
            return NotFound();
        }

        // 如果文件在本地且负载未超过阈值，直接提供服务
        if (!string.IsNullOrEmpty(fileInfo.LocalPath) && await _fileStorage.GetCurrentLoadAsync() < _config.MaxLoadPercentage)
        {
            fileInfo.AccessCount++;
            await _context.SaveChangesAsync();

            var fileResult = await _fileStorage.GetFileAsync(hash);
            if (fileResult.HasValue)
            {
                var (stream, contentType) = fileResult.Value;
                if (stream != null)
                {
                    return File(stream, contentType);
                }
            }
        }

        // 选择负载最低的可用节点进行重定向
        var availableNodes = fileInfo.AvailableNodes
            .Select(url => _context.Nodes.FirstOrDefault(n => n.Url == url))
            .Where(n => n != null && n.IsActive && n.CurrentLoad < _config.MaxLoadPercentage)
            .OrderBy(n => n.CurrentLoad)
            .ToList();

        if (availableNodes.Any())
        {
            var targetNode = availableNodes.First();
            if (targetNode?.Url == null)
            {
                return StatusCode(500, "Invalid node configuration");
            }

            if (!Uri.TryCreate(targetNode.Url, UriKind.Absolute, out var uri))
            {
                _logger.LogError("Invalid node URL format: {Url}", targetNode.Url);
                return StatusCode(500, "Invalid node configuration");
            }

            // 使用配置的主域名，使用 HTTPS，忽略原始端口
            var redirectUrl = $"https://{uri.Host}.{_config.MainDomain}/content/{hash}";
            _logger.LogInformation("Redirecting request to: {RedirectUrl}", redirectUrl);
            return Redirect(redirectUrl);
        }

        return NotFound("No available nodes to serve the content");
    }

}