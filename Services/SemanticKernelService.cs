using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ExamRecognitionSystem.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ExamRecognitionSystem.Services;

/// <summary>
/// Service for managing Semantic Kernel operations with Doubao vision model
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
    private readonly DoubaoSettings _settings;
    private readonly ILogger<SemanticKernelService> _logger;
    private readonly ConcurrentDictionary<string, ConversationHistory> _conversations;

    public SemanticKernelService(
        Kernel kernel,
        IChatCompletionService chatService,
        DoubaoSettings settings,
        ILogger<SemanticKernelService> logger)
    {
        _kernel = kernel;
        _chatService = chatService;
        _settings = settings;
        _logger = logger;
        _conversations = new ConcurrentDictionary<string, ConversationHistory>();
    }

    public async Task<VisionAnalysisResponse> AnalyzeImageAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("Starting vision analysis for image with {QuestionCount} questions", request.QuestionNumbers.Count);

           var c= new ChatHistory();
            c.AddUserMessage("Hello");
            var aa = _chatService.GetChatMessageContentAsync(c, new OpenAIPromptExecutionSettings
            {
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature
            }, _kernel, cancellationToken);
            var ccc=  aa.Result?.Content?.ToString();
            // Get or create conversation history
            var conversation = request.History ?? await CreateConversationAsync("Exam paper question extraction");
            
            // Build the analysis prompt
            var questionNumbers = request.QuestionNumbers.Select(q => int.TryParse(q, out var num) ? num : 0).Where(n => n > 0).ToList();
            var prompt = BuildAnalysisPrompt(request.Prompt, questionNumbers);
            
            // Create chat history from conversation
            var chatHistory = new ChatHistory();
            
            // Add system message
            chatHistory.AddSystemMessage(GetSystemPrompt());
            
            // Add previous conversation messages
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

            // Add current request with image
            var userMessage = prompt;
            if (!string.IsNullOrEmpty(request.ImageBase64))
            {
                userMessage += "\n[Image data provided for analysis]";
            }
            chatHistory.AddUserMessage(userMessage);

            // Configure execution settings
            var executionSettings = new OpenAIPromptExecutionSettings
            {
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                TopP = 0.9,
                FrequencyPenalty = 0.1,
                PresencePenalty = 0.1
            };

            // Get response from the model
            var response = await _chatService.GetChatMessageContentAsync(chatHistory, executionSettings, _kernel, cancellationToken);
            
            stopwatch.Stop();

            // Update conversation history
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

            // Parse extracted questions
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
                Extract exam questions from the following text. Focus on questions numbered: {string.Join(", ", questionNumbers)}
                
                Text to analyze:
                {text}
                
                Please extract the questions in the specified JSON format.
                """;

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(GetSystemPrompt());
            chatHistory.AddUserMessage(prompt);

            var response = await _chatService.GetChatMessageContentAsync(chatHistory, new OpenAIPromptExecutionSettings
            {
                MaxTokens = _settings.MaxTokens,
                Temperature = 0.3 // Lower temperature for more consistent extraction
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
            $"Focus specifically on questions numbered: {string.Join(", ", questionNumbers)}" : 
            "Extract all visible questions";

        return $"""
            {basePrompt}
            
            {questionRange}
            
            Please analyze this exam paper image and extract the questions in the specified format.
            Pay attention to:
            1. Question numbers and their exact content
            2. Question types (single choice, multiple choice, fill-in-blank, short answer, essay)
            3. Point values for each question
            4. Available options for choice questions
            5. Any visible answers or answer keys
            
            Provide the response in the JSON format specified in the system prompt.
            """;
    }

    private string GetSystemPrompt()
    {
        return """
            You are an expert exam paper analyzer. Your task is to extract questions from exam papers (images or text) and structure them in a specific JSON format.

            For each question, identify:
            1. Question number
            2. Complete question content (including question stem and all options)
            3. Question type: SingleChoice, MultipleChoice, FillInBlank, ShortAnswer, Essay, or Unknown
            4. Point value (extract from text like "3分", "5 points", etc.)
            5. Options (for choice questions, list all options A, B, C, D, etc.)
            6. Answer (if visible in the image/text)

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

            Important guidelines:
            - Be precise with question numbers
            - Include complete question content
            - Identify question types accurately
            - Extract point values as decimal numbers
            - For choice questions, include all options with their labels
            - Only include answers if they are clearly visible
            - Use null for missing information
            - Ensure valid JSON format
            """;
    }

    private async Task<List<ExamQuestion>> ParseExtractedQuestionsAsync(string response, List<int> expectedQuestionNumbers)
    {
        var questions = new List<ExamQuestion>();
        
        try
        {
            // Try to extract JSON from the response
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
            
            // Fallback: try to create basic questions based on expected numbers
            questions = expectedQuestionNumbers.Select(num => new ExamQuestion
            {
                QuestionNumber = num,
                Content = $"Question {num} - Content extraction failed",
                Type = QuestionType.Unknown,
                Points = 0
            }).ToList();
        }
        
        return questions;
    }
}