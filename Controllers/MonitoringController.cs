using Microsoft.AspNetCore.Mvc;
using ExamRecognitionSystem.DTOs;
using ExamRecognitionSystem.Models;
using ExamRecognitionSystem.Services;

namespace ExamRecognitionSystem.Controllers;

/// <summary>
/// Controller for monitoring processing progress and system status
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MonitoringController : ControllerBase
{
    private readonly IThreadPoolManager _threadPoolManager;
    private readonly IProgressMonitoringService _progressMonitoringService;
    private readonly IPerformanceMonitoringService _performanceMonitoringService;
    private readonly ILogger<MonitoringController> _logger;

    public MonitoringController(
        IThreadPoolManager threadPoolManager,
        IProgressMonitoringService progressMonitoringService,
        IPerformanceMonitoringService performanceMonitoringService,
        ILogger<MonitoringController> logger)
    {
        _threadPoolManager = threadPoolManager;
        _progressMonitoringService = progressMonitoringService;
        _performanceMonitoringService = performanceMonitoringService;
        _logger = logger;
    }

    /// <summary>
    /// Get processing status for a specific session
    /// </summary>
    /// <param name="sessionId">Session ID to check</param>
    /// <returns>Processing status information</returns>
    [HttpGet("status/{sessionId}")]
    public async Task<ActionResult<ApiResponse<ProcessingStatusResponse>>> GetProcessingStatus(string sessionId)
    {
        try
        {
            var session = await _threadPoolManager.GetSessionAsync(sessionId);
            if (session == null)
            {
                return NotFound(ApiResponse<ProcessingStatusResponse>.ErrorResult(
                    $"Session {sessionId} not found"));
            }

            var statusResponse = new ProcessingStatusResponse
            {
                SessionId = session.SessionId,
                Status = session.Status,
                TotalQuestions = session.TotalQuestions,
                CompletedQuestions = session.CompletedQuestions,
                TaskStatuses = session.Tasks.Select(t => new TaskStatusDto
                {
                    TaskId = t.TaskId,
                    StartQuestionNumber = t.StartQuestionNumber,
                    EndQuestionNumber = t.EndQuestionNumber,
                    Status = t.Status,
                    ThreadId = t.ThreadId,
                    Progress = t.Progress,
                    ErrorMessage = t.ErrorMessage,
                    StartedAt = t.StartedAt,
                    CompletedAt = t.CompletedAt
                }).ToList(),
                Metrics = new PerformanceMetricsDto
                {
                    ProcessingDuration = session.Metrics.ProcessingDuration,
                    CpuUsagePercent = session.Metrics.CpuUsagePercent,
                    MemoryUsageBytes = session.Metrics.MemoryUsageBytes,
                    ActiveThreads = session.Metrics.ActiveThreads,
                    QuestionsPerSecond = session.Metrics.QuestionsPerSecond
                },
                ErrorMessage = session.ErrorMessage
            };

            return Ok(ApiResponse<ProcessingStatusResponse>.SuccessResult(statusResponse));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting status for session {SessionId}: {Error}", sessionId, ex.Message);
            return StatusCode(500, ApiResponse<ProcessingStatusResponse>.ErrorResult(
                "Internal server error", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// Get all active processing sessions
    /// </summary>
    /// <returns>List of active sessions</returns>
    [HttpGet("active-sessions")]
    public async Task<ActionResult<ApiResponse<List<ProcessingStatusResponse>>>> GetActiveSessions()
    {
        try
        {
            var activeSessions = await _threadPoolManager.GetActiveSessionsAsync();
            var statusResponses = activeSessions.Select(session => new ProcessingStatusResponse
            {
                SessionId = session.SessionId,
                Status = session.Status,
                TotalQuestions = session.TotalQuestions,
                CompletedQuestions = session.CompletedQuestions,
                Metrics = new PerformanceMetricsDto
                {
                    ProcessingDuration = session.Metrics.ProcessingDuration,
                    CpuUsagePercent = session.Metrics.CpuUsagePercent,
                    MemoryUsageBytes = session.Metrics.MemoryUsageBytes,
                    ActiveThreads = session.Metrics.ActiveThreads,
                    QuestionsPerSecond = session.Metrics.QuestionsPerSecond
                },
                ErrorMessage = session.ErrorMessage
            }).ToList();

            return Ok(ApiResponse<List<ProcessingStatusResponse>>.SuccessResult(statusResponses));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active sessions: {Error}", ex.Message);
            return StatusCode(500, ApiResponse<List<ProcessingStatusResponse>>.ErrorResult(
                "Internal server error", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// Get extracted questions for a completed session
    /// </summary>
    /// <param name="sessionId">Session ID to get questions for</param>
    /// <returns>Extracted questions</returns>
    [HttpGet("questions/{sessionId}")]
    public async Task<ActionResult<ApiResponse<QuestionsResponse>>> GetExtractedQuestions(string sessionId)
    {
        try
        {
            var session = await _threadPoolManager.GetSessionAsync(sessionId);
            if (session == null)
            {
                return NotFound(ApiResponse<QuestionsResponse>.ErrorResult(
                    $"Session {sessionId} not found"));
            }

            if (session.Status != SessionStatus.Completed)
            {
                return BadRequest(ApiResponse<QuestionsResponse>.ErrorResult(
                    $"Session {sessionId} is not completed. Current status: {session.Status}"));
            }

            var questionsResponse = new QuestionsResponse
            {
                Success = true,
                SessionId = session.SessionId,
                Questions = session.AllQuestions.Select(q => new ExamQuestionDto
                {
                    QuestionNumber = q.QuestionNumber,
                    Content = q.Content,
                    Type = q.Type,
                    Points = q.Points,
                    Options = q.Options,
                    Answer = q.Answer,
                    Explanation = q.Explanation
                }).ToList(),
                Metrics = new PerformanceMetricsDto
                {
                    ProcessingDuration = session.Metrics.ProcessingDuration,
                    CpuUsagePercent = session.Metrics.CpuUsagePercent,
                    MemoryUsageBytes = session.Metrics.MemoryUsageBytes,
                    ActiveThreads = session.Metrics.ActiveThreads,
                    QuestionsPerSecond = session.Metrics.QuestionsPerSecond
                }
            };

            return Ok(ApiResponse<QuestionsResponse>.SuccessResult(questionsResponse));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting questions for session {SessionId}: {Error}", sessionId, ex.Message);
            return StatusCode(500, ApiResponse<QuestionsResponse>.ErrorResult(
                "Internal server error", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// Get current system performance metrics
    /// </summary>
    /// <returns>Current performance metrics</returns>
    [HttpGet("performance/current")]
    public ActionResult<ApiResponse<PerformanceMetricsDto>> GetCurrentPerformance()
    {
        try
        {
            var metrics = _performanceMonitoringService.GetCurrentMetrics();
            var metricsDto = new PerformanceMetricsDto
            {
                ProcessingDuration = metrics.ProcessingDuration,
                CpuUsagePercent = metrics.CpuUsagePercent,
                MemoryUsageBytes = metrics.MemoryUsageBytes,
                ActiveThreads = metrics.ActiveThreads,
                QuestionsPerSecond = metrics.QuestionsPerSecond
            };

            return Ok(ApiResponse<PerformanceMetricsDto>.SuccessResult(metricsDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current performance metrics: {Error}", ex.Message);
            return StatusCode(500, ApiResponse<PerformanceMetricsDto>.ErrorResult(
                "Internal server error", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// Get historical performance metrics
    /// </summary>
    /// <param name="hours">Number of hours of history to retrieve (default: 1)</param>
    /// <returns>Historical performance metrics</returns>
    [HttpGet("performance/history")]
    public async Task<ActionResult<ApiResponse<List<PerformanceMetricsDto>>>> GetPerformanceHistory(
        [FromQuery] int hours = 1)
    {
        try
        {
            var period = TimeSpan.FromHours(Math.Max(1, Math.Min(24, hours))); // Limit to 1-24 hours
            var historicalMetrics = await _performanceMonitoringService.GetHistoricalMetricsAsync(period);
            
            var metricsDto = historicalMetrics.Select(m => new PerformanceMetricsDto
            {
                ProcessingDuration = m.ProcessingDuration,
                CpuUsagePercent = m.CpuUsagePercent,
                MemoryUsageBytes = m.MemoryUsageBytes,
                ActiveThreads = m.ActiveThreads,
                QuestionsPerSecond = m.QuestionsPerSecond
            }).ToList();

            return Ok(ApiResponse<List<PerformanceMetricsDto>>.SuccessResult(metricsDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting performance history: {Error}", ex.Message);
            return StatusCode(500, ApiResponse<List<PerformanceMetricsDto>>.ErrorResult(
                "Internal server error", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// Get system health status
    /// </summary>
    /// <returns>System health information</returns>
    [HttpGet("health")]
    public async Task<ActionResult<ApiResponse<SystemHealthDto>>> GetSystemHealth()
    {
        try
        {
            var isHealthy = _performanceMonitoringService.IsSystemHealthy();
            var currentMetrics = _performanceMonitoringService.GetCurrentMetrics();
            var activeSessions = await _threadPoolManager.GetActiveSessionsAsync();

            var healthDto = new SystemHealthDto
            {
                IsHealthy = isHealthy,
                SystemMetrics = new PerformanceMetricsDto
                {
                    ProcessingDuration = currentMetrics.ProcessingDuration,
                    CpuUsagePercent = currentMetrics.CpuUsagePercent,
                    MemoryUsageBytes = currentMetrics.MemoryUsageBytes,
                    ActiveThreads = currentMetrics.ActiveThreads,
                    QuestionsPerSecond = currentMetrics.QuestionsPerSecond
                },
                ActiveSessions = activeSessions.Count,
                TotalThreads = activeSessions.Sum(s => s.Tasks.Count(t => t.Status == ExamRecognitionSystem.Models.TaskStatus.InProgress)),
                Issues = GetSystemIssues(currentMetrics, activeSessions)
            };

            return Ok(ApiResponse<SystemHealthDto>.SuccessResult(healthDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system health: {Error}", ex.Message);
            return StatusCode(500, ApiResponse<SystemHealthDto>.ErrorResult(
                "Internal server error", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// Export questions to JSON format
    /// </summary>
    /// <param name="sessionId">Session ID to export</param>
    /// <returns>JSON file with questions</returns>
    [HttpGet("export/{sessionId}")]
    public async Task<ActionResult> ExportQuestions(string sessionId)
    {
        try
        {
            var session = await _threadPoolManager.GetSessionAsync(sessionId);
            if (session == null)
            {
                return NotFound();
            }

            if (session.Status != SessionStatus.Completed)
            {
                return BadRequest("Session is not completed");
            }

            var exportData = new
            {
                SessionId = session.SessionId,
                FileName = session.FileName,
                ProcessedAt = session.CompletedAt,
                TotalQuestions = session.AllQuestions.Count,
                Questions = session.AllQuestions.Select(q => new
                {
                    QuestionNumber = q.QuestionNumber,
                    Content = q.Content,
                    Type = q.Type.ToString(),
                    Points = q.Points,
                    Options = q.Options,
                    Answer = q.Answer,
                    Explanation = q.Explanation
                })
            };

            var jsonContent = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            var fileName = $"exam_questions_{sessionId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            return File(System.Text.Encoding.UTF8.GetBytes(jsonContent), "application/json", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting questions for session {SessionId}: {Error}", sessionId, ex.Message);
            return StatusCode(500, "Internal server error during export");
        }
    }

    private List<string> GetSystemIssues(PerformanceMetrics metrics, List<ProcessingSession> activeSessions)
    {
        var issues = new List<string>();

        if (metrics.CpuUsagePercent > 80)
            issues.Add($"High CPU usage: {metrics.CpuUsagePercent:F1}%");

        if (metrics.MemoryUsageBytes > 2L * 1024 * 1024 * 1024) // 2GB
            issues.Add($"High memory usage: {metrics.MemoryUsageBytes / (1024 * 1024):F0}MB");

        if (metrics.ActiveThreads > 100)
            issues.Add($"High thread count: {metrics.ActiveThreads}");

        var failedSessions = activeSessions.Count(s => s.Status == SessionStatus.Failed);
        if (failedSessions > 0)
            issues.Add($"Failed sessions detected: {failedSessions}");

        return issues;
    }
}