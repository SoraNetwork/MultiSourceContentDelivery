using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using MultiSourceContentDelivery.DbContexts;
using MultiSourceContentDelivery.Models;
using System.ComponentModel;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MultiSourceContentDelivery.Services;

public class FileStorageService
{
    private readonly ILogger<FileStorageService> _logger;
    private readonly NodeConfig _config;
    private readonly string _storageDirectory;
    private long _currentStorageSize;
    private readonly FileInfoContext _context;
    private readonly FileHashCalculatorService _calculator;

    public FileStorageService(ILogger<FileStorageService> logger, NodeConfig config,FileInfoContext context,FileHashCalculatorService calculator)
    {
        _logger = logger;
        _config = config;
        _context = context;
        _calculator = calculator;
        _storageDirectory = Path.Combine(AppContext.BaseDirectory, "Storage");
        Directory.CreateDirectory(_storageDirectory);
        InitializeStorage();
    }
    public async Task<bool> FileExistsAsync (string hashOrName)
    {
        var fileInfo = _context.FileInfos
            .FirstOrDefault(x => x.Hash == hashOrName || x.Name == hashOrName);
        return fileInfo != null;
    }

    private async void InitializeStorage(CancellationToken cancellationToken = default)
    {
        var addFileInfos = new List<Models.FileInfo>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 10,
            CancellationToken = cancellationToken
        };

        var filepaths = Directory.GetFiles(_storageDirectory, "*", SearchOption.AllDirectories);

        await Parallel.ForEachAsync(
            filepaths,
            parallelOptions,
            async (filePath, ct) =>
            {
                var hash = await FileHashCalculatorService.ComputeSha256Async(filePath);

                var temp = await _context.FileInfos.FirstOrDefaultAsync(x => x.Hash == hash, ct);

                if (temp != null) return;

                var name = Path.GetFileName(filePath);

                var provider = new FileExtensionContentTypeProvider();
                provider.TryGetContentType(name, out string? contentType);

                var fileInfo = new Models.FileInfo
                {
                    Hash = hash,
                    ContentType = contentType ?? "application/octet-stream",
                    Size = new System.IO.FileInfo(filePath).Length,
                    Name = name
                };

                addFileInfos.Add(fileInfo);
            }
        );

        await _context.FileInfos.AddRangeAsync(addFileInfos, cancellationToken);
       
    }
    public async Task<(Stream? stream, string contentType)?> GetFileAsync(string hashOrName)
    {
        var fileInfo = _context.FileInfos
            .FirstOrDefault(x => x.Hash == hashOrName || x.Name == hashOrName);

        if (fileInfo == null)
        {
            return null;
        }

        try
        {
            var stream = File.OpenRead(Path.Combine(_storageDirectory, fileInfo.Name));
            fileInfo.LastAccessed = DateTime.UtcNow;
            return (stream, fileInfo.ContentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file {Hash}", hashOrName);
            return null;
        }
    }

    public bool HasFile(string hash) => _context.FileInfos.Any(f => f.Hash == hash);

    public int GetCurrentLoad() => (int)((_currentStorageSize * 100) / _config.MaxStorageCapacityBytes);
}