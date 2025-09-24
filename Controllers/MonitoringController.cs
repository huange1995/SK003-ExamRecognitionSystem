using Microsoft.AspNetCore.Mvc;
using ExamRecognitionSystem.DTOs;
using ExamRecognitionSystem.Models;
using ExamRecognitionSystem.Services;

namespace ExamRecognitionSystem.Controllers;

/// <summary>
/// 用于监控处理进度和系统状态的控制器
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
    /// 获取指定会话的处理状态
    /// </summary>
    /// <param name="sessionId">要检查的会话ID</param>
    /// <returns>处理状态信息</returns>
    [HttpGet("status/{sessionId}")]
    public async Task<ActionResult<ApiResponse<ProcessingStatusResponse>>> GetProcessingStatus(string sessionId)
    {
        try
        {
            var session = await _threadPoolManager.GetSessionAsync(sessionId);
            if (session == null)
            {
                return NotFound(ApiResponse<ProcessingStatusResponse>.ErrorResult(
                    $"会话 {sessionId} 未找到"));
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
            _logger.LogError(ex, "获取会话 {SessionId} 状态时发生错误：{Error}", sessionId, ex.Message);
            return StatusCode(500, ApiResponse<ProcessingStatusResponse>.ErrorResult(
                "内部服务器错误", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 获取所有活跃的处理会话
    /// </summary>
    /// <returns>活跃会话列表</returns>
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
            _logger.LogError(ex, "获取活跃会话时发生错误：{Error}", ex.Message);
            return StatusCode(500, ApiResponse<List<ProcessingStatusResponse>>.ErrorResult(
                "内部服务器错误", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 获取已完成会话的提取题目
    /// </summary>
    /// <param name="sessionId">要获取题目的会话ID</param>
    /// <returns>提取的题目</returns>
    [HttpGet("questions/{sessionId}")]
    public async Task<ActionResult<ApiResponse<QuestionsResponse>>> GetExtractedQuestions(string sessionId)
    {
        try
        {
            var session = await _threadPoolManager.GetSessionAsync(sessionId);
            if (session == null)
            {
                return NotFound(ApiResponse<QuestionsResponse>.ErrorResult(
                    $"会话 {sessionId} 未找到"));
            }

            if (session.Status != SessionStatus.Completed)
            {
                return BadRequest(ApiResponse<QuestionsResponse>.ErrorResult(
                    $"会话 {sessionId} 未完成。当前状态：{session.Status}"));
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
            _logger.LogError(ex, "获取会话 {SessionId} 题目时发生错误：{Error}", sessionId, ex.Message);
            return StatusCode(500, ApiResponse<QuestionsResponse>.ErrorResult(
                "内部服务器错误", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 获取当前系统性能指标
    /// </summary>
    /// <returns>当前性能指标</returns>
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
            _logger.LogError(ex, "获取当前性能指标时发生错误：{Error}", ex.Message);
            return StatusCode(500, ApiResponse<PerformanceMetricsDto>.ErrorResult(
                "内部服务器错误", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 获取历史性能指标
    /// </summary>
    /// <param name="hours">要检索的历史小时数（默认：1）</param>
    /// <returns>历史性能指标</returns>
    [HttpGet("performance/history")]
    public async Task<ActionResult<ApiResponse<List<PerformanceMetricsDto>>>> GetPerformanceHistory(
        [FromQuery] int hours = 1)
    {
        try
        {
            var period = TimeSpan.FromHours(Math.Max(1, Math.Min(24, hours))); // 限制为1-24小时
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
            _logger.LogError(ex, "获取性能历史记录时发生错误：{Error}", ex.Message);
            return StatusCode(500, ApiResponse<List<PerformanceMetricsDto>>.ErrorResult(
                "内部服务器错误", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 获取系统健康状态
    /// </summary>
    /// <returns>系统健康信息</returns>
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
            _logger.LogError(ex, "获取系统健康状态时发生错误：{Error}", ex.Message);
            return StatusCode(500, ApiResponse<SystemHealthDto>.ErrorResult(
                "内部服务器错误", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 将题目导出为JSON格式
    /// </summary>
    /// <param name="sessionId">要导出的会话ID</param>
    /// <returns>包含题目的JSON文件</returns>
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
                return BadRequest("会话未完成");
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
            _logger.LogError(ex, "导出会话 {SessionId} 题目时发生错误：{Error}", sessionId, ex.Message);
            return StatusCode(500, "导出过程中发生内部服务器错误");
        }
    }

    private List<string> GetSystemIssues(PerformanceMetrics metrics, List<ProcessingSession> activeSessions)
    {
        var issues = new List<string>();

        if (metrics.CpuUsagePercent > 80)
            issues.Add($"CPU使用率过高: {metrics.CpuUsagePercent:F1}%");

        if (metrics.MemoryUsageBytes > 2L * 1024 * 1024 * 1024) // 2GB 内存限制
            issues.Add($"内存使用量过高: {metrics.MemoryUsageBytes / (1024 * 1024):F0}MB");

        if (metrics.ActiveThreads > 100)
            issues.Add($"线程数量过多: {metrics.ActiveThreads}");

        var failedSessions = activeSessions.Count(s => s.Status == SessionStatus.Failed);
        if (failedSessions > 0)
            issues.Add($"检测到失败的会话: {failedSessions}");

        return issues;
    }
}