using System.ComponentModel.DataAnnotations;

namespace MultiSourceContentDelivery.Models;

public class FileInfo
{
    [Required]
    [Key]
    public required string Hash { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastAccessed { get; set; } = DateTime.Now;
    [Required]
    public required string Name { get; set; } = string.Empty;
}
