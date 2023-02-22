using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Fhir.Loader.Tool;

public class BlobStreamReader
{
    private readonly string _blobListFileName;
    private readonly int _readBlobConcurrency;
    private readonly ILogger<BlobStreamReader> _logger;
    private readonly TokenCredential _tokenCredential;

    public BlobStreamReader(
        IOptions<UploadConfiguration> config,
        ILogger<BlobStreamReader> logger
        )
    {
        _blobListFileName = config.Value.BlobListFile;
        _readBlobConcurrency = config.Value.ReadBlobConcurrency;
        _logger = logger;

        if (!string.IsNullOrEmpty(config.Value.TenantId)
            && !string.IsNullOrEmpty(config.Value.ClientSecret)
            && !string.IsNullOrEmpty(config.Value.ClientId))
        {
            _logger.LogInformation("Using client secret credential.");
            _tokenCredential = new ClientSecretCredential(config.Value.TenantId, config.Value.ClientId, config.Value.ClientSecret);
        }
        else
        {
            _logger.LogInformation("Using default azure credential.");
            _tokenCredential = new DefaultAzureCredential();
        }
    }
    public async Task ReadAsync(ChannelWriter<ResourceItem> writer, CancellationTokenSource cancellationTokenSource)
    {
        List<string> blobUrls = LoadBlobFileList(_blobListFileName);
        _logger.LogInformation($"Loaded {blobUrls.Count()} blob urls from {_blobListFileName}.");

        List<Task> tasks = new List<Task>();
        int i = 0;
        foreach (var blobUrl in blobUrls)
        {
            i++;
            if (tasks.Count >= _readBlobConcurrency)
            {
                var finishedTask = await Task.WhenAny(tasks);
                await finishedTask;
                tasks.Remove(finishedTask);
            }

            string progress = $"{i}/{blobUrls.Count}";
            tasks.Add(Task.Run(() => ReadSingleBlobAsync(blobUrl, writer, progress, cancellationTokenSource)));
        }

        await Task.WhenAll(tasks);

        writer.Complete();
    }

    private async Task ReadSingleBlobAsync(string blobUrl, ChannelWriter<ResourceItem> writer, string progress, CancellationTokenSource cancellationTokenSource = default)
    {
        _logger.LogInformation($"Read blob {progress} from {blobUrl}");
        if (cancellationTokenSource.IsCancellationRequested)
        {
            throw new OperationCanceledException($"Blob reader {progress} canceled.");
        };
        int index = 1;

        try
        {
            var blobClient = new BlobClient(new Uri(blobUrl), _tokenCredential);
            using var stream = await blobClient.OpenReadAsync(0, bufferSize: 40960, cancellationToken: cancellationTokenSource.Token);
            using var streamReader = new StreamReader(stream);
            string line;
            do
            {
                line = await streamReader.ReadLineAsync();
                if (!string.IsNullOrEmpty(line))
                {
                    if (await writer.WaitToWriteAsync())
                    {
                        MatchCollection matches = Regex.Matches(line, "[\"resourceType\"]:\"([A-Za-z]+)\",\"id\":\"([a-z0-9-]+)\"");
                        var item = new ResourceItem { Resource = line, BlobName = blobUrl, Index = index, ResourceType = matches[0].Groups[1].Value, Id = matches[0].Groups[2].Value };
                        await writer.WriteAsync(item);
                    }
                    index++;

                    if (index % 1000 == 0)
                    {
                        _logger.LogInformation($"Load {index} resources from {progress} {blobUrl}");
                    }
                }
            }
            while (line != null);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Read blob {progress} {blobUrl} failed: {ex}");
            cancellationTokenSource.Cancel();
            throw;
        }

        _logger.LogInformation($"Completed reading {index - 1} resources from {progress} {blobUrl}");
    }

    private List<string> LoadBlobFileList(string fileName)
    {
        return File.ReadAllLines(fileName).Where(line => !string.IsNullOrEmpty(line)).ToList();
    }
}
