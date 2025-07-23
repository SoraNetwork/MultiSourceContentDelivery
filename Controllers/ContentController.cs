using Microsoft.AspNetCore.Mvc;
using MultiSourceContentDelivery.Models;
using MultiSourceContentDelivery.Services;

namespace MultiSourceContentDelivery.Controllers;

[ApiController]
[Route("content")]
public class ContentController : ControllerBase
{
    private readonly ILogger<ContentController> _logger;
    private readonly FileStorageService _fileStorage;
    private readonly NodeCommunicationService _nodeCommunication;
    private readonly NodeConfig _config;

    public ContentController(
        ILogger<ContentController> logger,
        FileStorageService fileStorage,
        NodeCommunicationService nodeCommunication,
        NodeConfig config)
    {
        _logger = logger;
        _fileStorage = fileStorage;
        _nodeCommunication = nodeCommunication;
        _config = config;
    }

    [HttpGet("{hash}")]
    public async Task<IActionResult> GetFile(string hash)
    {
        if (_fileStorage.GetCurrentLoad() < _config.MaxLoadPercentage)
        {
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

        var nodeWithFile = await _nodeCommunication.QueryFileExistenceAsync(hash);
        if (nodeWithFile != null)
        {
            return Redirect($"https://{nodeWithFile.FirstOrDefault()}/content/{hash}");
        }

        return NotFound();
    }

}