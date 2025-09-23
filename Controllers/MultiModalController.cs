using Microsoft.AspNetCore.Mvc;
using ExamRecognitionSystem.DTOs;
using ExamRecognitionSystem.Models;
using ExamRecognitionSystem.Extensions;

namespace ExamRecognitionSystem.Controllers;

/// <summary>
/// 多模态聊天控制器，支持文本和图片输入
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MultiModalController : ControllerBase
{
    private readonly DoubaoTextGenerationService _doubaoService;
    private readonly ILogger<MultiModalController> _logger;

    public MultiModalController(
        DoubaoTextGenerationService doubaoService,
        ILogger<MultiModalController> logger)
    {
        _doubaoService = doubaoService;
        _logger = logger;
    }

    /// <summary>
    /// 发送多模态聊天消息（支持图片URL或Base64图片）
    /// </summary>
    /// <param name="request">多模态聊天请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聊天响应</returns>
    [HttpPost("chat")]
    public async Task<ActionResult<ApiResponse<MultiModalChatResponse>>> SendMultiModalMessage(
        [FromBody] MultiModalChatRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Received multimodal chat request with prompt: {Prompt}", 
                request.Prompt.Length > 100 ? request.Prompt[..100] + "..." : request.Prompt);

            // 验证请求
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return BadRequest(ApiResponse<MultiModalChatResponse>.ErrorResult(
                    "Prompt cannot be empty"));
            }

            // 验证图片输入（只能有一种）
            if (!string.IsNullOrEmpty(request.ImageUrl) && !string.IsNullOrEmpty(request.Base64Image))
            {
                return BadRequest(ApiResponse<MultiModalChatResponse>.ErrorResult(
                    "Cannot provide both ImageUrl and Base64Image. Please use only one."));
            }

            string? content = null;

            // 根据输入类型调用相应的方法
            if (!string.IsNullOrEmpty(request.ImageUrl) || !string.IsNullOrEmpty(request.Base64Image))
            {
                // 构建多模态消息
                var messages = new List<MultiModalMessage>
                {
                    new MultiModalMessage
                    {
                        Role = "user",
                        Content = new List<MultiModalContent>
                        {
                            new MultiModalContent { Type = "text", Text = request.Prompt }
                        }
                    }
                };

                // 添加图片内容
                if (!string.IsNullOrEmpty(request.ImageUrl))
                {
                    messages[0].Content.Add(new MultiModalContent
                    {
                        Type = "image_url",
                        ImageUrl = new ImageUrl { Url = request.ImageUrl }
                    });
                }
                else if (!string.IsNullOrEmpty(request.Base64Image))
                {
                    var dataUrl = ImageHelper.CreateBase64ImageUrl(request.Base64Image);
                    messages[0].Content.Add(new MultiModalContent
                    {
                        Type = "image_url",
                        ImageUrl = new ImageUrl { Url = dataUrl }
                    });
                }

                // 调用豆包多模态服务
                var result = await _doubaoService.GetMultiModalChatMessageContentsAsync(
                    messages,
                    null,
                    null,
                    cancellationToken);
                
                content = result.FirstOrDefault()?.Content;
            }
            else
            {
                // 纯文本聊天
                var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
                chatHistory.AddUserMessage(request.Prompt);
                
                var result = await _doubaoService.GetChatMessageContentsAsync(
                    chatHistory, 
                    new Microsoft.SemanticKernel.PromptExecutionSettings
                    {
                        ExtensionData = new Dictionary<string, object>
                        {
                            ["temperature"] = request.Temperature,
                            ["max_tokens"] = request.MaxTokens
                        }
                    }, 
                    cancellationToken: cancellationToken);
                
                content = result.FirstOrDefault()?.Content;
            }

            var response = new MultiModalChatResponse
            {
                Success = true,
                Content = content ?? "No response generated",
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Successfully processed multimodal chat request");
            return Ok(ApiResponse<MultiModalChatResponse>.SuccessResult(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing multimodal chat request: {Error}", ex.Message);
            
            var errorResponse = new MultiModalChatResponse
            {
                Success = false,
                Content = string.Empty,
                ErrorMessage = ex.Message,
                Timestamp = DateTime.UtcNow
            };

            return StatusCode(500, ApiResponse<MultiModalChatResponse>.ErrorResult(
                "Internal server error", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 验证图片URL或Base64格式
    /// </summary>
    /// <param name="imageUrl">图片URL</param>
    /// <param name="base64Image">Base64图片数据</param>
    /// <returns>验证结果</returns>
    [HttpPost("validate-image")]
    public ActionResult<ApiResponse<ImageValidationResponse>> ValidateImage(
        [FromBody] dynamic request)
    {
        try
        {
            string? imageUrl = request?.imageUrl;
            string? base64Image = request?.base64Image;

            if (!string.IsNullOrEmpty(imageUrl) && !string.IsNullOrEmpty(base64Image))
            {
                return BadRequest(ApiResponse<ImageValidationResponse>.ErrorResult(
                    "Cannot validate both ImageUrl and Base64Image. Please provide only one."));
            }

            var response = new ImageValidationResponse();

            if (!string.IsNullOrEmpty(imageUrl))
            {
                // 验证图片URL
                response.IsValid = ImageHelper.IsValidImageUrl(imageUrl);
                response.ErrorMessage = response.IsValid ? null : "Invalid image URL format";
                response.ImageType = "URL";
            }
            else if (!string.IsNullOrEmpty(base64Image))
            {
                // 验证Base64图片
                response.IsValid = ImageHelper.IsValidBase64Image(base64Image);
                response.ErrorMessage = response.IsValid ? null : "Invalid Base64 image format";
                response.ImageType = "Base64";
            }
            else
            {
                return BadRequest(ApiResponse<ImageValidationResponse>.ErrorResult(
                    "Please provide either imageUrl or base64Image"));
            }

            return Ok(ApiResponse<ImageValidationResponse>.SuccessResult(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating image: {Error}", ex.Message);
            
            var errorResponse = new ImageValidationResponse
            {
                IsValid = false,
                ErrorMessage = ex.Message
            };

            return StatusCode(500, ApiResponse<ImageValidationResponse>.ErrorResult(
                "Internal server error", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 获取支持的图片格式信息
    /// </summary>
    /// <returns>支持的图片格式列表</returns>
    [HttpGet("supported-formats")]
    public ActionResult<ApiResponse<object>> GetSupportedFormats()
    {
        var supportedFormats = new
        {
            ImageFormats = new[] { "png", "jpg", "jpeg", "gif", "bmp", "webp" },
            MaxImageSize = "10MB",
            SupportedInputTypes = new[] { "URL", "Base64" },
            Examples = new
            {
                ImageUrl = "https://example.com/image.jpg",
                Base64Format = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg=="
            }
        };

        return Ok(ApiResponse<object>.SuccessResult(supportedFormats));
    }
}