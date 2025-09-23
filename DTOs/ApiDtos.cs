using ExamRecognitionSystem.Models;

namespace ExamRecognitionSystem.DTOs;

/// <summary>
/// Response for file upload operation
/// </summary>
public class FileUploadResponse
{
    public bool Success { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// 多模态聊天请求DTO
/// </summary>
public class MultiModalChatRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? Base64Image { get; set; }
    public string ImageFormat { get; set; } = "png";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2000;
}

/// <summary>
/// 多模态聊天响应DTO
/// </summary>
public class MultiModalChatResponse
{
    public bool Success { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 图片验证响应DTO
/// </summary>
public class ImageValidationResponse
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ImageType { get; set; }
}

/// <summary>
/// Request for starting processing
/// </summary>
public class ProcessingStartRequest
{
    public string SessionId { get; set; } = string.Empty;
    public ThreadPoolConfig? CustomConfig { get; set; }
}

/// <summary>
/// Response for processing status
/// </summary>
public class ProcessingStatusResponse
{
    public string SessionId { get; set; } = string.Empty;
    public SessionStatus Status { get; set; }
    public int TotalQuestions { get; set; }
    public int CompletedQuestions { get; set; }
    public double ProgressPercentage => TotalQuestions > 0 ? (double)CompletedQuestions / TotalQuestions * 100 : 0;
    public List<TaskStatusDto> TaskStatuses { get; set; } = new();
    public PerformanceMetricsDto Metrics { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public DateTime? EstimatedCompletion { get; set; }
}

/// <summary>
/// Task status DTO
/// </summary>
public class TaskStatusDto
{
    public string TaskId { get; set; } = string.Empty;
    public int StartQuestionNumber { get; set; }
    public int EndQuestionNumber { get; set; }
    public ExamRecognitionSystem.Models.TaskStatus Status { get; set; }
    public int ThreadId { get; set; }
    public int Progress { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Performance metrics DTO
/// </summary>
public class PerformanceMetricsDto
{
    public TimeSpan ProcessingDuration { get; set; }
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageBytes { get; set; }
    public int ActiveThreads { get; set; }
    public int QuestionsPerSecond { get; set; }
    public string FormattedMemoryUsage => FormatBytes(MemoryUsageBytes);
    
    private static string FormatBytes(long bytes)
    {
        const int scale = 1024;
        string[] orders = { "GB", "MB", "KB", "Bytes" };
        long max = (long)Math.Pow(scale, orders.Length - 1);

        foreach (string order in orders)
        {
            if (bytes > max)
                return $"{decimal.Divide(bytes, max):##.##} {order}";
            max /= scale;
        }
        return "0 Bytes";
    }
}

/// <summary>
/// Response containing extracted questions
/// </summary>
public class QuestionsResponse
{
    public bool Success { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public List<ExamQuestionDto> Questions { get; set; } = new();
    public int TotalQuestions => Questions.Count;
    public string? ErrorMessage { get; set; }
    public PerformanceMetricsDto Metrics { get; set; } = new();
}

/// <summary>
/// Exam question DTO
/// </summary>
public class ExamQuestionDto
{
    public int QuestionNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public decimal Points { get; set; }
    public List<string> Options { get; set; } = new();
    public string? Answer { get; set; }
    public string? Explanation { get; set; }
    public string TypeDisplayName => Type.ToString();
}

/// <summary>
/// Generic API response wrapper
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    public static ApiResponse<T> SuccessResult(T data, string message = "Operation completed successfully")
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }
    
    public static ApiResponse<T> ErrorResult(string message, List<string>? errors = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Errors = errors ?? new List<string>()
        };
    }
}

/// <summary>
/// System health status DTO
/// </summary>
public class SystemHealthDto
{
    public bool IsHealthy { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public PerformanceMetricsDto SystemMetrics { get; set; } = new();
    public int ActiveSessions { get; set; }
    public int TotalThreads { get; set; }
    public string Version { get; set; } = "1.0.0";
    public List<string> Issues { get; set; } = new();
}