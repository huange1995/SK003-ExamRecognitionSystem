using ExamRecognitionSystem.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.TextGeneration;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExamRecognitionSystem.Extensions
{
    public class DoubaoTextGenerationService : ITextGenerationService, IChatCompletionService
    {
        private readonly HttpClient _httpClient;
        private readonly DoubaoSettings _settings;
        private readonly IReadOnlyDictionary<string, object?> _attributes = new Dictionary<string, object?>();

        public DoubaoTextGenerationService(HttpClient httpClient, DoubaoSettings settings)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public IReadOnlyDictionary<string, object?> Attributes => _attributes;

        public async Task<IReadOnlyList<TextContent>> GetTextContentsAsync(
            string prompt,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new
            {
                model = _settings.ModelId,
                messages = new[]
                {
                new { role = "user", content = prompt }
            },
                stream = false,
                temperature = 0.7,
                max_tokens = 2000
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.ApiKey}");

            var response = await _httpClient.PostAsync($"{_settings.BaseUrl}/chat/completions", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"豆包API请求失败：{response.StatusCode}, {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<DoubaoResponse>(responseContent);

            if (result?.Choices?.Length > 0)
            {
                var textContent = new TextContent(result.Choices[0].Message.Content ?? string.Empty);
                return new List<TextContent> { textContent };
            }

            return new List<TextContent>();
        }

        /// <summary>
        /// 获取聊天消息内容，支持多模态输入（文本和图片）
        /// </summary>
        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            // 检查执行设置是否包含图像信息
            var imageUrl = executionSettings?.ExtensionData?.TryGetValue("imageUrl", out var urlValue) == true ? urlValue as string : null;
            var base64Image = executionSettings?.ExtensionData?.TryGetValue("base64Image", out var base64Value) == true ? base64Value as string : null;
            var imageFormat = executionSettings?.ExtensionData?.TryGetValue("imageFormat", out var formatValue) == true ? formatValue as string ?? "png" : "png";

            var messages = new List<object>();

            // 处理聊天历史
            foreach (var msg in chatHistory)
            {
                var messageContent = new List<object> { new { type = "text", text = msg.Content } };

                // 如果这是最后一条用户消息并包含图像信息，则添加图像内容
                if (msg == chatHistory.LastOrDefault() && msg.Role == AuthorRole.User)
                {
                    if (!string.IsNullOrWhiteSpace(imageUrl))
                    {
                        if (ImageHelper.IsValidImageUrl(imageUrl))
                        {
                            messageContent.Add(new { type = "image_url", image_url = new { url = imageUrl } });
                        }
                        else
                        {
                            throw new ArgumentException("提供的图片URL格式无效", nameof(imageUrl));
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(base64Image))
                    {
                        if (ImageHelper.IsValidBase64(base64Image))
                        {
                            var dataUrl = ImageHelper.CreateBase64ImageUrl(base64Image, imageFormat);
                            messageContent.Add(new { type = "image_url", image_url = new { url = dataUrl } });
                        }
                        else
                        {
                            throw new ArgumentException("提供的base64字符串格式无效", nameof(base64Image));
                        }
                    }
                }

                messages.Add(new
                {
                    role = msg.Role.Label.ToLower(),
                    content = messageContent.Count > 1 ? messageContent.ToArray() : (object)msg.Content
                });
            }

            var requestBody = new
            {
                model = _settings.ModelId,
                messages = messages.ToArray(),
                stream = false,
                temperature = 0.7,
                max_tokens = 2000
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.ApiKey}");

            var response = await _httpClient.PostAsync($"{_settings.BaseUrl}/chat/completions", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"豆包API请求失败：{response.StatusCode}, {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<DoubaoResponse>(responseContent);

            if (result?.Choices?.Length > 0)
            {
                var chatMessage = new ChatMessageContent(AuthorRole.Assistant, result.Choices[0].Message.Content ?? string.Empty);
                return new List<ChatMessageContent> { chatMessage };
            }

            return new List<ChatMessageContent>();
        }

        public async IAsyncEnumerable<StreamingTextContent> GetStreamingTextContentsAsync(
            string prompt,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var textContents = await GetTextContentsAsync(prompt, executionSettings, kernel, cancellationToken);
            foreach (var content in textContents)
            {
                yield return new StreamingTextContent(content.Text);
            }
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var chatContents = await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
            foreach (var content in chatContents)
            {
                yield return new StreamingChatMessageContent(content.Role, content.Content);
            }
        }


    }

    public class DoubaoResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonPropertyName("object")]
        public string Object { get; set; } = "chat.completion";
        
        [JsonPropertyName("created")]
        public long Created { get; set; }
        
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;
        
        [JsonPropertyName("service_tier")]
        public string ServiceTier { get; set; } = string.Empty;
        
        [JsonPropertyName("choices")]
        public Choice[]? Choices { get; set; }
        
        [JsonPropertyName("usage")]
        public Usage Usage { get; set; } = new();
    }

    public class Choice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }
        
        [JsonPropertyName("message")]
        public Message Message { get; set; } = new();
        
        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; } = string.Empty;
        
        [JsonPropertyName("logprobs")]
        public object? LogProbs { get; set; }
    }

    public class Message
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "assistant";
        
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
        
        [JsonPropertyName("reasoning_content")]
        public string? ReasoningContent { get; set; }
    }

    public class Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }
        
        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
        
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
        
        [JsonPropertyName("prompt_tokens_details")]
        public PromptTokensDetails? PromptTokensDetails { get; set; }
        
        [JsonPropertyName("completion_tokens_details")]
        public CompletionTokensDetails? CompletionTokensDetails { get; set; }
    }

    public class PromptTokensDetails
    {
        [JsonPropertyName("cached_tokens")]
        public int CachedTokens { get; set; }
    }

    public class CompletionTokensDetails
    {
        [JsonPropertyName("reasoning_tokens")]
        public int ReasoningTokens { get; set; }
    }

    public class DoubaoError
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    public class DoubaoErrorResponse
    {
        public DoubaoError Error { get; set; } = new();
    }



    /// <summary>
/// 图像处理辅助类
/// </summary>
    public static class ImageHelper
    {
        /// <summary>
    /// 验证URL是否为有效的图像URL
    /// </summary>
        public static bool IsValidImageUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            try
            {
                var uri = new Uri(url);
                var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
                return new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" }.Contains(extension);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
    /// 验证base64字符串是否有效
    /// </summary>
        public static bool IsValidBase64(string base64String)
        {
            if (string.IsNullOrWhiteSpace(base64String))
                return false;

            try
            {
                Convert.FromBase64String(base64String);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
    /// 验证base64图像字符串是否有效
    /// </summary>
        public static bool IsValidBase64Image(string base64String)
        {
            if (string.IsNullOrWhiteSpace(base64String))
                return false;

            // 检查是否包含data:image前缀
            if (base64String.StartsWith("data:image/"))
            {
                var base64Index = base64String.IndexOf(",");
                if (base64Index > 0 && base64Index < base64String.Length - 1)
                {
                    base64String = base64String.Substring(base64Index + 1);
                }
            }

            return IsValidBase64(base64String);
        }

        /// <summary>
    /// 创建base64图像URL
    /// </summary>
        public static string CreateBase64ImageUrl(string base64String, string imageFormat = "png")
        {
            return $"data:image/{imageFormat};base64,{base64String}";
        }

        /// <summary>
    /// 从base64 URL中提取base64字符串
    /// </summary>
        public static string? ExtractBase64FromUrl(string dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:image/"))
                return null;

            var base64Index = dataUrl.IndexOf("base64,");
            if (base64Index == -1)
                return null;

            return dataUrl.Substring(base64Index + 7);
        }
    }
}
