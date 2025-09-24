namespace ExamRecognitionSystem.Models;

/// <summary>
/// 表示包含所有属性的考试题目
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
/// 支持的题目类型枚举
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
/// 表示一组题目的处理任务
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
    public int Progress { get; set; } = 0; // 0-100 百分比
}

/// <summary>
/// 任务状态枚举
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
/// 表示整体处理会话
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
/// 文件类型枚举
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
/// 会话状态枚举
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
/// 用于监控的性能指标
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
/// 线程池配置
/// </summary>
public class ThreadPoolConfig
{
    public int MaxConcurrentThreads { get; set; } = Environment.ProcessorCount;
    public int QuestionsPerThread { get; set; } = 5;
    public TimeSpan ThreadTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public bool EnableAdaptiveThreading { get; set; } = true;
}