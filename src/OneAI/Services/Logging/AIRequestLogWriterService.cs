using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using OneAI.Data;
using OneAI.Entities;

namespace OneAI.Services.Logging;

/// <summary>
/// AI请求日志写入服务（消费者）- 后台服务，从 Channel 读取日志并批量写入数据库
/// </summary>
public class AIRequestLogWriterService : BackgroundService
{
    private readonly Channel<LogQueueItem> _logChannel;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AIRequestLogWriterService> _logger;

    // 临时ID到真实数据库ID的映射
    private readonly ConcurrentDictionary<long, long> _tempIdToRealIdMap = new();

    // 批量写入配置
    private const int BatchSize = 50; // 批量大小
    private const int FlushIntervalMs = 1000; // 强制刷新间隔（毫秒）

    public AIRequestLogWriterService(
        Channel<LogQueueItem> logChannel,
        IServiceProvider serviceProvider,
        ILogger<AIRequestLogWriterService> logger)
    {
        _logChannel = logChannel;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("日志写入后台服务已启动");

        try
        {
            await ProcessLogQueue(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "日志写入后台服务发生致命错误");
            throw;
        }
        finally
        {
            _logger.LogInformation("日志写入后台服务已停止");
        }
    }

    /// <summary>
    /// 处理日志队列
    /// </summary>
    private async Task ProcessLogQueue(CancellationToken stoppingToken)
    {
        var batch = new List<LogQueueItem>();
        var lastFlushTime = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 尝试读取一个日志项（带超时）
                var hasItem = await _logChannel.Reader.WaitToReadAsync(stoppingToken);

                if (hasItem)
                {
                    while (batch.Count < BatchSize && _logChannel.Reader.TryRead(out var item))
                    {
                        batch.Add(item);
                    }
                }

                // 检查是否需要刷新（达到批量大小或超时）
                var shouldFlush = batch.Count >= BatchSize ||
                                  (batch.Count > 0 && (DateTime.UtcNow - lastFlushTime).TotalMilliseconds >= FlushIntervalMs);

                if (shouldFlush)
                {
                    await FlushBatch(batch, stoppingToken);
                    batch.Clear();
                    lastFlushTime = DateTime.UtcNow;
                }
                else if (batch.Count == 0)
                {
                    // 如果队列为空，等待一小段时间
                    await Task.Delay(100, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常关闭
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理日志队列时发生错误");
                await Task.Delay(1000, stoppingToken); // 错误后等待1秒再继续
            }
        }

        // 服务停止时，处理剩余的日志
        if (batch.Count > 0)
        {
            _logger.LogInformation("正在写入剩余的 {Count} 条日志...", batch.Count);
            await FlushBatch(batch, CancellationToken.None);
        }
    }

    /// <summary>
    /// 批量写入日志到数据库
    /// </summary>
    private async Task FlushBatch(List<LogQueueItem> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LogDbContext>();

        try
        {
            // 分组处理不同类型的操作
            var createItems = batch.Where(x => x.OperationType == LogOperationType.Create).ToList();
            var updateItems = batch.Where(x => x.OperationType != LogOperationType.Create).ToList();

            // 1. 批量创建
            if (createItems.Count > 0)
            {
                await BatchCreate(dbContext, createItems, cancellationToken);
            }

            // 2. 批量更新
            if (updateItems.Count > 0)
            {
                await BatchUpdate(dbContext, updateItems, cancellationToken);
            }

            sw.Stop();
            _logger.LogDebug(
                "批量写入 {Count} 条日志完成，耗时 {Duration}ms (创建: {Create}, 更新: {Update})",
                batch.Count, sw.ElapsedMilliseconds, createItems.Count, updateItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量写入日志失败 (批量大小: {Count})", batch.Count);
            // 写入失败不影响主业务，记录错误后继续
        }
    }

    /// <summary>
    /// 批量创建日志
    /// </summary>
    private async Task BatchCreate(LogDbContext dbContext, List<LogQueueItem> createItems, CancellationToken cancellationToken)
    {
        foreach (var item in createItems)
        {
            if (item.Log == null) continue;

            dbContext.AIRequestLogs.Add(item.Log);

            // 保存后获取真实ID
            await dbContext.SaveChangesAsync(cancellationToken);

            // 映射临时ID到真实ID
            if (item.UpdateData?.TryGetValue("TempLogId", out var tempIdObj) == true && tempIdObj is long tempId)
            {
                _tempIdToRealIdMap[tempId] = item.Log.Id;

                _logger.LogDebug("日志创建成功 [TempId={TempId}, RealId={RealId}, RequestId={RequestId}]",
                    tempId, item.Log.Id, item.Log.RequestId);
            }
        }
    }

    /// <summary>
    /// 批量更新日志 - 使用原子性操作，避免实体跟踪
    /// </summary>
    private async Task BatchUpdate(LogDbContext dbContext, List<LogQueueItem> updateItems, CancellationToken cancellationToken)
    {
        // 按操作类型分组，批量执行原子更新
        var updateGroups = updateItems
            .Where(x => x.LogId.HasValue && _tempIdToRealIdMap.ContainsKey(x.LogId.Value))
            .GroupBy(x => x.OperationType)
            .ToList();

        foreach (var group in updateGroups)
        {
            try
            {
                switch (group.Key)
                {
                    case LogOperationType.UpdateRetry:
                        await BatchUpdateRetry(dbContext, group.ToList(), cancellationToken);
                        break;

                    case LogOperationType.RecordSuccess:
                        await BatchUpdateSuccess(dbContext, group.ToList(), cancellationToken);
                        break;

                    case LogOperationType.RecordFailure:
                        await BatchUpdateFailure(dbContext, group.ToList(), cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新失败 [操作类型: {Type}, 数量: {Count}]",
                    group.Key, group.Count());
            }
        }
    }

    /// <summary>
    /// 批量更新重试信息 - 原子操作
    /// </summary>
    private async Task BatchUpdateRetry(LogDbContext dbContext, List<LogQueueItem> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (!item.LogId.HasValue || !_tempIdToRealIdMap.TryGetValue(item.LogId.Value, out var realId))
                continue;

            var data = item.UpdateData;
            if (data == null) continue;

            var retryCount = (int?)data.GetValueOrDefault("RetryCount") ?? 0;
            var totalAttempts = (int?)data.GetValueOrDefault("TotalAttempts") ?? 0;
            var accountId = data.ContainsKey("AccountId") ? (int?)data["AccountId"] : null;
            var updatedAt = (DateTime?)data.GetValueOrDefault("UpdatedAt") ?? DateTime.UtcNow;

            var setter = dbContext.AIRequestLogs
                .Where(x => x.Id == realId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RetryCount, retryCount)
                    .SetProperty(x => x.TotalAttempts, totalAttempts)
                    .SetProperty(x => x.UpdatedAt, updatedAt),
                    cancellationToken);

            await setter;

            // 如果有 AccountId，单独更新（因为可能为 null）
            if (accountId.HasValue)
            {
                await dbContext.AIRequestLogs
                    .Where(x => x.Id == realId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.AccountId, accountId.Value), cancellationToken);
            }
        }
    }

    /// <summary>
    /// 批量更新成功信息 - 原子操作
    /// </summary>
    private async Task BatchUpdateSuccess(LogDbContext dbContext, List<LogQueueItem> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (!item.LogId.HasValue || !_tempIdToRealIdMap.TryGetValue(item.LogId.Value, out var realId))
                continue;

            var data = item.UpdateData;
            if (data == null) continue;

            var statusCode = (int?)data.GetValueOrDefault("StatusCode");
            var requestEndTime = (DateTime?)data.GetValueOrDefault("RequestEndTime");
            var durationMs = (long?)data.GetValueOrDefault("DurationMs");
            var timeToFirstByteMs = (long?)data.GetValueOrDefault("TimeToFirstByteMs");
            var quotaInfo = (string?)data.GetValueOrDefault("QuotaInfo");
            var promptTokens = (int?)data.GetValueOrDefault("PromptTokens");
            var completionTokens = (int?)data.GetValueOrDefault("CompletionTokens");
            var totalTokens = (int?)data.GetValueOrDefault("TotalTokens");
            var cacheTokens = (int?)data.GetValueOrDefault("CacheTokens");
            var createCacheTokens = (int?)data.GetValueOrDefault("CreateCacheTokens");
            var updatedAt = (DateTime?)data.GetValueOrDefault("UpdatedAt") ?? DateTime.UtcNow;

            await dbContext.AIRequestLogs
                .Where(x => x.Id == realId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.IsSuccess, true)
                    .SetProperty(x => x.StatusCode, statusCode)
                    .SetProperty(x => x.RequestEndTime, requestEndTime)
                    .SetProperty(x => x.DurationMs, durationMs)
                    .SetProperty(x => x.TimeToFirstByteMs, timeToFirstByteMs)
                    .SetProperty(x => x.QuotaInfo, quotaInfo)
                    .SetProperty(x => x.PromptTokens, promptTokens)
                    .SetProperty(x => x.CompletionTokens, completionTokens)
                    .SetProperty(x => x.TotalTokens, totalTokens)
                    .SetProperty(x => x.CacheTokens, cacheTokens)
                    .SetProperty(x => x.CreateCacheTokens, createCacheTokens)
                    .SetProperty(x => x.UpdatedAt, updatedAt),
                    cancellationToken);
        }
    }

    /// <summary>
    /// 批量更新失败信息 - 原子操作
    /// </summary>
    private async Task BatchUpdateFailure(LogDbContext dbContext, List<LogQueueItem> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (!item.LogId.HasValue || !_tempIdToRealIdMap.TryGetValue(item.LogId.Value, out var realId))
                continue;

            var data = item.UpdateData;
            if (data == null) continue;

            var statusCode = (int?)data.GetValueOrDefault("StatusCode");
            var errorMessage = (string?)data.GetValueOrDefault("ErrorMessage");
            var requestEndTime = (DateTime?)data.GetValueOrDefault("RequestEndTime");
            var durationMs = (long?)data.GetValueOrDefault("DurationMs");
            var isRateLimited = (bool?)data.GetValueOrDefault("IsRateLimited") ?? false;
            var rateLimitResetSeconds = (int?)data.GetValueOrDefault("RateLimitResetSeconds");
            var quotaInfo = (string?)data.GetValueOrDefault("QuotaInfo");
            var updatedAt = (DateTime?)data.GetValueOrDefault("UpdatedAt") ?? DateTime.UtcNow;

            await dbContext.AIRequestLogs
                .Where(x => x.Id == realId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.IsSuccess, false)
                    .SetProperty(x => x.StatusCode, statusCode)
                    .SetProperty(x => x.ErrorMessage, errorMessage)
                    .SetProperty(x => x.RequestEndTime, requestEndTime)
                    .SetProperty(x => x.DurationMs, durationMs)
                    .SetProperty(x => x.IsRateLimited, isRateLimited)
                    .SetProperty(x => x.RateLimitResetSeconds, rateLimitResetSeconds)
                    .SetProperty(x => x.QuotaInfo, quotaInfo)
                    .SetProperty(x => x.UpdatedAt, updatedAt),
                    cancellationToken);
        }
    }


    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在停止日志写入服务...");

        // 等待队列处理完成
        _logChannel.Writer.Complete();

        await base.StopAsync(cancellationToken);

        _logger.LogInformation("日志写入服务已停止，ID映射表大小: {Size}", _tempIdToRealIdMap.Count);
    }
}
