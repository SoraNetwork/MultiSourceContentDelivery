using System.ComponentModel.DataAnnotations;

namespace MultiSourceContentDelivery.Models;

public class NodeInfo
{
    [Key]
    public required string Url { get; set; }

    public long AvailableStorageBytes { get; set; }

    public int CurrentLoad { get; set; }

    public DateTime LastSeen { get; set; } = DateTime.Now;

    public bool IsActive => DateTime.Now - LastSeen <= TimeSpan.FromMinutes(15);
}
