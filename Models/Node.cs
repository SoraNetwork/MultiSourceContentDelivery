using System.ComponentModel.DataAnnotations;

namespace MultiSourceContentDelivery.Models;

public class Node
{
    [Key]
    public required string Address { get; set; }
    
    public required int Port { get; set; }
    
    public long AvailableSpace { get; set; }
    
    public double CurrentLoad { get; set; }
    
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    
    public bool IsActive { get; set; } = true;
}
