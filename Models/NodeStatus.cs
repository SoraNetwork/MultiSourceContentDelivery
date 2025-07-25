namespace MultiSourceContentDelivery.Models;

public class NodeStatus
{
    public string Hostname { get; set; } = string.Empty;
    public string MainDomain { get; set; } = string.Empty;
    public List<string> LocalAddresses { get; set; } = new();
    public List<string> ResolvedAddresses { get; set; } = new();
    public bool IsPartOfCluster { get; set; }
    public StorageStatus StorageStatus { get; set; } = new();
    public List<ConnectedNode> ConnectedNodes { get; set; } = new();
    public DateTime LastUpdateTime { get; set; }
}

public class StorageStatus
{
    public long AvailableStorageBytes { get; set; }
    public int CurrentLoadPercentage { get; set; }
    public long MaxStorageCapacityBytes { get; set; }
    public int TotalFiles { get; set; }
    public int LocalFiles { get; set; }
    public long TotalStoredBytes { get; set; }
}

public class ConnectedNode
{
    public string Url { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; }
    public int CurrentLoad { get; set; }
    public long AvailableStorageBytes { get; set; }
}

public class NodeStatusHistory
{
    public DateTime Timestamp { get; set; }
    public bool IsPartOfCluster { get; set; }
    public int ConnectedNodesCount { get; set; }
    public int CurrentLoad { get; set; }
    public int LocalFiles { get; set; }
    public long AvailableStorageBytes { get; set; }
}
