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
}
