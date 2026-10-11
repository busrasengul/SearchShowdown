using System.Collections.Concurrent;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SearchShowdown.AzureSearch.Configuration;

namespace SearchShowdown.AzureSearch.Services;

internal sealed class AzureSearchBatchWriter : IAsyncDisposable
{
    private readonly IAzureSearchIndexManager _indexManager;
    private readonly AzureSearchOptions _options;
    private readonly ILogger<AzureSearchBatchWriter> _logger;
    private readonly ConcurrentDictionary<string, IndexBuffer> _buffers = new();

    public AzureSearchBatchWriter(
        IAzureSearchIndexManager indexManager,
        IOptions<AzureSearchOptions> options,
        ILogger<AzureSearchBatchWriter> logger)
    {
        _indexManager = indexManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnqueueAsync(string indexAlias, IEnumerable<SearchDocument> documents)
    {
        IndexBuffer buffer = Buffer(indexAlias);
        await buffer.Lock.WaitAsync();
        try
        {
            buffer.Pending.AddRange(documents);
            if (buffer.Pending.Count >= _options.BatchSize)
            {
                await FlushLockedAsync(indexAlias, buffer);
            }
            else
            {
                buffer.Timer.Change(TimeSpan.FromMilliseconds(_options.FlushDelayMilliseconds), Timeout.InfiniteTimeSpan);
            }
        }
        finally
        {
            buffer.Lock.Release();
        }
    }

    public async Task FlushAsync(string indexAlias)
    {
        IndexBuffer buffer = Buffer(indexAlias);
        await buffer.Lock.WaitAsync();
        try
        {
            await FlushLockedAsync(indexAlias, buffer);
        }
        finally
        {
            buffer.Lock.Release();
        }
    }

    public void Discard(string indexAlias)
    {
        if (_buffers.TryGetValue(indexAlias, out IndexBuffer? buffer))
        {
            buffer.Lock.Wait();
            buffer.Pending.Clear();
            buffer.Lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var indexAlias in _buffers.Keys)
        {
            try
            {
                await FlushAsync(indexAlias);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not flush pending Azure AI Search documents for {IndexAlias} during shutdown", indexAlias);
            }
        }

        foreach (IndexBuffer buffer in _buffers.Values)
        {
            await buffer.Timer.DisposeAsync();
        }
    }

    private IndexBuffer Buffer(string indexAlias)
        => _buffers.GetOrAdd(indexAlias, alias => new IndexBuffer(new Timer(_ => FlushInBackground(alias))));

    private async void FlushInBackground(string indexAlias)
    {
        try
        {
            await FlushAsync(indexAlias);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not flush pending Azure AI Search documents for {IndexAlias}", indexAlias);
        }
    }

    private async Task FlushLockedAsync(string indexAlias, IndexBuffer buffer)
    {
        buffer.Timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (buffer.Pending.Count is 0)
        {
            return;
        }

        SearchDocument[] batch = buffer.Pending
            .GroupBy(document => document.GetString(AzureFieldNames.Id))
            .Select(group => group.Last())
            .ToArray();
        buffer.Pending.Clear();

        await UploadAsync(_indexManager.GetSearchClient(indexAlias), batch);
    }

    private async Task UploadAsync(SearchClient client, SearchDocument[] documents)
    {
        SearchDocument[] remaining = documents;
        TimeSpan delay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; remaining.Length > 0; attempt++)
        {
            try
            {
                Response<IndexDocumentsResult> response = await client.UploadDocumentsAsync(remaining);
                var failedKeys = response.Value.Results
                    .Where(result => result.Succeeded is false && IsRetryable(result.Status))
                    .Select(result => result.Key)
                    .ToHashSet();

                foreach (IndexingResult failed in response.Value.Results.Where(result => result.Succeeded is false && IsRetryable(result.Status) is false))
                {
                    _logger.LogWarning("Azure AI Search rejected document {Key}: {Message}", failed.Key, failed.ErrorMessage);
                }

                remaining = remaining.Where(document => failedKeys.Contains(document.GetString(AzureFieldNames.Id))).ToArray();
            }
            catch (RequestFailedException ex) when (IsRetryable(ex.Status) && attempt < _options.MaxIndexingAttempts)
            {
                _logger.LogInformation("Azure AI Search is throttling ({Status}); retrying {Count} documents in {Delay}", ex.Status, remaining.Length, delay);
            }

            if (remaining.Length is 0)
            {
                return;
            }

            if (attempt >= _options.MaxIndexingAttempts)
            {
                throw new InvalidOperationException($"Azure AI Search did not accept {remaining.Length} documents after {attempt} attempts.");
            }

            await Task.Delay(delay);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
        }
    }

    private static bool IsRetryable(int status) => status is 429 or 503;

    private sealed record IndexBuffer(Timer Timer)
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public List<SearchDocument> Pending { get; } = [];
    }
}
