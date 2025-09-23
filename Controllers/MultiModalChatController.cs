using Microsoft.AspNetCore.Mvc;
using ExamRecognitionSystem.DTOs;
using ExamRecognitionSystem.Extensions;
using ExamRecognitionSystem.Models;

namespace ExamRecognitionSystem.Controllers;

/// <summary>
/// 多模态聊天控制器 - 支持文本和图片输入
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MultiModalChatController : ControllerBase
{
    private readonly DoubaoTextGenerationService _doubaoService;
    private readonly ILogger<MultiModalChatController> _logger;

    public MultiModalChatController(
        DoubaoTextGenerationService doubaoService,
        ILogger<MultiModalChatController> logger)
    {
        _doubaoService = doubaoService;
        _logger = logger;
    }

    /// <summary>
    /// 发送多模态聊天消息（支持文本+图片）
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
            _logger.LogInformation("收到多模态聊天请求: {Prompt}, 图片URL: {ImageUrl}, Base64图片: {HasBase64}",
                request.Prompt, request.ImageUrl, !string.IsNullOrEmpty(request.Base64Image));

            // 验证请求参数
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return BadRequest(ApiResponse<MultiModalChatResponse>.ErrorResult(
                "提示文本不能为空", new List<string> { "INVALID_PROMPT" }));
            }

            // 验证图片输入（至少需要一种图片输入方式）
            if (string.IsNullOrWhiteSpace(request.ImageUrl) && string.IsNullOrWhiteSpace(request.Base64Image))
            {
                return BadRequest(ApiResponse<MultiModalChatResponse>.ErrorResult(
                "请提供图片URL或Base64编码的图片数据", new List<string> { "NO_IMAGE_PROVIDED" }));
            }

            // 验证图片URL格式
            if (!string.IsNullOrWhiteSpace(request.ImageUrl))
            {
                if (!ImageHelper.IsValidImageUrl(request.ImageUrl))
                {
                    return BadRequest(ApiResponse<MultiModalChatResponse>.ErrorResult(
                    "无效的图片URL格式", new List<string> { "INVALID_IMAGE_URL" }));
                }
            }

            // 验证Base64图片格式
            if (!string.IsNullOrWhiteSpace(request.Base64Image))
            {
                if (!ImageHelper.IsValidBase64Image(request.Base64Image))
                {
                    return BadRequest(ApiResponse<MultiModalChatResponse>.ErrorResult(
                    "无效的Base64图片格式", new List<string> { "INVALID_BASE64_IMAGE" }));
                }
            }

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
            if (!string.IsNullOrWhiteSpace(request.ImageUrl))
            {
                messages[0].Content.Add(new MultiModalContent
                {
                    Type = "image_url",
                    ImageUrl = new ImageUrl { Url = request.ImageUrl }
                });
            }
            else if (!string.IsNullOrWhiteSpace(request.Base64Image))
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

            if (result?.Count > 0)
            {
                var response = new MultiModalChatResponse
                {
                    Success = true,
                    Content = result.First().Content ?? string.Empty,
                    Timestamp = DateTime.UtcNow
                };

                _logger.LogInformation("多模态聊天请求成功完成，响应长度: {Length}", response.Content.Length);
                return Ok(ApiResponse<MultiModalChatResponse>.SuccessResult(response));
            }
            else
            {
                _logger.LogWarning("豆包服务返回空响应");
                return StatusCode(500, ApiResponse<MultiModalChatResponse>.ErrorResult(
                "服务返回空响应", new List<string> { "EMPTY_RESPONSE" }));
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "调用豆包API时发生网络错误");
            return StatusCode(502, ApiResponse<MultiModalChatResponse>.ErrorResult(
            "网络连接错误，请稍后重试", new List<string> { "NETWORK_ERROR" }));
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "豆包API调用超时");
            return StatusCode(408, ApiResponse<MultiModalChatResponse>.ErrorResult(
            "请求超时，请稍后重试", new List<string> { "REQUEST_TIMEOUT" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理多模态聊天请求时发生未知错误");
            return StatusCode(500, ApiResponse<MultiModalChatResponse>.ErrorResult(
            "服务器内部错误", new List<string> { "INTERNAL_ERROR" }));
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

            var response = new ImageValidationResponse();

            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                if (ImageHelper.IsValidImageUrl(imageUrl))
                {
                    response.IsValid = true;
                    response.ImageType = "URL";
                }
                else
                {
                    response.IsValid = false;
                    response.ErrorMessage = "无效的图片URL格式";
                }
            }
            else if (!string.IsNullOrWhiteSpace(base64Image))
            {
                if (ImageHelper.IsValidBase64Image(base64Image))
                {
                    response.IsValid = true;
                    response.ImageType = "Base64";
                }
                else
                {
                    response.IsValid = false;
                    response.ErrorMessage = "无效的Base64图片格式";
                }
            }
            else
            {
                response.IsValid = false;
                response.ErrorMessage = "请提供图片URL或Base64图片数据";
            }

            return Ok(ApiResponse<ImageValidationResponse>.SuccessResult(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证图片时发生错误");
            return StatusCode(500, ApiResponse<ImageValidationResponse>.ErrorResult(
                "验证图片时发生错误", new List<string> { "VALIDATION_ERROR" }));
        }
    }
}