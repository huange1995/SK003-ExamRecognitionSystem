namespace ExamRecognitionSystem.Models;

/// <summary>
/// Represents an exam question with all its properties
/// </summary>
public class ExamQuestion
{
    public int QuestionNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public decimal Points { get; set; }
    public List<string> Options { get; set; } = new();
    public string? Answer { get; set; }
    public string? Explanation { get; set; }
}

/// <summary>
/// Enumeration of supported question types
/// </summary>
public enum QuestionType
{
    SingleChoice,
    MultipleChoice,
    FillInBlank,
    ShortAnswer,
    Essay,
    Unknown
}

/// <summary>
/// Represents a processing task for a group of questions
/// </summary>
public class ProcessingTask
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString();
    public int StartQuestionNumber { get; set; }
    public int EndQuestionNumber { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Pending;
    public int ThreadId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<ExamQuestion> Questions { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public int Progress { get; set; } = 0; // 0-100 percentage
}

/// <summary>
/// Task status enumeration
/// </summary>
public enum TaskStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Represents the overall processing session
/// </summary>
public class ProcessingSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public FileType FileType { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Created;
    public int TotalQuestions { get; set; }
    public int CompletedQuestions { get; set; }
    public List<ProcessingTask> Tasks { get; set; } = new();
    public List<ExamQuestion> AllQuestions { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public PerformanceMetrics Metrics { get; set; } = new();
}

/// <summary>
/// File type enumeration
/// </summary>
public enum FileType
{
    Pdf,
    Docx,
    Jpeg,
    Png,
    Unknown
}

/// <summary>
/// Session status enumeration
/// </summary>
public enum SessionStatus
{
    Created,
    Uploading,
    Uploaded,
    Processing,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Performance metrics for monitoring
/// </summary>
public class PerformanceMetrics
{
    public TimeSpan ProcessingDuration { get; set; }
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageBytes { get; set; }
    public int ActiveThreads { get; set; }
    public int QuestionsPerSecond { get; set; }
}

/// <summary>
/// Thread pool configuration
/// </summary>
public class ThreadPoolConfig
{
    public int MaxConcurrentThreads { get; set; } = Environment.ProcessorCount;
    public int QuestionsPerThread { get; set; } = 5;
    public TimeSpan ThreadTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public bool EnableAdaptiveThreading { get; set; } = true;
}