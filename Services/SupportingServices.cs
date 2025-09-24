using ExamRecognitionSystem.Models;
using ExamRecognitionSystem.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace ExamRecognitionSystem.Services;

/// <summary>
/// 文件上传操作接口
/// </summary>
public interface IFileUploadService
{
    Task<(bool Success, string SessionId, List<string> Errors)> UploadFileAsync(IFormFile file, CancellationToken cancellationToken = default);
    Task<bool> DeleteFileAsync(string sessionId);
    Task<string?> GetFilePathAsync(string sessionId);
    Task<bool> CleanupExpiredFilesAsync();
}

/// <summary>
/// 进度监控接口
/// </summary>
public interface IProgressMonitoringService
{
    Task<ProcessingSession?> GetSessionStatusAsync(string sessionId);
    Task<List<ProcessingSession>> GetActiveSessionsAsync();
    Task RegisterSessionAsync(ProcessingSession session);
    Task UpdateSessionProgressAsync(string sessionId, int completedQuestions, PerformanceMetrics metrics);
    event EventHandler<ProcessingProgressEventArgs>? ProgressUpdated;
}

/// <summary>
/// 性能监控接口
/// </summary>
public interface IPerformanceMonitoringService
{
    PerformanceMetrics GetCurrentMetrics();
    Task<List<PerformanceMetrics>> GetHistoricalMetricsAsync(TimeSpan period);
    Task LogMetricsAsync(PerformanceMetrics metrics);
    bool IsSystemHealthy();
}

/// <summary>
/// 处理文件上传和验证的服务
/// </summary>
public class FileUploadService : IFileUploadService
{
    private readonly ILogger<FileUploadService> _logger;
    private readonly FileUploadSettings _settings;
    private readonly ConcurrentDictionary<string, FileUploadInfo> _uploadedFiles;

    public FileUploadService(
        ILogger<FileUploadService> logger,
        Microsoft.Extensions.Options.IOptions<FileUploadSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
        _uploadedFiles = new ConcurrentDictionary<string, FileUploadInfo>();

        // 确保临时目录存在
        Directory.CreateDirectory(_settings.TempDirectory);
    }

    public async Task<(bool Success, string SessionId, List<string> Errors)> UploadFileAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var errors = file.ValidateUploadedFile(_settings);

        if (errors.Count > 0)
        {
            _logger.LogWarning("File upload validation failed for session {SessionId}: {Errors}",
                sessionId, string.Join(", ", errors));
            return (false, sessionId, errors);
        }

        try
        {
            // 生成唯一的文件路径
            var fileExtension = Path.GetExtension(file.FileName);
            var fileName = $"{sessionId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{fileExtension}";
            var filePath = Path.Combine(_settings.TempDirectory, fileName);

            // 保存文件
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream, cancellationToken);
            }

            // 存储文件信息
            var fileInfo = new FileUploadInfo
            {
                SessionId = sessionId,
                OriginalFileName = file.FileName,
                FilePath = filePath,
                FileSize = file.Length,
                FileType = file.FileName.GetFileTypeFromExtension(),
                UploadedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(_settings.FileRetentionPeriod)
            };

            _uploadedFiles.TryAdd(sessionId, fileInfo);

            _logger.LogInformation("File uploaded successfully: {FileName} -> {FilePath} (Session: {SessionId})",
                file.FileName, filePath, sessionId);

            return (true, sessionId, new List<string>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName} for session {SessionId}: {Error}",
                file.FileName, sessionId, ex.Message);
            
            errors.Add($"Upload failed: {ex.Message}");
            return (false, sessionId, errors);
        }
    }

    public async Task<bool> DeleteFileAsync(string sessionId)
    {
        if (!_uploadedFiles.TryRemove(sessionId, out var fileInfo))
        {
            return false;
        }

        try
        {
            if (File.Exists(fileInfo.FilePath))
            {
                File.Delete(fileInfo.FilePath);
            }

            _logger.LogInformation("File deleted for session {SessionId}: {FilePath}",
                sessionId, fileInfo.FilePath);
            
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file for session {SessionId}: {Error}",
                sessionId, ex.Message);
            return false;
        }
    }

    public async Task<string?> GetFilePathAsync(string sessionId)
    {
        _uploadedFiles.TryGetValue(sessionId, out var fileInfo);
        return await Task.FromResult(fileInfo?.FilePath);
    }

    public async Task<bool> CleanupExpiredFilesAsync()
    {
        var expiredFiles = _uploadedFiles.Values
            .Where(f => f.ExpiresAt < DateTime.UtcNow)
            .ToList();

        foreach (var fileInfo in expiredFiles)
        {
            await DeleteFileAsync(fileInfo.SessionId);
        }

        if (expiredFiles.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired files", expiredFiles.Count);
        }

        return true;
    }

    private class FileUploadInfo
    {
        public string SessionId { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public FileType FileType { get; set; }
        public DateTime UploadedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}

/// <summary>
/// 监控处理进度的服务
/// </summary>
public class ProgressMonitoringService : IProgressMonitoringService
{
    private readonly ILogger<ProgressMonitoringService> _logger;
    private readonly ConcurrentDictionary<string, ProcessingSession> _activeSessions;

    public event EventHandler<ProcessingProgressEventArgs>? ProgressUpdated;

    public ProgressMonitoringService(ILogger<ProgressMonitoringService> logger)
    {
        _logger = logger;
        _activeSessions = new ConcurrentDictionary<string, ProcessingSession>();
    }

    public async Task<ProcessingSession?> GetSessionStatusAsync(string sessionId)
    {
        _activeSessions.TryGetValue(sessionId, out var session);
        return await Task.FromResult(session);
    }

    public async Task<List<ProcessingSession>> GetActiveSessionsAsync()
    {
        var activeSessions = _activeSessions.Values
            .Where(s => s.Status == SessionStatus.Processing || s.Status == SessionStatus.Created || s.Status == SessionStatus.Uploaded)
            .ToList();
        
        return await Task.FromResult(activeSessions);
    }

    public async Task RegisterSessionAsync(ProcessingSession session)
    {
        _activeSessions.TryAdd(session.SessionId, session);
        _logger.LogInformation("Registered session {SessionId} for monitoring", session.SessionId);
        await Task.CompletedTask;
    }

    public async Task UpdateSessionProgressAsync(string sessionId, int completedQuestions, PerformanceMetrics metrics)
    {
        if (_activeSessions.TryGetValue(sessionId, out var session))
        {
            session.CompletedQuestions = completedQuestions;
            session.Metrics = metrics;

            ProgressUpdated?.Invoke(this, new ProcessingProgressEventArgs
            {
                SessionId = sessionId,
                CompletedQuestions = completedQuestions,
                TotalQuestions = session.TotalQuestions,
                Metrics = metrics
            });

            _logger.LogDebug("Updated progress for session {SessionId}: {Completed}/{Total} questions",
                sessionId, completedQuestions, session.TotalQuestions);
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// 监控系统性能的服务
/// </summary>
public class PerformanceMonitoringService : IPerformanceMonitoringService
{
    private readonly ILogger<PerformanceMonitoringService> _logger;
    private readonly List<PerformanceMetrics> _metricsHistory;
    private readonly object _metricsLock = new object();
    private readonly Timer _metricsTimer;

    public PerformanceMonitoringService(ILogger<PerformanceMonitoringService> logger)
    {
        _logger = logger;
        _metricsHistory = new List<PerformanceMetrics>();
        
        // 每30秒收集一次指标
        _metricsTimer = new Timer(CollectMetrics, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    public PerformanceMetrics GetCurrentMetrics()
    {
        return new PerformanceMetrics
        {
            CpuUsagePercent = PerformanceExtensions.GetCpuUsage(),
            MemoryUsageBytes = PerformanceExtensions.GetMemoryUsage(),
            ActiveThreads = PerformanceExtensions.GetActiveThreadCount(),
            ProcessingDuration = TimeSpan.Zero
        };
    }

    public async Task<List<PerformanceMetrics>> GetHistoricalMetricsAsync(TimeSpan period)
    {
        var cutoffTime = DateTime.UtcNow - period;
        
        lock (_metricsLock)
        {
            var historicalMetrics = _metricsHistory
                .Where(m => m.ProcessingDuration >= TimeSpan.Zero) // 过滤有效指标
                .TakeLast(100) // 保留最后100条记录
                .ToList();
            
            return historicalMetrics;
        }
    }

    public async Task LogMetricsAsync(PerformanceMetrics metrics)
    {
        lock (_metricsLock)
        {
            _metricsHistory.Add(metrics);
            
            // 只保留最近的指标（最后1000条记录）
            if (_metricsHistory.Count > 1000)
            {
                _metricsHistory.RemoveAt(0);
            }
        }

        _logger.LogDebug("Logged performance metrics: CPU {Cpu}%, Memory {Memory}MB, Threads {Threads}",
            metrics.CpuUsagePercent, metrics.MemoryUsageBytes / (1024 * 1024), metrics.ActiveThreads);

        await Task.CompletedTask;
    }

    public bool IsSystemHealthy()
    {
        var currentMetrics = GetCurrentMetrics();
        
        // 定义健康阈值
        var cpuThreshold = 80.0; // 80% CPU
        var memoryThreshold = 2L * 1024 * 1024 * 1024; // 2GB 内存
        var threadThreshold = 100; // 100 线程

        var isHealthy = currentMetrics.CpuUsagePercent < cpuThreshold &&
                       currentMetrics.MemoryUsageBytes < memoryThreshold &&
                       currentMetrics.ActiveThreads < threadThreshold;

        if (!isHealthy)
        {
            _logger.LogWarning("System health check failed: CPU {Cpu}%, Memory {Memory}MB, Threads {Threads}",
                currentMetrics.CpuUsagePercent, 
                currentMetrics.MemoryUsageBytes / (1024 * 1024), 
                currentMetrics.ActiveThreads);
        }

        return isHealthy;
    }

    private async void CollectMetrics(object? state)
    {
        try
        {
            var metrics = GetCurrentMetrics();
            await LogMetricsAsync(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting performance metrics: {Error}", ex.Message);
        }
    }
}

/// <summary>
/// 清理过期文件和会话的后台服务
/// </summary>
public class ProcessingCleanupService : BackgroundService
{
    private readonly ILogger<ProcessingCleanupService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ProcessingCleanupService(
        ILogger<ProcessingCleanupService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Processing cleanup service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                
                // 清理过期文件
                var fileUploadService = scope.ServiceProvider.GetRequiredService<IFileUploadService>();
                await fileUploadService.CleanupExpiredFilesAsync();

                // 清理已完成的会话
                var threadPoolManager = scope.ServiceProvider.GetRequiredService<IThreadPoolManager>();
                await threadPoolManager.CleanupCompletedSessionsAsync(TimeSpan.FromHours(24));

                // 等待1小时后进行下次清理
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup operation: {Error}", ex.Message);
                
                // 等待10分钟后重试
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

        _logger.LogInformation("Processing cleanup service stopped");
    }
}