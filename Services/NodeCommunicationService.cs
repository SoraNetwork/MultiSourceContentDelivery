using MultiSourceContentDelivery.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MultiSourceContentDelivery.Services;

public class NodeCommunicationService : IHostedService, IDisposable
{
    private readonly ILogger<NodeCommunicationService> _logger;
    private readonly NodeConfig _config;
    private readonly FileStorageService _fileStorage;
    private readonly DnsResolutionService _dnsResolution;
    private readonly Dictionary<string, NodeDto> _nodes = new();
    private readonly UdpClient _udpClient;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _activeQueries = new();
    private Task? _receiveTask;
    private Task? _nodeUpdateTask;

    private class FileQuery
    {
        public string FileHash { get; set; } = string.Empty;
    }

    private class FileQueryResponse
    {
        public string FileHash { get; set; } = string.Empty;
        public bool Exists { get; set; }
    }

    public NodeCommunicationService(
        ILogger<NodeCommunicationService> logger,
        NodeConfig config,
        FileStorageService fileStorage,
        DnsResolutionService dnsResolution)
    {
        _logger = logger;
        _config = config;
        _fileStorage = fileStorage;
        _dnsResolution = dnsResolution;
        _udpClient = new UdpClient(_config.UdpPort);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting node communication service...");

        _nodeUpdateTask = Task.Run(() => UpdateNodeListPeriodically(_cts.Token), _cts.Token);
        _receiveTask = Task.Run(() => ReceiveMessagesAsync(_cts.Token), _cts.Token);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping node communication service...");
        _cts.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts.Dispose();
        _udpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task UpdateNodeListPeriodically(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _dnsResolution.GetNodeAddresses();
                await Task.Delay(_config.NodeCacheUpdateInterval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update node list");
                await Task.Delay(5000, token);
            }
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(token);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var remoteEndpoint = result.RemoteEndPoint;

                try
                {
                    var query = JsonSerializer.Deserialize<FileQuery>(json);
                    if (query != null)
                    {
                        _ = ProcessQueryAsync(query, remoteEndpoint);
                        continue;
                    }
                }
                catch (JsonException) { }

                try
                {
                    var response = JsonSerializer.Deserialize<FileQueryResponse>(json);
                    if (response != null)
                    {
                        ProcessResponse(response, remoteEndpoint.Address.ToString());
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid message format from {IP}", remoteEndpoint);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving UDP message");
            }
        }
    }

    private async Task ProcessQueryAsync(FileQuery query, IPEndPoint remoteEndpoint)
    {
        try
        {
            bool exists = await _fileStorage.FileExistsAsync(query.FileHash);
            var response = new FileQueryResponse
            {
                FileHash = query.FileHash,
                Exists = exists
            };

            var json = JsonSerializer.Serialize(response);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _udpClient.SendAsync(bytes, bytes.Length, remoteEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing query for {Hash}", query.FileHash);
        }
    }

    private void ProcessResponse(FileQueryResponse response, string nodeIp)
    {
        if (!response.Exists) return;

        if (_activeQueries.TryGetValue(response.FileHash, out var bag))
        {
            bag.Add(nodeIp);
        }
    }

    public async Task<IEnumerable<string>> QueryFileExistenceAsync(string fileHash, CancellationToken cancellationToken = default)
    {
        var responseBag = new ConcurrentBag<string>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        cts.CancelAfter(_config.FileTransferDelay);

        _activeQueries[fileHash] = responseBag;

        try
        {
            var nodeAddresses = await _dnsResolution.GetNodeAddresses();
            var sendTasks = nodeAddresses.Select(address =>
                SendQueryAsync(fileHash, address, cts.Token)).ToList();

            await Task.WhenAll(sendTasks);
            await WaitForResponsesAsync(cts.Token);

            return responseBag.Distinct().ToList();
        }
        finally
        {
            _activeQueries.TryRemove(fileHash, out _);
            cts.Dispose();
        }
    }

    private async Task SendQueryAsync(string fileHash, string address, CancellationToken token)
    {
        try
        {
            var query = new FileQuery { FileHash = fileHash };
            var json = JsonSerializer.Serialize(query);
            var bytes = Encoding.UTF8.GetBytes(json);
            var endpoint = new IPEndPoint(IPAddress.Parse(address), _config.UdpPort);

            await _udpClient.SendAsync(bytes, bytes.Length, endpoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send query to {Address}", address);
        }
    }

    private async Task WaitForResponsesAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_config.FileTransferDelay, token);
        }
        catch (OperationCanceledException)
        {
        }
    }
}