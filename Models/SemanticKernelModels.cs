namespace ExamRecognitionSystem.Models;

/// <summary>
/// Configuration settings for Ollama AI model
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
/// Configuration settings for OpenAI API
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
/// AI provider configuration
/// </summary>
public class AIProviderSettings
{
    public string Provider { get; set; } = "Ollama"; // "Ollama" or "OpenAI"
    public OllamaSettings Ollama { get; set; } = new();
    public OpenAISettings OpenAI { get; set; } = new();
}

/// <summary>
/// Represents conversation history for multi-turn dialogues
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
/// Individual message in a conversation
/// </summary>
public class ConversationMessage
{
    public string Role { get; set; } = string.Empty; // "user", "assistant", "system"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ImageBase64 { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Vision analysis request for image processing
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
/// Response from vision analysis
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