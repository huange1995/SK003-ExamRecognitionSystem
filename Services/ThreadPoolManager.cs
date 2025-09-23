using ExamRecognitionSystem.Models;
using ExamRecognitionSystem.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;
using TaskStatus = ExamRecognitionSystem.Models.TaskStatus;

namespace ExamRecognitionSystem.Services;

/// <summary>
/// Interface for thread pool management
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
/// High-performance multi-threading manager with dynamic thread allocation
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
        
        // Configure default thread pool settings
        _defaultConfig = configuration.GetSection("Threading").Get<ThreadPoolConfig>() ?? new ThreadPoolConfig();
        _processingLimitSemaphore = new SemaphoreSlim(_defaultConfig.MaxConcurrentThreads, _defaultConfig.MaxConcurrentThreads);
        
        // Setup cleanup timer to run every 5 minutes
        _cleanupTimer = new Timer(async _ => await CleanupCompletedSessionsAsync(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        
        _logger.LogInformation("ThreadPoolManager initialized with max {MaxThreads} concurrent threads, {QuestionsPerThread} questions per thread",
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

        // Use provided config or default
        var threadConfig = config ?? _defaultConfig;

        // Estimate total questions (this would be refined after initial analysis)
        session.TotalQuestions =  await EstimateQuestionCountAsync(filePath, fileType);
        
        // Create processing tasks based on thread configuration
        session.Tasks = CreateProcessingTasks(session.TotalQuestions, threadConfig);
        
        _sessions.TryAdd(session.SessionId, session);
        
        _logger.LogInformation("Created processing session {SessionId} for file {FileName} with {TaskCount} tasks",
            session.SessionId, session.FileName, session.Tasks.Count);
        
        return session.SessionId;
    }

    public async Task<bool> StartProcessingAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogWarning("Session {SessionId} not found", sessionId);
            return false;
        }

        if (session.Status != SessionStatus.Created && session.Status != SessionStatus.Uploaded)
        {
            _logger.LogWarning("Session {SessionId} is not in a valid state for processing. Current status: {Status}",
                sessionId, session.Status);
            return false;
        }

        try
        {
            // Create cancellation token source for this session
            var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationTokens.TryAdd(sessionId, sessionCts);

            session.Status = SessionStatus.Processing;
            session.StartedAt = DateTime.UtcNow;
            session.Metrics = new PerformanceMetrics();

            _logger.LogInformation("Starting processing for session {SessionId} with {TaskCount} tasks",
                sessionId, session.Tasks.Count);

            // Start processing tasks concurrently
            var processingTasks = session.Tasks.Select(task => 
                ProcessTaskAsync(session, task, sessionCts.Token)).ToArray();

            // Start monitoring task
            _ = Task.Run(async () => await MonitorProcessingAsync(session, sessionCts.Token), sessionCts.Token);

            // Wait for all tasks to complete
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.WhenAll(processingTasks);
                    await CompleteProcessingAsync(session);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during processing completion for session {SessionId}", sessionId);
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
            _logger.LogError(ex, "Error starting processing for session {SessionId}: {Error}", sessionId, ex.Message);
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

        // Cancel all pending tasks
        foreach (var task in session.Tasks.Where(t => t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
        {
            task.Status = TaskStatus.Cancelled;
        }

        _logger.LogInformation("Cancelled processing for session {SessionId}", sessionId);
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
            
            // Clean up temporary files
            try
            {
                if (File.Exists(session.FilePath))
                {
                    File.Delete(session.FilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temporary file {FilePath} for session {SessionId}",
                    session.FilePath, session.SessionId);
            }
        }

        if (sessionsToRemove.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} completed sessions", sessionsToRemove.Count);
        }

        return await Task.FromResult(true);
    }

    private async Task<int> EstimateQuestionCountAsync(string filePath, FileType fileType)
    {
        // Simple estimation logic - in a real implementation, this could be more sophisticated
        var fileInfo = new FileInfo(filePath);
        var estimatedQuestions = fileType switch
        {
            FileType.Pdf => Math.Max(1, (int)(fileInfo.Length / (50 * 1024))), // Estimate 1 question per 50KB
            FileType.Docx => Math.Max(1, (int)(fileInfo.Length / (30 * 1024))), // Estimate 1 question per 30KB
            FileType.Jpeg or FileType.Png => Math.Max(1, (int)(fileInfo.Length / (100 * 1024))), // Estimate 1 question per 100KB
            _ => 10 // Default estimate
        };

        _logger.LogDebug("Estimated {QuestionCount} questions for file {FileName} ({FileSize} bytes)",
            estimatedQuestions, Path.GetFileName(filePath), fileInfo.Length);

        return await Task.FromResult(Math.Min(estimatedQuestions, 100)); // Cap at 100 questions for safety
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

        _logger.LogDebug("Created {TaskCount} processing tasks for {QuestionCount} questions with {ThreadCount} threads",
            tasks.Count, totalQuestions, maxThreads);

        return tasks;
    }

    private int CalculateOptimalThreadCount(int totalQuestions, ThreadPoolConfig config)
    {
        if (!config.EnableAdaptiveThreading)
        {
            return config.MaxConcurrentThreads;
        }

        // Adaptive threading based on workload and system resources
        var cpuCount = Environment.ProcessorCount;
        var memoryUsageMB = PerformanceExtensions.GetMemoryUsage() / (1024 * 1024);
        
        // Calculate optimal thread count based on:
        // 1. Number of questions
        // 2. Available CPU cores
        // 3. Current memory usage
        var taskCount = (int)Math.Ceiling((double)totalQuestions / config.QuestionsPerThread);
        var cpuBasedThreads = Math.Max(1, cpuCount - 1); // Leave one core for system
        var memoryBasedThreads = memoryUsageMB < 512 ? cpuCount : Math.Max(1, cpuCount / 2);
        
        var optimalThreads = Math.Min(
            Math.Min(taskCount, cpuBasedThreads),
            Math.Min(memoryBasedThreads, config.MaxConcurrentThreads)
        );

        _logger.LogDebug("Calculated optimal thread count: {OptimalThreads} (CPU: {CpuThreads}, Memory: {MemoryThreads}, Tasks: {TaskCount})",
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

            _logger.LogDebug("Starting processing task {TaskId} for questions {StartQuestion}-{EndQuestion} on thread {ThreadId}",
                task.TaskId, task.StartQuestionNumber, task.EndQuestionNumber, task.ThreadId);

            // Get question parsing service
            using var scope = _serviceProvider.CreateScope();
            var questionParsingService = scope.ServiceProvider.GetRequiredService<IQuestionParsingService>();

            // Process questions for this task
            var questions = await questionParsingService.ParseQuestionsAsync(
                session.FilePath, 
                Enumerable.Range(task.StartQuestionNumber, task.EndQuestionNumber - task.StartQuestionNumber + 1).ToList(),
                cancellationToken);

            task.Questions = questions;
            task.Status = TaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            task.Progress = 100;

            // Update session progress
            UpdateSessionProgress(session);

            _logger.LogDebug("Completed processing task {TaskId} with {QuestionCount} questions",
                task.TaskId, questions.Count);
        }
        catch (OperationCanceledException)
        {
            task.Status = TaskStatus.Cancelled;
            _logger.LogInformation("Task {TaskId} was cancelled", task.TaskId);
        }
        catch (Exception ex)
        {
            task.Status = TaskStatus.Failed;
            task.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error processing task {TaskId}: {Error}", task.TaskId, ex.Message);
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

        // Update performance metrics
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

        // Notify progress update
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

        // Merge all questions from completed tasks
        session.AllQuestions = session.Tasks
            .Where(t => t.Status == TaskStatus.Completed)
            .SelectMany(t => t.Questions)
            .OrderBy(q => q.QuestionNumber)
            .ToList();

        session.TotalQuestions = session.AllQuestions.Count;
        session.CompletedQuestions = session.AllQuestions.Count;

        _logger.LogInformation("Completed processing session {SessionId} with {QuestionCount} questions in {Duration}",
            session.SessionId, session.AllQuestions.Count, session.Metrics.ProcessingDuration);

        // Notify completion
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
/// Event arguments for processing progress updates
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
/// Event arguments for processing completion
/// </summary>
public class ProcessingCompletedEventArgs : EventArgs
{
    public string SessionId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public List<ExamQuestion> Questions { get; set; } = new();
    public PerformanceMetrics Metrics { get; set; } = new();
    public string? ErrorMessage { get; set; }
}