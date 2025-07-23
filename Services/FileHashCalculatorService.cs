using MultiSourceContentDelivery.Models;
using System.Security.Cryptography;

namespace MultiSourceContentDelivery.Services;

public class FileHashCalculatorService
{
    private readonly ILogger<DnsResolutionService> _logger;
    public FileHashCalculatorService(ILogger<DnsResolutionService> logger)
    {
        _logger = logger;
    }
    public static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true
        );

        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<string> ComputeMD5Async(string filePath)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920, 
            useAsync: true
        );
        var hash = await MD5.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
