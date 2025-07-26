using System.ComponentModel.DataAnnotations;

namespace MultiSourceContentDelivery.Models;

public class NodeConfig
{
    [Required]
    public string MainDomain { get; set; } = string.Empty;

    [Required]
    public long MaxStorageCapacityBytes { get; set; }

    [Required]
    public int MaxLoadPercentage { get; set; }

    public TimeSpan NodeCacheUpdateInterval { get; set; } = TimeSpan.FromMinutes(60);

    public TimeSpan FileTransferDelay { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan DirectoryScanInterval { get; set; } = TimeSpan.FromMinutes(10);

    [Required]
    public int Port { get; set; } = 5000; // 默认端口号为5000

    public string StorageDirectory { get; set; } = "Storage";
}