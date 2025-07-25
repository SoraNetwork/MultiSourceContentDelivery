using System.ComponentModel.DataAnnotations;

namespace MultiSourceContentDelivery.Models;

public class FileInfo
{
    [Required]
    [Key]
    public required string Hash { get; set; } = string.Empty;
    
    public string ContentType { get; set; } = string.Empty;
    
    public long Size { get; set; }
    
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    
    [Required]
    public required string Name { get; set; } = string.Empty;
    
    // 存储该文件的节点URL列表
    public List<string> AvailableNodes { get; set; } = new();
    
    // 本地存储路径
    public string LocalPath { get; set; } = string.Empty;
    
    // 文件是否在本地存储
    public bool IsLocallyStored => !string.IsNullOrEmpty(LocalPath);
    
    // 文件的访问次数，用于负载均衡
    public int AccessCount { get; set; }
}
