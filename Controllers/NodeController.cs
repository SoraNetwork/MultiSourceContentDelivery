using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MultiSourceContentDelivery.DbContexts;
using MultiSourceContentDelivery.Models;
using MultiSourceContentDelivery.Services;

namespace MultiSourceContentDelivery.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NodeController : ControllerBase
{
    private readonly FileInfoContext _context;
    private readonly NodeConfig _config;
    private readonly FileStorageService _storageService;

    public NodeController(FileInfoContext context, NodeConfig config, FileStorageService storageService)
    {
        _context = context;
        _config = config;
        _storageService = storageService;
    }

    [HttpGet("info")]
    public async Task<ActionResult<NodeInfo>> GetNodeInfo()
    {
        var availableStorage = await _storageService.GetAvailableStorageAsync();
        var currentLoad = await _storageService.GetCurrentLoadAsync();

        return new NodeInfo
        {
            Url = $"http://{HttpContext.Request.Host}",
            AvailableStorageBytes = availableStorage,
            CurrentLoad = currentLoad,
            LastSeen = DateTime.UtcNow
        };
    }

    [HttpGet("files")]
    public async Task<ActionResult<List<Models.FileInfo>>> GetFiles()
    {
        var files = await _context.FileInfos
            .Where(f => f.LocalPath != "")
            .ToListAsync();
        return files;
    }

    [HttpPost("sync")]
    public async Task<ActionResult<List<System.IO.FileInfo>>> SyncFiles([FromBody] List<Models.FileInfo> files)
    {
        foreach (var file in files)
        {
            var existingFile = await _context.FileInfos
                .FirstOrDefaultAsync(f => f.Hash == file.Hash);

            if (existingFile == null)
            {
                file.AvailableNodes = file.AvailableNodes.Distinct().ToList();
                await _context.FileInfos.AddAsync(file);
            }
            else
            {
                existingFile.AvailableNodes = existingFile.AvailableNodes
                    .Union(file.AvailableNodes)
                    .Distinct()
                    .ToList();
                existingFile.LastAccessed = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
        return Ok();
    }
}
