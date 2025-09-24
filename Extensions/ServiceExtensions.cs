using DocumentFormat.OpenXml.Wordprocessing;
using ExamRecognitionSystem.Models;
using ExamRecognitionSystem.Plugins;
using ExamRecognitionSystem.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.TextGeneration;

namespace ExamRecognitionSystem.Extensions;

/// <summary>
/// 配置 Semantic Kernel 服务的扩展方法
/// </summary>
public static class SemanticKernelExtensions
{
    public static IServiceCollection ConfigureSemanticKernel(this IServiceCollection services, IConfiguration configuration)
    {
        // 配置 AI 提供商设置
        var aiProviderSettings = configuration.GetSection("AIProvider").Get<AIProviderSettings>()
            ?? throw new InvalidOperationException("AIProvider configuration is missing");

        services.AddSingleton(aiProviderSettings);

        // 配置 Semantic Kernel
        services.AddSingleton<Kernel>(serviceProvider =>
        {
            var builder = Kernel.CreateBuilder();

            // 根据选择的提供商进行配置
            if (aiProviderSettings.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                // 配置 OpenAI
                if (string.IsNullOrEmpty(aiProviderSettings.OpenAI.ApiKey))
                {
                    throw new InvalidOperationException("OpenAI API Key is required when using OpenAI provider");
                }

                builder.AddOpenAIChatCompletion(
                    modelId: aiProviderSettings.OpenAI.ModelId,
                    apiKey: aiProviderSettings.OpenAI.ApiKey,
                    orgId: aiProviderSettings.OpenAI.Organization);
            }
            else if (aiProviderSettings.Provider.Equals("Doubao", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(aiProviderSettings.Doubao.ApiKey))
                {
                    throw new InvalidOperationException("Doubao API Key is required when using Doubao provider");
                }

                var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("DoubaoClient");


                var doubaoService = new DoubaoTextGenerationService(httpClient, aiProviderSettings.Doubao);


                builder.Services.AddSingleton<ITextGenerationService>(doubaoService);
                builder.Services.AddSingleton<IChatCompletionService>(doubaoService);
            }
            else
            {
                // 配置 Ollama（默认）
                var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("OllamaClient");

                builder.AddOllamaChatCompletion(aiProviderSettings.Ollama.ModelId, httpClient);
            }

            // 添加插件
            builder.Plugins.AddFromType<QuestionParsingPlugin>();
            builder.Plugins.AddFromType<ImageAnalysisPlugin>();
            builder.Plugins.AddFromType<FileProcessingPlugin>();

            return builder.Build();
        });

        // 单独注册聊天完成服务以便直接访问
        services.AddSingleton<IChatCompletionService>(serviceProvider =>
        {
            var kernel = serviceProvider.GetRequiredService<Kernel>();
            return kernel.GetRequiredService<IChatCompletionService>();
        });

        // 注册 Semantic Kernel 服务
        services.AddScoped<ISemanticKernelService, SemanticKernelService>();

        return services;
    }
}

/// <summary>
/// 配置应用程序服务的扩展方法
/// </summary>
public static class ApplicationServiceExtensions
{
    public static IServiceCollection ConfigureApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 配置文件上传设置
        services.Configure<FileUploadSettings>(configuration.GetSection("FileUpload"));

        // 配置线程设置
        services.Configure<ThreadPoolConfig>(configuration.GetSection("Threading"));

        // 注册应用程序服务
        services.AddScoped<IFileUploadService, FileUploadService>();
        services.AddScoped<IQuestionParsingService, QuestionParsingService>();
        services.AddSingleton<IThreadPoolManager, ThreadPoolManager>();
        services.AddSingleton<IProgressMonitoringService, ProgressMonitoringService>();
        services.AddScoped<IFileProcessingService, FileProcessingService>();
        services.AddSingleton<IPerformanceMonitoringService, PerformanceMonitoringService>();

        // 为外部服务配置 HTTP 客户端
        services.AddHttpClient("OllamaClient", client =>
        {
            var aiProviderSettings = configuration.GetSection("AIProvider").Get<AIProviderSettings>();
            if (aiProviderSettings?.Ollama != null)
            {
                client.BaseAddress = new Uri(aiProviderSettings.Ollama.BaseUrl);
                client.Timeout = TimeSpan.FromMinutes(aiProviderSettings.Ollama.RequestTimeout);
                // Ollama 不需要授权头
            }
        });

        // 为豆包配置 HTTP 客户端
        services.AddHttpClient("DoubaoClient", client =>
        {
            var aiProviderSettings = configuration.GetSection("AIProvider").Get<AIProviderSettings>();
            if (aiProviderSettings?.Doubao != null)
            {
                client.BaseAddress = new Uri(aiProviderSettings.Doubao.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(aiProviderSettings.Doubao.RequestTimeout);
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {aiProviderSettings.Doubao.ApiKey}");
            }
        });

        // 为对话历史配置内存缓存
        services.AddMemoryCache();

        // 配置后台服务
        services.AddHostedService<ProcessingCleanupService>();

        return services;
    }
}

/// <summary>
/// 文件上传配置设置
/// </summary>
public class FileUploadSettings
{
    public long MaxFileSize { get; set; } = 10 * 1024 * 1024; // 10MB
    public string[] AllowedExtensions { get; set; } = { ".pdf", ".docx", ".jpeg", ".jpg", ".png" };
    public string TempDirectory { get; set; } = "UploadTemp";
    public int MaxConcurrentUploads { get; set; } = 5;
    public TimeSpan FileRetentionPeriod { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// 性能监控的扩展方法
/// </summary>
public static class PerformanceExtensions
{
    public static long GetMemoryUsage()
    {
        return GC.GetTotalMemory(false);
    }

    public static double GetCpuUsage()
    {
        // 简化的 CPU 使用率计算
        // 在实际实现中，您可能会使用 PerformanceCounter 或更复杂的方法
        return Environment.ProcessorCount > 0 ?
            Math.Min(100.0, (Environment.WorkingSet / (1024.0 * 1024.0)) / Environment.ProcessorCount) : 0.0;
    }

    public static int GetActiveThreadCount()
    {
        return System.Threading.ThreadPool.ThreadCount;
    }
}

/// <summary>
/// 字符串操作的扩展方法
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// 如果字符串超过指定长度，则用省略号截断
    /// </summary>
    /// <param name="value">要截断的字符串</param>
    /// <param name="maxLength">截断前的最大长度</param>
    /// <returns>如果需要，返回带省略号的截断字符串</returns>
    public static string TruncateWithEllipsis(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength - 3) + "...";
    }

    /// <summary>
    /// 检查字符串是否为有效的 Base64 格式
    /// </summary>
    /// <param name="value">要检查的字符串</param>
    /// <returns>如果是有效的 Base64 字符串则返回 true</returns>
    public static bool IsBase64String(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        try
        {
            Convert.FromBase64String(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 根据文件扩展名获取文件类型
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <returns>对应的文件类型枚举</returns>
    public static FileType GetFileTypeFromExtension(this string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => FileType.Pdf,
            ".docx" => FileType.Docx,
            ".jpeg" or ".jpg" => FileType.Jpeg,
            ".png" => FileType.Png,
            _ => FileType.Unknown
        };
    }
}

/// <summary>
/// 验证相关的扩展方法
/// </summary>
public static class ValidationExtensions
{
    /// <summary>
    /// 检查文件扩展名是否在允许的扩展名列表中
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="allowedExtensions">允许的扩展名数组</param>
    /// <returns>如果扩展名有效则返回 true</returns>
    public static bool IsValidFileExtension(this string fileName, string[] allowedExtensions)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return allowedExtensions.Contains(extension);
    }

    /// <summary>
    /// 检查文件大小是否在有效范围内
    /// </summary>
    /// <param name="fileSize">文件大小</param>
    /// <param name="maxFileSize">最大允许文件大小</param>
    /// <returns>如果文件大小有效则返回 true</returns>
    public static bool IsValidFileSize(this long fileSize, long maxFileSize)
    {
        return fileSize > 0 && fileSize <= maxFileSize;
    }

    /// <summary>
    /// 验证上传的文件是否符合要求
    /// </summary>
    /// <param name="file">上传的文件</param>
    /// <param name="settings">文件上传设置</param>
    /// <returns>验证错误列表，如果为空则表示验证通过</returns>
    public static List<string> ValidateUploadedFile(this IFormFile file, FileUploadSettings settings)
    {
        var errors = new List<string>();

        if (file == null)
        {
            errors.Add("未提供文件");
            return errors;
        }

        if (string.IsNullOrEmpty(file.FileName))
        {
            errors.Add("文件名是必需的");
        }

        if (file.Length <= 0)
        {
            errors.Add("文件为空");
        }
        else if (!file.Length.IsValidFileSize(settings.MaxFileSize))
        {
            errors.Add($"文件大小超过最大允许大小 {settings.MaxFileSize / (1024 * 1024)}MB");
        }

        if (!file.FileName.IsValidFileExtension(settings.AllowedExtensions))
        {
            errors.Add($"不允许的文件类型。允许的类型：{string.Join(", ", settings.AllowedExtensions)}");
        }

        return errors;
    }
}