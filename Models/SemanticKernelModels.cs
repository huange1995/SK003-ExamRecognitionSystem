namespace ExamRecognitionSystem.Models;

/// <summary>
/// Ollama AI 模型的配置设置
/// </summary>
public class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ModelId { get; set; } = "llama3.2";
    public int MaxTokens { get; set; } = 4000;
    public double Temperature { get; set; } = 0.7;
    public int MaxRetries { get; set; } = 3;
    public int RequestTimeout { get; set; } = 20;
}

/// <summary>
/// OpenAI API 的配置设置
/// </summary>
public class OpenAISettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = "gpt-4o";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public int MaxTokens { get; set; } = 4000;
    public double Temperature { get; set; } = 0.7;
    public int MaxRetries { get; set; } = 3;
    public int RequestTimeout { get; set; } = 60;
    public string? Organization { get; set; }
}

/// <summary>
/// 豆包 API 的配置设置
/// </summary>
public class DoubaoSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = "ep-20250921160727-qgzd9";
    public string BaseUrl { get; set; } = "https://ark.cn-beijing.volces.com/api/v3";
    public int MaxTokens { get; set; } = 4000;
    public double Temperature { get; set; } = 0.7;
    public int MaxRetries { get; set; } = 3;
    public int RequestTimeout { get; set; } = 60;
}

/// <summary>
/// AI 提供商配置
/// </summary>
public class AIProviderSettings
{
    public string Provider { get; set; } = "Ollama"; // "Ollama"、"OpenAI" 或 "Doubao"
    public OllamaSettings Ollama { get; set; } = new();
    public OpenAISettings OpenAI { get; set; } = new();
    public DoubaoSettings Doubao { get; set; } = new();
}

/// <summary>
/// 表示多轮对话的会话历史
/// </summary>
public class ConversationHistory
{
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();
    public List<ConversationMessage> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public string Context { get; set; } = string.Empty;
    public int MessageCount => Messages.Count;
}

/// <summary>
/// 会话中的单条消息
/// </summary>
public class ConversationMessage
{
    public string Role { get; set; } = string.Empty; // "user"、"assistant"、"system"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ImageBase64 { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 图像处理的视觉分析请求
/// </summary>
public class VisionAnalysisRequest
{
    public string ImageBase64 { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public List<string> QuestionNumbers { get; set; } = new();
    public ConversationHistory? History { get; set; }
    public int MaxTokens { get; set; } = 4000;
    public double Temperature { get; set; } = 0.7;
}

/// <summary>
/// 视觉分析的响应
/// </summary>
public class VisionAnalysisResponse
{
    public bool Success { get; set; }
    public string Content { get; set; } = string.Empty;
    public List<ExamQuestion> ExtractedQuestions { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public int TokensUsed { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public ConversationHistory? UpdatedHistory { get; set; }
}