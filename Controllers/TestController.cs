using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MultiSourceContentDelivery.Filters;
using MultiSourceContentDelivery.Models;
using MultiSourceContentDelivery.Services;
using System.Text;

namespace MultiSourceContentDelivery.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnvironmentFilter("Development")] // 仅在开发环境中可用
public class TestController : ControllerBase
{
    private readonly ILogger<TestController> _logger;
    private readonly FileStorageService _storageService;
    private readonly FileHashCalculatorService _hashCalculator;
    private readonly NodeConfig _config;

    public TestController(
        ILogger<TestController> logger,
        FileStorageService storageService,
        FileHashCalculatorService hashCalculator,
        NodeConfig config)
    {
        _logger = logger;
        _storageService = storageService;
        _hashCalculator = hashCalculator;
        _config = config;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadTestFile()
    {
        try
        {
            // 创建一个测试文件
            var content = "这是一个测试文件内容 " + DateTime.UtcNow.ToString();
            var fileName = $"test_{DateTime.UtcNow:yyyyMMddHHmmss}.txt";
            var contentType = "text/plain";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            var success = await _storageService.SaveFileAsync(stream, fileName, contentType);

            if (!success)
            {
                return BadRequest("保存文件失败");
            }

            _logger.LogInformation("测试文件 {FileName} 上传成功", fileName);
            return Ok(new { fileName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "上传测试文件时发生错误");
            return StatusCode(500, "内部服务器错误");
        }
    }

    [HttpGet("storage-info")]
    public async Task<IActionResult> GetStorageInfo()
    {
        try
        {
            var availableStorage = await _storageService.GetAvailableStorageAsync();
            var currentLoad = await _storageService.GetCurrentLoadAsync();

            return Ok(new
            {
                AvailableStorageBytes = availableStorage,
                AvailableStorageGB = Math.Round(availableStorage / (1024.0 * 1024 * 1024), 2),
                CurrentLoadPercentage = currentLoad,
                MaxStorageCapacityBytes = _config.MaxStorageCapacityBytes,
                MaxStorageCapacityGB = Math.Round(_config.MaxStorageCapacityBytes / (1024.0 * 1024 * 1024), 2),
                MaxLoadPercentage = _config.MaxLoadPercentage
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取存储信息时发生错误");
            return StatusCode(500, "内部服务器错误");
        }
    }

    [HttpGet("files")]
    public async Task<IActionResult> GetAllFiles()
    {
        try
        {
            var context = HttpContext.RequestServices.GetRequiredService<DbContexts.FileInfoContext>();
            var files = await context.FileInfos.ToListAsync();

            return Ok(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取文件列表时发生错误");
            return StatusCode(500, "内部服务器错误");
        }
    }
}
