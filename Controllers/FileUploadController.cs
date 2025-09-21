using Microsoft.AspNetCore.Mvc;
using ExamRecognitionSystem.DTOs;
using ExamRecognitionSystem.Models;
using ExamRecognitionSystem.Services;
using ExamRecognitionSystem.Extensions;

namespace ExamRecognitionSystem.Controllers;

/// <summary>
/// Controller for file upload operations
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
    /// Upload exam paper file for processing
    /// </summary>
    /// <param name="file">The exam paper file (PDF, DOCX, JPEG, PNG)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>File upload result with session ID</returns>
    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB limit
    [DisableRequestSizeLimit]
    public async Task<ActionResult<ApiResponse<FileUploadResponse>>> UploadFile(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Received file upload request: {FileName} ({Size} bytes)",
                file?.FileName, file?.Length);

            if (file == null)
            {
                return BadRequest(ApiResponse<FileUploadResponse>.ErrorResult(
                    "No file provided", new List<string> { "File is required" }));
            }

            // Upload file
            var (success, sessionId, errors) = await _fileUploadService.UploadFileAsync(file, cancellationToken);

            if (!success)
            {
                return BadRequest(ApiResponse<FileUploadResponse>.ErrorResult(
                    "File upload failed", errors));
            }

            // Get file path for session creation
            var filePath = await _fileUploadService.GetFilePathAsync(sessionId);
            if (string.IsNullOrEmpty(filePath))
            {
                return BadRequest(ApiResponse<FileUploadResponse>.ErrorResult(
                    "Failed to retrieve uploaded file"));
            }

            // Create processing session
            var fileType = file.FileName.GetFileTypeFromExtension();
            var processingSessionId = await _threadPoolManager.CreateProcessingSessionAsync(filePath, fileType);

            var response = new FileUploadResponse
            {
                Success = true,
                SessionId = processingSessionId,
                Message = "File uploaded successfully",
                FileName = file.FileName,
                FileSizeBytes = file.Length
            };

            _logger.LogInformation("File upload completed successfully. Session ID: {SessionId}", processingSessionId);

            return Ok(ApiResponse<FileUploadResponse>.SuccessResult(response, "File uploaded successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during file upload: {Error}", ex.Message);
            return StatusCode(500, ApiResponse<FileUploadResponse>.ErrorResult(
                "Internal server error during file upload", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// Start processing uploaded file
    /// </summary>
    /// <param name="request">Processing start request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing start result</returns>
    [HttpPost("start-processing")]
    public async Task<ActionResult<ApiResponse<ProcessingStatusResponse>>> StartProcessing(
        [FromBody] ProcessingStartRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting processing for session {SessionId}", request.SessionId);

            var session = await _threadPoolManager.GetSessionAsync(request.SessionId);
            if (session == null)
            {
                return NotFound(ApiResponse<ProcessingStatusResponse>.ErrorResult(
                    $"Session {request.SessionId} not found"));
            }

            var started = await _threadPoolManager.StartProcessingAsync(request.SessionId, cancellationToken);
            if (!started)
            {
                return BadRequest(ApiResponse<ProcessingStatusResponse>.ErrorResult(
                    "Failed to start processing. Check session status."));
            }

            var statusResponse = MapToStatusResponse(session);
            return Ok(ApiResponse<ProcessingStatusResponse>.SuccessResult(statusResponse, "Processing started successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting processing for session {SessionId}: {Error}", request.SessionId, ex.Message);
            return StatusCode(500, ApiResponse<ProcessingStatusResponse>.ErrorResult(
                "Internal server error during processing start", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// Cancel processing for a session
    /// </summary>
    /// <param name="sessionId">Session ID to cancel</param>
    /// <returns>Cancellation result</returns>
    [HttpPost("cancel/{sessionId}")]
    public async Task<ActionResult<ApiResponse<bool>>> CancelProcessing(string sessionId)
    {
        try
        {
            _logger.LogInformation("Cancelling processing for session {SessionId}", sessionId);

            var cancelled = await _threadPoolManager.CancelProcessingAsync(sessionId);
            if (!cancelled)
            {
                return NotFound(ApiResponse<bool>.ErrorResult(
                    $"Session {sessionId} not found or cannot be cancelled"));
            }

            return Ok(ApiResponse<bool>.SuccessResult(true, "Processing cancelled successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling processing for session {SessionId}: {Error}", sessionId, ex.Message);
            return StatusCode(500, ApiResponse<bool>.ErrorResult(
                "Internal server error during processing cancellation", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// Delete uploaded file and associated session
    /// </summary>
    /// <param name="sessionId">Session ID to delete</param>
    /// <returns>Deletion result</returns>
    [HttpDelete("{sessionId}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteFile(string sessionId)
    {
        try
        {
            _logger.LogInformation("Deleting file for session {SessionId}", sessionId);

            // Cancel processing if still running
            await _threadPoolManager.CancelProcessingAsync(sessionId);

            // Delete uploaded file
            var deleted = await _fileUploadService.DeleteFileAsync(sessionId);

            return Ok(ApiResponse<bool>.SuccessResult(deleted, 
                deleted ? "File deleted successfully" : "File not found or already deleted"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file for session {SessionId}: {Error}", sessionId, ex.Message);
            return StatusCode(500, ApiResponse<bool>.ErrorResult(
                "Internal server error during file deletion", new List<string> { ex.Message }));
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