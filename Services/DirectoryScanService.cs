using Microsoft.EntityFrameworkCore;
using MultiSourceContentDelivery.DbContexts;
using MultiSourceContentDelivery.Models;
using System.Net.Http.Json;
using Polly;
using Polly.Retry;
using FileInfo = MultiSourceContentDelivery.Models.FileInfo;
using IOFileInfo = System.IO.FileInfo;

namespace MultiSourceContentDelivery.Services;

public class DirectoryScanService : BackgroundService
{
    private readonly ILogger<DirectoryScanService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly NodeConfig _config;
    private readonly FileHashCalculatorService _hashCalculator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public DirectoryScanService(
        ILogger<DirectoryScanService> logger,
        IServiceScopeFactory serviceScopeFactory,
        NodeConfig config,
        FileHashCalculatorService hashCalculator,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _config = config;
        _hashCalculator = hashCalculator;
        _httpClientFactory = httpClientFactory;

        _retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(3, retryAttempt => 
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, _) =>
                {
                    _logger.LogWarning(
                        "Error while syncing with node (Attempt {RetryCount}): {Message}", 
                        retryCount, 
                        exception.Exception.Message);
                });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Directory scan service starting");

        var timer = new PeriodicTimer(_config.DirectoryScanInterval);
        try
        {
            do
            {
                try
                {
                    await ScanDirectoryAndUpdateDatabase(stoppingToken);
                    await SyncWithOtherNodes(stoppingToken);
                    await CleanupStaleData(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during directory scan or node sync");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            timer.Dispose();
            _logger.LogInformation("Directory scan service stopped");
        }
    }

    private async Task CleanupStaleData(CancellationToken stoppingToken)
    {
        try
        {
            await _syncLock.WaitAsync(stoppingToken);

            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FileInfoContext>();

            // 清理超过15分钟未响应的节点
            var staleTime = DateTime.UtcNow.AddMinutes(-15);
            var staleNodes = await context.Nodes
                .Where(n => n.LastSeen < staleTime)
                .ToListAsync(stoppingToken);

            foreach (var node in staleNodes)
            {
                // 从所有文件的可用节点列表中移除此节点
                var affectedFiles = await context.FileInfos
                    .Where(f => f.AvailableNodes.Contains(node.Url))
                    .ToListAsync(stoppingToken);

                foreach (var file in affectedFiles)
                {
                    file.AvailableNodes.Remove(node.Url);
                }

                context.Nodes.Remove(node);
                _logger.LogInformation("Removed stale node: {NodeUrl}", node.Url);
            }

            // 清理没有可用节点的文件记录
            var orphanedFiles = await context.FileInfos
                .Where(f => f.LocalPath == "" && !f.AvailableNodes.Any())
                .ToListAsync(stoppingToken);

            if (orphanedFiles.Any())
            {
                context.FileInfos.RemoveRange(orphanedFiles);
                _logger.LogInformation("Removed {Count} orphaned file records", orphanedFiles.Count);
            }

            await context.SaveChangesAsync(stoppingToken);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task ScanDirectoryAndUpdateDatabase(CancellationToken stoppingToken)
    {
        var storageDir = Path.GetFullPath(_config.StorageDirectory);
        if (!Directory.Exists(storageDir))
        {
            Directory.CreateDirectory(storageDir);
            return;
        }

        try
        {
            await _syncLock.WaitAsync(stoppingToken);

            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FileInfoContext>();

            var files = Directory.GetFiles(storageDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    var hash = await FileHashCalculatorService.ComputeSha256Async(file);
                    var fileInfo = await context.FileInfos.FirstOrDefaultAsync(f => f.Hash == hash);
                    var relPath = Path.GetRelativePath(storageDir, file);

                    if (fileInfo == null)
                    {
                        fileInfo = new FileInfo
                        {
                            Hash = hash,
                            Name = Path.GetFileName(file),
                            ContentType = GetContentType(file),
                            Size = new IOFileInfo(file).Length,
                            LocalPath = relPath,
                            AvailableNodes = new List<string> { $"http://{Environment.MachineName}" }
                        };
                        await context.FileInfos.AddAsync(fileInfo);
                    }
                    else
                    {
                        fileInfo.LocalPath = relPath;
                        if (!fileInfo.AvailableNodes.Contains($"http://{Environment.MachineName}"))
                        {
                            fileInfo.AvailableNodes.Add($"http://{Environment.MachineName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file {FilePath}", file);
                    continue;
                }
            }

            await context.SaveChangesAsync(stoppingToken);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task SyncWithOtherNodes(CancellationToken stoppingToken)
    {
        try
        {
            await _syncLock.WaitAsync(stoppingToken);

            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FileInfoContext>();
            var client = _httpClientFactory.CreateClient("NodeSync");

            // 更新本节点信息
            var nodeInfo = new NodeInfo
            {
                Url = $"http://{Environment.MachineName}",
                AvailableStorageBytes = await GetAvailableStorageAsync(),
                CurrentLoad = await GetCurrentLoadAsync(),
                LastSeen = DateTime.UtcNow
            };

            var existingNode = await context.Nodes.FindAsync(nodeInfo.Url);
            if (existingNode != null)
            {
                existingNode.AvailableStorageBytes = nodeInfo.AvailableStorageBytes;
                existingNode.CurrentLoad = nodeInfo.CurrentLoad;
                existingNode.LastSeen = nodeInfo.LastSeen;
            }
            else
            {
                await context.Nodes.AddAsync(nodeInfo);
            }

            await context.SaveChangesAsync(stoppingToken);

            // 同步其他节点的文件信息
            var activeNodes = await context.Nodes
                .Where(n => n.IsActive && n.Url != nodeInfo.Url)
                .ToListAsync(stoppingToken);

            foreach (var node in activeNodes)
            {
                try
                {
                    var response = await _retryPolicy.ExecuteAsync(async () =>
                        await client.GetAsync($"{node.Url}/api/node/files", stoppingToken));

                    if (response.IsSuccessStatusCode)
                    {
                        var nodeFiles = await response.Content.ReadFromJsonAsync<List<FileInfo>>(
                            cancellationToken: stoppingToken);

                        if (nodeFiles != null)
                        {
                            await context.Entry(node).ReloadAsync();
                            node.LastSeen = DateTime.UtcNow;
                            await SyncFilesFromNode(context, nodeFiles, stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing with node {NodeUrl}", node.Url);
                }
            }

            await context.SaveChangesAsync(stoppingToken);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task SyncFilesFromNode(
        FileInfoContext context, 
        List<FileInfo> nodeFiles,
        CancellationToken cancellationToken)
    {
        if (!nodeFiles.Any()) return;

        // 批量查询已存在的文件
        var nodeFileHashes = nodeFiles.Select(f => f.Hash).ToList();
        var existingFiles = await context.FileInfos
            .Where(f => nodeFileHashes.Contains(f.Hash))
            .ToDictionaryAsync(f => f.Hash, f => f, cancellationToken);

        var filesToAdd = new List<FileInfo>();
        var filesToUpdate = new List<FileInfo>();

        foreach (var nodeFile in nodeFiles)
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (existingFiles.TryGetValue(nodeFile.Hash, out var localFile))
            {
                // 更新现有文件的节点信息
                var newNodes = nodeFile.AvailableNodes.Except(localFile.AvailableNodes).ToList();
                if (newNodes.Any())
                {
                    localFile.AvailableNodes.AddRange(newNodes);
                    filesToUpdate.Add(localFile);
                }
            }
            else
            {
                // 添加新文件记录
                filesToAdd.Add(nodeFile);
            }
        }

        if (filesToAdd.Any())
        {
            await context.FileInfos.AddRangeAsync(filesToAdd, cancellationToken);
            _logger.LogInformation("Added {Count} new file records from remote node", filesToAdd.Count);
        }

        if (filesToUpdate.Any())
        {
            context.FileInfos.UpdateRange(filesToUpdate);
            _logger.LogInformation("Updated {Count} existing file records with new node information", filesToUpdate.Count);
        }
    }

    private async Task<long> GetAvailableStorageAsync()
    {
        var storageDir = Path.GetFullPath(_config.StorageDirectory);
        var rootPath = Path.GetPathRoot(storageDir);
        if (rootPath == null)
            throw new InvalidOperationException($"Invalid storage directory path: {storageDir}");

        var drive = new DriveInfo(rootPath);
        return await Task.FromResult(drive.AvailableFreeSpace);
    }

    private async Task<int> GetCurrentLoadAsync()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FileInfoContext>();
        
        var recentAccessCount = await context.FileInfos
            .Where(f => f.LocalPath != "" && f.LastAccessed >= DateTime.UtcNow.AddMinutes(-5))
            .SumAsync(f => f.AccessCount);

        return (int)(recentAccessCount / 5.0); // 每分钟平均访问次数作为负载指标
    }

    private string GetContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }
}
