using Microsoft.AspNetCore.Mvc;
using ExamRecognitionSystem.DTOs;
using ExamRecognitionSystem.Models;
using ExamRecognitionSystem.Services;
using ExamRecognitionSystem.Extensions;

namespace ExamRecognitionSystem.Controllers;

/// <summary>
/// 文件上传操作控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FileUploadController : ControllerBase
{
    private readonly IFileUploadService _fileUploadService;
    private readonly IThreadPoolManager _threadPoolManager;
    private readonly ILogger<FileUploadController> _logger;

    public FileUploadController(
        IFileUploadService fileUploadService,
        IThreadPoolManager threadPoolManager,
        ILogger<FileUploadController> logger)
    {
        _fileUploadService = fileUploadService;
        _threadPoolManager = threadPoolManager;
        _logger = logger;
    }

    /// <summary>
    /// 上传试卷文件进行处理
    /// </summary>
    /// <param name="file">试卷文件 (PDF, DOCX, JPEG, PNG)</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含会话ID的文件上传结果</returns>
    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB 限制
    [DisableRequestSizeLimit]
    public async Task<ActionResult<ApiResponse<FileUploadResponse>>> UploadFile(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("收到文件上传请求：{FileName}（{Size} 字节）",
                file?.FileName, file?.Length);

            if (file == null)
            {
                return BadRequest(ApiResponse<FileUploadResponse>.ErrorResult(
                    "未提供文件", new List<string> { "文件是必需的" }));
            }

            // 上传文件
            var (success, sessionId, errors) = await _fileUploadService.UploadFileAsync(file, cancellationToken);

            if (!success)
            {
                return BadRequest(ApiResponse<FileUploadResponse>.ErrorResult(
                    "文件上传失败", errors));
            }

            // 获取文件路径用于创建会话
            var filePath = await _fileUploadService.GetFilePathAsync(sessionId);
            if (string.IsNullOrEmpty(filePath))
            {
                return BadRequest(ApiResponse<FileUploadResponse>.ErrorResult(
                    "获取上传文件失败"));
            }

            // 创建处理会话
            var fileType = file.FileName.GetFileTypeFromExtension();
            var processingSessionId = await _threadPoolManager.CreateProcessingSessionAsync(filePath, fileType);

            var response = new FileUploadResponse
            {
                Success = true,
                SessionId = processingSessionId,
                Message = "文件上传成功",
                FileName = file.FileName,
                FileSizeBytes = file.Length
            };

            _logger.LogInformation("文件上传成功完成。会话ID：{SessionId}", processingSessionId);

            return Ok(ApiResponse<FileUploadResponse>.SuccessResult(response, "文件上传成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件上传过程中发生错误：{Error}", ex.Message);
            return StatusCode(500, ApiResponse<FileUploadResponse>.ErrorResult(
                "文件上传过程中发生内部服务器错误", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 开始处理上传的文件
    /// </summary>
    /// <param name="request">处理启动请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理启动结果</returns>
    [HttpPost("start-processing")]
    public async Task<ActionResult<ApiResponse<ProcessingStatusResponse>>> StartProcessing(
        [FromBody] ProcessingStartRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始处理会话 {SessionId}", request.SessionId);

            var session = await _threadPoolManager.GetSessionAsync(request.SessionId);
            if (session == null)
            {
                return NotFound(ApiResponse<ProcessingStatusResponse>.ErrorResult(
                    $"会话 {request.SessionId} 未找到"));
            }

            var started = await _threadPoolManager.StartProcessingAsync(request.SessionId, cancellationToken);
            if (!started)
            {
                return BadRequest(ApiResponse<ProcessingStatusResponse>.ErrorResult(
                    "启动处理失败。请检查会话状态。"));
            }

            var statusResponse = MapToStatusResponse(session);
            return Ok(ApiResponse<ProcessingStatusResponse>.SuccessResult(statusResponse, "处理启动成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动会话 {SessionId} 处理时发生错误：{Error}", request.SessionId, ex.Message);
            return StatusCode(500, ApiResponse<ProcessingStatusResponse>.ErrorResult(
                "启动处理时发生内部服务器错误", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 取消会话的处理
    /// </summary>
    /// <param name="sessionId">要取消的会话ID</param>
    /// <returns>取消结果</returns>
    [HttpPost("cancel/{sessionId}")]
    public async Task<ActionResult<ApiResponse<bool>>> CancelProcessing(string sessionId)
    {
        try
        {
            _logger.LogInformation("取消会话 {SessionId} 的处理", sessionId);

            var cancelled = await _threadPoolManager.CancelProcessingAsync(sessionId);
            if (!cancelled)
            {
                return NotFound(ApiResponse<bool>.ErrorResult(
                    $"会话 {sessionId} 未找到或无法取消"));
            }

            return Ok(ApiResponse<bool>.SuccessResult(true, "处理已成功取消"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消会话 {SessionId} 处理时发生错误：{Error}", sessionId, ex.Message);
            return StatusCode(500, ApiResponse<bool>.ErrorResult(
                "取消处理时发生内部服务器错误", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 删除上传的文件和关联的会话
    /// </summary>
    /// <param name="sessionId">要删除的会话ID</param>
    /// <returns>删除结果</returns>
    [HttpDelete("{sessionId}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteFile(string sessionId)
    {
        try
        {
            _logger.LogInformation("删除会话 {SessionId} 的文件", sessionId);

            // 如果仍在运行则取消处理
            await _threadPoolManager.CancelProcessingAsync(sessionId);

            // 删除上传的文件
            var deleted = await _fileUploadService.DeleteFileAsync(sessionId);

            return Ok(ApiResponse<bool>.SuccessResult(deleted, 
                deleted ? "文件删除成功" : "文件未找到或已删除"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除会话 {SessionId} 文件时发生错误：{Error}", sessionId, ex.Message);
            return StatusCode(500, ApiResponse<bool>.ErrorResult(
                "删除文件时发生内部服务器错误", new List<string> { ex.Message }));
        }
    }

    private ProcessingStatusResponse MapToStatusResponse(ProcessingSession session)
    {
        return new ProcessingStatusResponse
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
            ErrorMessage = session.ErrorMessage,
            EstimatedCompletion = EstimateCompletion(session)
        };
    }

    private DateTime? EstimateCompletion(ProcessingSession session)
    {
        if (session.Status != SessionStatus.Processing || session.CompletedQuestions == 0)
            return null;

        var elapsed = DateTime.UtcNow - (session.StartedAt ?? DateTime.UtcNow);
        var questionsPerSecond = session.CompletedQuestions / Math.Max(1, elapsed.TotalSeconds);
        var remainingQuestions = session.TotalQuestions - session.CompletedQuestions;
        var estimatedRemainingSeconds = remainingQuestions / Math.Max(0.1, questionsPerSecond);

        return DateTime.UtcNow.AddSeconds(estimatedRemainingSeconds);
    }
}