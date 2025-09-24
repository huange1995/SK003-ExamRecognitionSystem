using ExamRecognitionSystem.Models;
using ExamRecognitionSystem.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;
using TaskStatus = ExamRecognitionSystem.Models.TaskStatus;

namespace ExamRecognitionSystem.Services;

/// <summary>
/// 线程池管理接口
/// </summary>
public interface IThreadPoolManager
{
    Task<string> CreateProcessingSessionAsync(string filePath, FileType fileType, ThreadPoolConfig? config = null);
    Task<bool> StartProcessingAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<bool> CancelProcessingAsync(string sessionId);
    Task<ProcessingSession?> GetSessionAsync(string sessionId);
    Task<List<ProcessingSession>> GetActiveSessionsAsync();
    Task<bool> CleanupCompletedSessionsAsync(TimeSpan? olderThan = null);
    event EventHandler<ProcessingProgressEventArgs>? ProgressUpdated;
    event EventHandler<ProcessingCompletedEventArgs>? ProcessingCompleted;
}

/// <summary>
/// 具有动态线程分配的高性能多线程管理器
/// </summary>
public class ThreadPoolManager : IThreadPoolManager, IDisposable
{
    private readonly ILogger<ThreadPoolManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, ProcessingSession> _sessions;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens;
    private readonly SemaphoreSlim _processingLimitSemaphore;
    private readonly Timer _cleanupTimer;
    private readonly ThreadPoolConfig _defaultConfig;
    private bool _disposed;

    public event EventHandler<ProcessingProgressEventArgs>? ProgressUpdated;
    public event EventHandler<ProcessingCompletedEventArgs>? ProcessingCompleted;

    public ThreadPoolManager(
        ILogger<ThreadPoolManager> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _sessions = new ConcurrentDictionary<string, ProcessingSession>();
        _cancellationTokens = new ConcurrentDictionary<string, CancellationTokenSource>();
        
        // 配置默认线程池设置
        _defaultConfig = configuration.GetSection("Threading").Get<ThreadPoolConfig>() ?? new ThreadPoolConfig();
        _processingLimitSemaphore = new SemaphoreSlim(_defaultConfig.MaxConcurrentThreads, _defaultConfig.MaxConcurrentThreads);
        
        // 设置清理定时器每5分钟运行一次
        _cleanupTimer = new Timer(async _ => await CleanupCompletedSessionsAsync(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        
        _logger.LogInformation("线程池管理器已初始化，最大并发线程数：{MaxThreads}，每线程处理题目数：{QuestionsPerThread}",
            _defaultConfig.MaxConcurrentThreads, _defaultConfig.QuestionsPerThread);
    }

    public async Task<string> CreateProcessingSessionAsync(string filePath, FileType fileType, ThreadPoolConfig? config = null)
    {
        var session = new ProcessingSession
        {
            SessionId = Guid.NewGuid().ToString(),
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            FileType = fileType,
            FileSizeBytes = new FileInfo(filePath).Length,
            Status = SessionStatus.Created,
            CreatedAt = DateTime.UtcNow
        };

        // 使用提供的配置或默认配置
        var threadConfig = config ?? _defaultConfig;

        // 估算总题目数（这将在初始分析后进行优化）
        session.TotalQuestions = 2;// await EstimateQuestionCountAsync(filePath, fileType);
        
        // 根据线程配置创建处理任务
        session.Tasks = CreateProcessingTasks(session.TotalQuestions, threadConfig);
        
        _sessions.TryAdd(session.SessionId, session);
        
        _logger.LogInformation("已为文件 {FileName} 创建处理会话 {SessionId}，包含 {TaskCount} 个任务",
            session.SessionId, session.FileName, session.Tasks.Count);
        
        return session.SessionId;
    }

    public async Task<bool> StartProcessingAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogWarning("未找到会话 {SessionId}", sessionId);
            return false;
        }

        if (session.Status != SessionStatus.Created && session.Status != SessionStatus.Uploaded)
        {
            _logger.LogWarning("会话 {SessionId} 状态无效，无法开始处理。当前状态：{Status}",
                sessionId, session.Status);
            return false;
        }

        try
        {
            // 为此会话创建取消令牌源
            var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationTokens.TryAdd(sessionId, sessionCts);

            session.Status = SessionStatus.Processing;
            session.StartedAt = DateTime.UtcNow;
            session.Metrics = new PerformanceMetrics();

            _logger.LogInformation("开始处理会话 {SessionId}，包含 {TaskCount} 个任务",
                sessionId, session.Tasks.Count);

            // 并发启动处理任务
            var processingTasks = session.Tasks.Select(task => 
                ProcessTaskAsync(session, task, sessionCts.Token)).ToArray();

            // 启动监控任务
            _ = Task.Run(async () => await MonitorProcessingAsync(session, sessionCts.Token), sessionCts.Token);

            // 等待所有任务完成
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.WhenAll(processingTasks);
                    await CompleteProcessingAsync(session);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "会话 {SessionId} 处理完成时发生错误", sessionId);
                    session.Status = SessionStatus.Failed;
                    session.ErrorMessage = ex.Message;
                }
                finally
                {
                    _cancellationTokens.TryRemove(sessionId, out _);
                    sessionCts.Dispose();
                }
            }, sessionCts.Token);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动会话 {SessionId} 处理时发生错误：{Error}", sessionId, ex.Message);
            session.Status = SessionStatus.Failed;
            session.ErrorMessage = ex.Message;
            return false;
        }
    }

    public async Task<bool> CancelProcessingAsync(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        if (_cancellationTokens.TryRemove(sessionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        session.Status = SessionStatus.Cancelled;
        session.CompletedAt = DateTime.UtcNow;

        // 取消所有待处理任务
        foreach (var task in session.Tasks.Where(t => t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
        {
            task.Status = TaskStatus.Cancelled;
        }

        _logger.LogInformation("已取消会话 {SessionId} 的处理", sessionId);
        return await Task.FromResult(true);
    }

    public async Task<ProcessingSession?> GetSessionAsync(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return await Task.FromResult(session);
    }

    public async Task<List<ProcessingSession>> GetActiveSessionsAsync()
    {
        var activeSessions = _sessions.Values
            .Where(s => s.Status == SessionStatus.Processing || s.Status == SessionStatus.Created || s.Status == SessionStatus.Uploaded)
            .ToList();
        
        return await Task.FromResult(activeSessions);
    }

    public async Task<bool> CleanupCompletedSessionsAsync(TimeSpan? olderThan = null)
    {
        var cutoffTime = DateTime.UtcNow - (olderThan ?? TimeSpan.FromHours(24));
        var sessionsToRemove = _sessions.Values
            .Where(s => (s.Status == SessionStatus.Completed || s.Status == SessionStatus.Failed || s.Status == SessionStatus.Cancelled) 
                        && s.CompletedAt.HasValue && s.CompletedAt.Value < cutoffTime)
            .ToList();

        foreach (var session in sessionsToRemove)
        {
            _sessions.TryRemove(session.SessionId, out _);
            
            // 清理临时文件
            try
            {
                if (File.Exists(session.FilePath))
                {
                    File.Delete(session.FilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "删除会话 {SessionId} 的临时文件 {FilePath} 失败",
                    session.FilePath, session.SessionId);
            }
        }

        if (sessionsToRemove.Count > 0)
        {
            _logger.LogInformation("已清理 {Count} 个已完成的会话", sessionsToRemove.Count);
        }

        return await Task.FromResult(true);
    }

    private async Task<int> EstimateQuestionCountAsync(string filePath, FileType fileType)
    {
        // 简单的估算逻辑 - 在实际实现中，这可以更加复杂
        var fileInfo = new FileInfo(filePath);
        var estimatedQuestions = fileType switch
        {
            FileType.Pdf => Math.Max(1, (int)(fileInfo.Length / (50 * 1024))), // 估算每50KB一个问题
            FileType.Docx => Math.Max(1, (int)(fileInfo.Length / (30 * 1024))), // 估算每30KB一个问题
            FileType.Jpeg or FileType.Png => Math.Max(1, (int)(fileInfo.Length / (100 * 1024))), // 估算每100KB一个问题
            _ => 10 // 默认估算
        };

        _logger.LogDebug("估算文件 {FileName} 包含 {QuestionCount} 个题目（{FileSize} 字节）",
            estimatedQuestions, Path.GetFileName(filePath), fileInfo.Length);

        return await Task.FromResult(Math.Min(estimatedQuestions, 100)); // 为安全起见，最多100个问题
    }

    private List<ProcessingTask> CreateProcessingTasks(int totalQuestions, ThreadPoolConfig config)
    {
        var tasks = new List<ProcessingTask>();
        var questionsPerThread = config.QuestionsPerThread;
        var maxThreads = CalculateOptimalThreadCount(totalQuestions, config);

        for (int i = 0; i < totalQuestions; i += questionsPerThread)
        {
            var startQuestion = i + 1;
            var endQuestion = Math.Min(i + questionsPerThread, totalQuestions);
            var threadId = (i / questionsPerThread) % maxThreads;

            tasks.Add(new ProcessingTask
            {
                TaskId = Guid.NewGuid().ToString(),
                StartQuestionNumber = startQuestion,
                EndQuestionNumber = endQuestion,
                ThreadId = threadId,
                Status = TaskStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });
        }

        _logger.LogDebug("为 {QuestionCount} 个题目创建了 {TaskCount} 个处理任务，使用 {ThreadCount} 个线程",
            tasks.Count, totalQuestions, maxThreads);

        return tasks;
    }

    private int CalculateOptimalThreadCount(int totalQuestions, ThreadPoolConfig config)
    {
        if (!config.EnableAdaptiveThreading)
        {
            return config.MaxConcurrentThreads;
        }

        // 基于工作负载和系统资源的自适应线程
        var cpuCount = Environment.ProcessorCount;
        var memoryUsageMB = PerformanceExtensions.GetMemoryUsage() / (1024 * 1024);
        
        // 基于以下因素计算最优线程数：
        // 1. 问题数量
        // 2. 可用CPU核心数
        // 3. 当前内存使用情况
        var taskCount = (int)Math.Ceiling((double)totalQuestions / config.QuestionsPerThread);
        var cpuBasedThreads = Math.Max(1, cpuCount - 1); // 为系统保留一个核心
        var memoryBasedThreads = memoryUsageMB < 512 ? cpuCount : Math.Max(1, cpuCount / 2);
        
        var optimalThreads = Math.Min(
            Math.Min(taskCount, cpuBasedThreads),
            Math.Min(memoryBasedThreads, config.MaxConcurrentThreads)
        );

        _logger.LogDebug("计算出最优线程数：{OptimalThreads}（CPU：{CpuThreads}，内存：{MemoryThreads}，任务：{TaskCount}）",
            optimalThreads, cpuBasedThreads, memoryBasedThreads, taskCount);

        return optimalThreads;
    }

    private async Task ProcessTaskAsync(ProcessingSession session, ProcessingTask task, CancellationToken cancellationToken)
    {
        await _processingLimitSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            task.Status = TaskStatus.InProgress;
            task.StartedAt = DateTime.UtcNow;

            _logger.LogDebug("在线程 {ThreadId} 上开始处理任务 {TaskId}，题目范围：{StartQuestion}-{EndQuestion}",
                task.TaskId, task.StartQuestionNumber, task.EndQuestionNumber, task.ThreadId);

            // 获取问题解析服务
            using var scope = _serviceProvider.CreateScope();
            var questionParsingService = scope.ServiceProvider.GetRequiredService<IQuestionParsingService>();

            // 处理此任务的问题
            var questions = await questionParsingService.ParseQuestionsAsync(
                session.FilePath, 
                Enumerable.Range(task.StartQuestionNumber, task.EndQuestionNumber - task.StartQuestionNumber + 1).ToList(),
                cancellationToken);

            task.Questions = questions;
            task.Status = TaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            task.Progress = 100;

            // 更新会话进度
            UpdateSessionProgress(session);

            _logger.LogDebug("已完成处理任务 {TaskId}，包含 {QuestionCount} 个题目",
                task.TaskId, questions.Count);
        }
        catch (OperationCanceledException)
        {
            task.Status = TaskStatus.Cancelled;
            _logger.LogInformation("任务 {TaskId} 已被取消", task.TaskId);
        }
        catch (Exception ex)
        {
            task.Status = TaskStatus.Failed;
            task.ErrorMessage = ex.Message;
            _logger.LogError(ex, "处理任务 {TaskId} 时发生错误：{Error}", task.TaskId, ex.Message);
        }
        finally
        {
            _processingLimitSemaphore.Release();
        }
    }

    private void UpdateSessionProgress(ProcessingSession session)
    {
        var completedTasks = session.Tasks.Count(t => t.Status == TaskStatus.Completed);
        var totalTasks = session.Tasks.Count;
        
        session.CompletedQuestions = session.Tasks
            .Where(t => t.Status == TaskStatus.Completed)
            .Sum(t => t.Questions.Count);

        // 更新性能指标
        session.Metrics.ActiveThreads = session.Tasks.Count(t => t.Status == TaskStatus.InProgress);
        session.Metrics.CpuUsagePercent = PerformanceExtensions.GetCpuUsage();
        session.Metrics.MemoryUsageBytes = PerformanceExtensions.GetMemoryUsage();
        
        if (session.StartedAt.HasValue)
        {
            session.Metrics.ProcessingDuration = DateTime.UtcNow - session.StartedAt.Value;
            var secondsElapsed = session.Metrics.ProcessingDuration.TotalSeconds;
            session.Metrics.QuestionsPerSecond = secondsElapsed > 0 ? 
                (int)(session.CompletedQuestions / secondsElapsed) : 0;
        }

        // 通知进度更新
        ProgressUpdated?.Invoke(this, new ProcessingProgressEventArgs
        {
            SessionId = session.SessionId,
            CompletedQuestions = session.CompletedQuestions,
            TotalQuestions = session.TotalQuestions,
            CompletedTasks = completedTasks,
            TotalTasks = totalTasks,
            Metrics = session.Metrics
        });
    }

    private async Task CompleteProcessingAsync(ProcessingSession session)
    {
        session.Status = SessionStatus.Completed;
        session.CompletedAt = DateTime.UtcNow;

        // 合并所有已完成任务的问题
        session.AllQuestions = session.Tasks
            .Where(t => t.Status == TaskStatus.Completed)
            .SelectMany(t => t.Questions)
            .OrderBy(q => q.QuestionNumber)
            .ToList();

        session.TotalQuestions = session.AllQuestions.Count;
        session.CompletedQuestions = session.AllQuestions.Count;

        _logger.LogInformation("已完成处理会话 {SessionId}，包含 {QuestionCount} 个题目，耗时 {Duration}",
            session.SessionId, session.AllQuestions.Count, session.Metrics.ProcessingDuration);

        // 通知完成
        ProcessingCompleted?.Invoke(this, new ProcessingCompletedEventArgs
        {
            SessionId = session.SessionId,
            Success = true,
            Questions = session.AllQuestions,
            Metrics = session.Metrics
        });

        await Task.CompletedTask;
    }

    private async Task MonitorProcessingAsync(ProcessingSession session, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && 
               session.Status == SessionStatus.Processing)
        {
            UpdateSessionProgress(session);
            
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cleanupTimer?.Dispose();
        _processingLimitSemaphore?.Dispose();
        
        foreach (var cts in _cancellationTokens.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        
        _disposed = true;
    }
}

/// <summary>
/// 处理进度更新的事件参数
/// </summary>
public class ProcessingProgressEventArgs : EventArgs
{
    public string SessionId { get; set; } = string.Empty;
    public int CompletedQuestions { get; set; }
    public int TotalQuestions { get; set; }
    public int CompletedTasks { get; set; }
    public int TotalTasks { get; set; }
    public PerformanceMetrics Metrics { get; set; } = new();
}

/// <summary>
/// 处理完成的事件参数
/// </summary>
public class ProcessingCompletedEventArgs : EventArgs
{
    public string SessionId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public List<ExamQuestion> Questions { get; set; } = new();
    public PerformanceMetrics Metrics { get; set; } = new();
    public string? ErrorMessage { get; set; }
}