using ExamRecognitionSystem.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SixLabors.ImageSharp.Formats;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ExamRecognitionSystem.Services;

/// <summary>
/// 管理豆包视觉模型的 Semantic Kernel 操作服务
/// </summary>
public interface ISemanticKernelService
{
    Task<VisionAnalysisResponse> AnalyzeImageAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default);
    Task<ConversationHistory> CreateConversationAsync(string context = "");
    Task<ConversationHistory> GetConversationAsync(string conversationId);
    Task<bool> DeleteConversationAsync(string conversationId);
    Task<List<ExamQuestion>> ExtractQuestionsFromTextAsync(string text, List<int> questionNumbers, CancellationToken cancellationToken = default);
}

public class SemanticKernelService : ISemanticKernelService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly AIProviderSettings _aiProviderSettings;
    private readonly ILogger<SemanticKernelService> _logger;
    private readonly ConcurrentDictionary<string, ConversationHistory> _conversations;

    public SemanticKernelService(
        Kernel kernel,
        IChatCompletionService chatService,
        AIProviderSettings aiProviderSettings,
        ILogger<SemanticKernelService> logger)
    {
        _kernel = kernel;
        _chatService = chatService;
        _aiProviderSettings = aiProviderSettings;
        _logger = logger;
        _conversations = new ConcurrentDictionary<string, ConversationHistory>();
    }

    public async Task<VisionAnalysisResponse> AnalyzeImageAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("Starting vision analysis for image with {QuestionCount} questions", request.QuestionNumbers.Count);

            // 获取或创建对话历史
            var conversation = request.History ?? await CreateConversationAsync("Exam paper question extraction");
            
            // 构建分析提示
            var questionNumbers = request.QuestionNumbers.Select(q => int.TryParse(q, out var num) ? num : 0).Where(n => n > 0).ToList();
            var prompt = BuildAnalysisPrompt(request.Prompt, questionNumbers);
            
            // 从对话中创建聊天历史
            var chatHistory = new ChatHistory();
            
            // 添加系统消息
            chatHistory.AddSystemMessage(GetSystemPrompt());
            
            // 添加之前的对话消息
            foreach (var msg in conversation.Messages)
            {
                if (msg.Role == "user")
                {
                    chatHistory.AddUserMessage(msg.Content);
                }
                else if (msg.Role == "assistant")
                {
                    chatHistory.AddAssistantMessage(msg.Content);
                }
            }

            // 添加当前带图像的请求
            var userMessage = prompt;
            chatHistory.AddUserMessage(userMessage);
            if (!string.IsNullOrEmpty(request.ImageBase64))
            {
                //userMessage += "\n[已提供图像数据用于分析]";
          
            }
            // 配置执行设置
            var executionSettings = new OpenAIPromptExecutionSettings
            {
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                TopP = 0.9,
                FrequencyPenalty = 0.1,
                PresencePenalty = 0.1,
                ExtensionData=new Dictionary<string, object>
                {
                    { "base64Image", request.ImageBase64 },
                    { "imageFormat", "png" }
                }
            };
            // 从模型获取响应
            var response = await _chatService.GetChatMessageContentAsync(chatHistory, executionSettings, _kernel, cancellationToken);
            
            stopwatch.Stop();

            // 更新对话历史
            conversation.Messages.Add(new ConversationMessage
            {
                Role = "user",
                Content = prompt,
                ImageBase64 = request.ImageBase64,
                Timestamp = DateTime.UtcNow
            });

            conversation.Messages.Add(new ConversationMessage
            {
                Role = "assistant",
                Content = response.Content ?? "",
                Timestamp = DateTime.UtcNow
            });

            conversation.LastActivity = DateTime.UtcNow;
            _conversations.TryAdd(conversation.ConversationId, conversation);

            // 解析提取的问题
            var extractedQuestions = await ParseExtractedQuestionsAsync(response.Content ?? "", questionNumbers);

            _logger.LogInformation("Vision analysis completed successfully. Extracted {QuestionCount} questions in {Duration}ms", 
                extractedQuestions.Count, stopwatch.ElapsedMilliseconds);

            return new VisionAnalysisResponse
            {
                Success = true,
                Content = response.Content ?? "",
                ExtractedQuestions = extractedQuestions,
                TokensUsed = response.Metadata?.ContainsKey("Usage") == true ? 
                    JsonSerializer.Deserialize<Dictionary<string, object>>(response.Metadata["Usage"].ToString()!)?.GetValueOrDefault("total_tokens", 0) as int? ?? 0 : 0,
                ProcessingTime = stopwatch.Elapsed,
                UpdatedHistory = conversation
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error during vision analysis: {Error}", ex.Message);
            
            return new VisionAnalysisResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTime = stopwatch.Elapsed
            };
        }
    }

    public async Task<ConversationHistory> CreateConversationAsync(string context = "")
    {
        var conversation = new ConversationHistory
        {
            Context = context,
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };

        _conversations.TryAdd(conversation.ConversationId, conversation);
        
        _logger.LogInformation("Created new conversation {ConversationId} with context: {Context}", 
            conversation.ConversationId, context);

        return await Task.FromResult(conversation);
    }

    public async Task<ConversationHistory> GetConversationAsync(string conversationId)
    {
        _conversations.TryGetValue(conversationId, out var conversation);
        return await Task.FromResult(conversation ?? new ConversationHistory());
    }

    public async Task<bool> DeleteConversationAsync(string conversationId)
    {
        var removed = _conversations.TryRemove(conversationId, out _);
        _logger.LogInformation("Conversation {ConversationId} deleted: {Success}", conversationId, removed);
        return await Task.FromResult(removed);
    }

    public async Task<List<ExamQuestion>> ExtractQuestionsFromTextAsync(string text, List<int> questionNumbers, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Extracting questions from text for question numbers: {QuestionNumbers}", 
                string.Join(", ", questionNumbers));

            var prompt = $"""
                从以下文本中提取考试问题。重点关注编号为：{string.Join(", ", questionNumbers)} 的问题
                
                要分析的文本：
                {text}
                
                请按照指定的JSON格式提取问题。
                """;

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(GetSystemPrompt());
            chatHistory.AddUserMessage(prompt);

            var response = await _chatService.GetChatMessageContentAsync(chatHistory, new OpenAIPromptExecutionSettings
            {
                MaxTokens = GetCurrentProviderMaxTokens(),
                Temperature = 0.3 // 较低的温度值以获得更一致的提取结果
            }, _kernel, cancellationToken);

            return await ParseExtractedQuestionsAsync(response.Content ?? "", questionNumbers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting questions from text: {Error}", ex.Message);
            return new List<ExamQuestion>();
        }
    }

    private string BuildAnalysisPrompt(string basePrompt, List<int> questionNumbers)
    {
        var questionRange = questionNumbers.Count > 0 ? 
            $"重点关注编号为：{string.Join(", ", questionNumbers)} 的问题" : 
            "提取所有可见的问题";

        return $"""
            {basePrompt}
            
            {questionRange}
            
            请分析这张考试试卷图片并按照指定格式提取问题。
            请注意：
            1. 问题编号及其确切内容
            2. 问题类型（单选题、多选题、填空题、简答题、论述题）
            3. 每个问题的分值
            4. 选择题的可选项
            5. 任何可见的答案或答案键
            
            请按照系统提示中指定的JSON格式提供响应。
            """;
    }

    private string GetSystemPrompt()
    {
        return """
            您是一位专业的考试试卷分析师。您的任务是从考试试卷（图片或文本）中提取问题并按照特定的JSON格式进行结构化。

            对于每个问题，请识别：
            1. 问题编号
            2. 完整的问题内容（包括题干和所有选项）
            3. 问题类型：SingleChoice（单选）、MultipleChoice（多选）、FillInBlank（填空）、ShortAnswer（简答）、Essay（论述）或Unknown（未知）
            4. 分值（从文本中提取，如"3分"、"5 points"等）
            5. 选项（对于选择题，列出所有选项A、B、C、D等）
            6. 答案（如果在图片/文本中可见）

            Response format (JSON):
            {
              "questions": [
                {
                  "questionNumber": 1,
                  "content": "Complete question text including all options",
                  "type": "SingleChoice",
                  "points": 3.0,
                  "options": ["A. Option 1", "B. Option 2", "C. Option 3", "D. Option 4"],
                  "answer": "A",
                  "explanation": null
                }
              ]
            }

            重要指导原则：
            - 准确识别问题编号
            - 包含完整的问题内容
            - 准确识别问题类型
            - 将分值提取为十进制数字
            - 对于选择题，包含所有选项及其标签
            - 仅在答案清晰可见时才包含答案
            - 对缺失信息使用null
            - 确保有效的JSON格式
            """;
    }

    private async Task<List<ExamQuestion>> ParseExtractedQuestionsAsync(string response, List<int> expectedQuestionNumbers)
    {
        var questions = new List<ExamQuestion>();
        
        try
        {
            // 尝试从响应中提取JSON
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}') + 1;
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonContent = response.Substring(jsonStart, jsonEnd - jsonStart);
                var jsonDoc = JsonDocument.Parse(jsonContent);
                
                if (jsonDoc.RootElement.TryGetProperty("questions", out var questionsArray))
                {
                    foreach (var questionElement in questionsArray.EnumerateArray())
                    {
                        var question = new ExamQuestion();
                        
                        if (questionElement.TryGetProperty("questionNumber", out var qNum))
                            question.QuestionNumber = qNum.GetInt32();
                        
                        if (questionElement.TryGetProperty("content", out var content))
                            question.Content = content.GetString() ?? "";
                        
                        if (questionElement.TryGetProperty("type", out var type))
                        {
                            Enum.TryParse<QuestionType>(type.GetString(), true, out var questionType);
                            question.Type = questionType;
                        }
                        
                        if (questionElement.TryGetProperty("points", out var points))
                            question.Points = (decimal)points.GetDouble();
                        
                        if (questionElement.TryGetProperty("options", out var options))
                        {
                            question.Options = options.EnumerateArray()
                                .Select(opt => opt.GetString() ?? "")
                                .ToList();
                        }
                        
                        if (questionElement.TryGetProperty("answer", out var answer) && answer.ValueKind != JsonValueKind.Null)
                            question.Answer = answer.GetString();
                        
                        if (questionElement.TryGetProperty("explanation", out var explanation) && explanation.ValueKind != JsonValueKind.Null)
                            question.Explanation = explanation.GetString();
                        
                        questions.Add(question);
                    }
                }
            }
            
            _logger.LogInformation("Successfully parsed {Count} questions from AI response", questions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing extracted questions from response: {Error}", ex.Message);
            
            // 备用方案：尝试基于预期编号创建基本问题
            questions = expectedQuestionNumbers.Select(num => new ExamQuestion
            {
                QuestionNumber = num,
                Content = $"问题 {num} - 内容提取失败",
                Type = QuestionType.Unknown,
                Points = 0
            }).ToList();
        }
        
        return questions;
    }

    /// <summary>
    /// 获取当前AI提供商的最大令牌设置
    /// </summary>
    private int GetCurrentProviderMaxTokens()
    {
        return _aiProviderSettings.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            ? _aiProviderSettings.OpenAI.MaxTokens
            : _aiProviderSettings.Ollama.MaxTokens;
    }

    /// <summary>
    /// 获取当前AI提供商的温度设置
    /// </summary>
    private double GetCurrentProviderTemperature()
    {
        return _aiProviderSettings.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            ? _aiProviderSettings.OpenAI.Temperature
            : _aiProviderSettings.Ollama.Temperature;
    }
}