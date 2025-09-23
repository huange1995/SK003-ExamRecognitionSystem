using ExamRecognitionSystem.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.TextGeneration;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

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
                throw new HttpRequestException($"Doubao API request failed: {response.StatusCode}, {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<DoubaoResponse>(responseContent);

            if (result?.Choices?.Length > 0)
            {
                var textContent = new TextContent(result.Choices[0].Message.Content);
                return new List<TextContent> { textContent };
            }

            return new List<TextContent>();
        }

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            var messages = chatHistory.Select(msg => new
            {
                role = msg.Role.Label.ToLower(),
                content = msg.Content
            }).ToArray();

            var requestBody = new
            {
                model = _settings.ModelId,
                messages = messages,
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
                throw new HttpRequestException($"Doubao API request failed: {response.StatusCode}, {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<DoubaoResponse>(responseContent);

            if (result?.Choices?.Length > 0)
            {
                var chatMessage = new ChatMessageContent(AuthorRole.Assistant, result.Choices[0].Message.Content);
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

        /// <summary>
        /// 发送多模态消息（支持文本和图片）
        /// </summary>
        public async Task<IReadOnlyList<ChatMessageContent>> GetMultiModalChatMessageContentsAsync(
            List<MultiModalMessage> messages,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            var requestMessages = messages.Select(msg => new
            {
                role = msg.Role.ToLower(),
                content = msg.Content.Select(content => new
                {
                    type = content.Type,
                    text = content.Text,
                    image_url = content.ImageUrl != null ? new { url = content.ImageUrl.Url } : null
                }).Where(c => c.text != null || c.image_url != null).ToArray()
            }).ToArray();

            var requestBody = new
            {
                model = _settings.ModelId,
                messages = requestMessages,
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
                throw new HttpRequestException($"Doubao API request failed: {response.StatusCode}, {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<DoubaoResponse>(responseContent);

            if (result?.Choices?.Length > 0)
            {
                var chatMessage = new ChatMessageContent(AuthorRole.Assistant, result.Choices[0].Message.Content);
                return new List<ChatMessageContent> { chatMessage };
            }

            return new List<ChatMessageContent>();
        }

        /// <summary>
        /// 发送包含图片的文本生成请求
        /// </summary>
        public async Task<IReadOnlyList<TextContent>> GetTextContentsWithImageAsync(
            string prompt,
            string? imageUrl = null,
            string? base64Image = null,
            string imageFormat = "png",
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            var messages = new List<MultiModalMessage>
            {
                new MultiModalMessage
                {
                    Role = "user",
                    Content = new List<MultiModalContent>
                    {
                        new MultiModalContent { Type = "text", Text = prompt }
                    }
                }
            };

            // 添加图片内容
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                if (ImageHelper.IsValidImageUrl(imageUrl))
                {
                    messages[0].Content.Add(new MultiModalContent
                    {
                        Type = "image_url",
                        ImageUrl = new ImageUrl { Url = imageUrl }
                    });
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
                    messages[0].Content.Add(new MultiModalContent
                    {
                        Type = "image_url",
                        ImageUrl = new ImageUrl { Url = dataUrl }
                    });
                }
                else
                {
                    throw new ArgumentException("提供的base64字符串格式无效", nameof(base64Image));
                }
            }

            var chatResults = await GetMultiModalChatMessageContentsAsync(messages, executionSettings, kernel, cancellationToken);
            return chatResults.Select(chat => new TextContent(chat.Content ?? string.Empty)).ToList();
        }
    }

    public class DoubaoResponse
    {
        public Choice[]? Choices { get; set; }
    }

    public class Choice
    {
        public Message Message { get; set; } = new();
    }

    public class Message
    {
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// 多模态消息内容类型
    /// </summary>
    public class MultiModalContent
    {
        public string Type { get; set; } = string.Empty;
        public string? Text { get; set; }
        public ImageUrl? ImageUrl { get; set; }
    }

    /// <summary>
    /// 图片URL信息
    /// </summary>
    public class ImageUrl
    {
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// 多模态消息类
    /// </summary>
    public class MultiModalMessage
    {
        public string Role { get; set; } = string.Empty;
        public List<MultiModalContent> Content { get; set; } = new();
    }

    /// <summary>
    /// 图片处理辅助类
    /// </summary>
    public static class ImageHelper
    {
        /// <summary>
        /// 验证URL是否为有效的图片URL
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
        /// 验证base64图片字符串是否有效
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
        /// 创建base64图片URL
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
