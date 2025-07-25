using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using MultiSourceContentDelivery.DbContexts;
using MultiSourceContentDelivery.Models;

namespace MultiSourceContentDelivery.Services;

public class FileStorageService
{
    private readonly ILogger<FileStorageService> _logger;
    private readonly NodeConfig _config;
    private readonly FileInfoContext _context;
    private readonly FileHashCalculatorService _calculator;
    private readonly string _storageDirectory;
    private long _currentStorageSize;

    public FileStorageService(
        ILogger<FileStorageService> logger, 
        NodeConfig config,
        FileInfoContext context,
        FileHashCalculatorService calculator)
    {
        _logger = logger;
        _config = config;
        _context = context;
        _calculator = calculator;
        _storageDirectory = Path.Combine(AppContext.BaseDirectory, "Storage");
        Directory.CreateDirectory(_storageDirectory);
        InitializeStorage();
    }

    private async void InitializeStorage(CancellationToken cancellationToken = default)
    {
        try
        {
            var addFileInfos = new List<Models.FileInfo>();
            var files = Directory.GetFiles(_storageDirectory, "*", SearchOption.AllDirectories);

            foreach (var filePath in files)
            {
                var fileInfo = new System.IO.FileInfo(filePath);
                _currentStorageSize += fileInfo.Length;

                var hash = await FileHashCalculatorService.ComputeSha256Async(filePath);
                var existing = await _context.FileInfos.FirstOrDefaultAsync(x => x.Hash == hash, cancellationToken);
                
                if (existing != null)
                {
                    continue;
                }

                var name = Path.GetFileName(filePath);
                var provider = new FileExtensionContentTypeProvider();
                provider.TryGetContentType(name, out string? contentType);

                var newFileInfo = new Models.FileInfo
                {
                    Hash = hash,
                    ContentType = contentType ?? "application/octet-stream",
                    Size = fileInfo.Length,
                    Name = name,
                    LocalPath = filePath,
                    LastAccessed = DateTime.UtcNow
                };

                addFileInfos.Add(newFileInfo);
            }

            if (addFileInfos.Any())
            {
                await _context.FileInfos.AddRangeAsync(addFileInfos, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("已初始化存储，添加了 {Count} 个文件", addFileInfos.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化存储时发生错误");
        }
    }

    public async Task<bool> FileExistsAsync(string hashOrName)
    {
        return await _context.FileInfos
            .AnyAsync(x => x.Hash == hashOrName || x.Name == hashOrName);
    }

    public async Task<(Stream? stream, string contentType)?> GetFileAsync(string hashOrName)
    {
        var fileInfo = await _context.FileInfos
            .FirstOrDefaultAsync(x => x.Hash == hashOrName || x.Name == hashOrName);

        if (fileInfo == null)
        {
            return null;
        }

        try
        {
            var filePath = !string.IsNullOrEmpty(fileInfo.LocalPath) 
                ? fileInfo.LocalPath 
                : Path.Combine(_storageDirectory, fileInfo.Name);

            if (!System.IO.File.Exists(filePath))
            {
                return null;
            }

            var stream = System.IO.File.OpenRead(filePath);
            fileInfo.LastAccessed = DateTime.UtcNow;
            fileInfo.AccessCount++;
            await _context.SaveChangesAsync();
            
            return (stream, fileInfo.ContentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取文件 {Hash} 时发生错误", hashOrName);
            return null;
        }
    }

    public async Task<long> GetAvailableStorageAsync()
    {
        var drive = new DriveInfo(Path.GetPathRoot(_storageDirectory)!);
        return drive.AvailableFreeSpace;
    }

    public async Task<int> GetCurrentLoadAsync()
    {
        if (_config.MaxStorageCapacityBytes <= 0)
        {
            return 0;
        }
        
        return (int)((_currentStorageSize * 100) / _config.MaxStorageCapacityBytes);
    }

    public async Task<bool> SaveFileAsync(Stream stream, string fileName, string contentType)
    {
        var filePath = Path.Combine(_storageDirectory, fileName);
        try
        {
            using var fileStream = System.IO.File.Create(filePath);
            await stream.CopyToAsync(fileStream);

            var hash = await FileHashCalculatorService.ComputeSha256Async(filePath);
            var fileInfo = new System.IO.FileInfo(filePath);
            
            var dbFileInfo = new Models.FileInfo
            {
                Hash = hash,
                ContentType = contentType,
                Size = fileInfo.Length,
                Name = fileName,
                LocalPath = filePath,
                LastAccessed = DateTime.UtcNow
            };

            _currentStorageSize += fileInfo.Length;
            await _context.FileInfos.AddAsync(dbFileInfo);
            await _context.SaveChangesAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存文件 {FileName} 时发生错误", fileName);
            if (System.IO.File.Exists(filePath))
            {
                try
                {
                    System.IO.File.Delete(filePath);
                }
                catch
                {
                    // 忽略清理错误
                }
            }
            return false;
        }
    }
}
