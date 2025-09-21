using Azure.AI.OpenAI;
using ExamRecognitionSystem.Models;
using ExamRecognitionSystem.Plugins;
using ExamRecognitionSystem.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ExamRecognitionSystem.Extensions;

/// <summary>
/// Extension methods for configuring Semantic Kernel services
/// </summary>
public static class SemanticKernelExtensions
{
    public static IServiceCollection ConfigureSemanticKernel(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure Doubao settings
        var doubaoSettings = configuration.GetSection("DoubaoSettings").Get<DoubaoSettings>()
            ?? throw new InvalidOperationException("DoubaoSettings configuration is missing");

        services.AddSingleton(doubaoSettings);

        // Configure Semantic Kernel
        services.AddSingleton<Kernel>(serviceProvider =>
        {
            var builder = Kernel.CreateBuilder();

            OpenAIClientOptions clientOptions = new OpenAIClientOptions
            {
            };
            // Add Doubao chat completion service (using OpenAI-compatible API)
            OpenAIClient openAIClient = new OpenAIClient(doubaoSettings.ApiKey, clientOptions);

            builder.AddOpenAIChatCompletion(doubaoSettings.ModelId, openAIClient);

            // Add plugins
            builder.Plugins.AddFromType<QuestionParsingPlugin>();
            builder.Plugins.AddFromType<ImageAnalysisPlugin>();
            builder.Plugins.AddFromType<FileProcessingPlugin>();

            return builder.Build();
        });

        // Register chat completion service separately for direct access
        services.AddSingleton<IChatCompletionService>(serviceProvider =>
        {
            var kernel = serviceProvider.GetRequiredService<Kernel>();
            return kernel.GetRequiredService<IChatCompletionService>();
        });

        // Register Semantic Kernel service
        services.AddScoped<ISemanticKernelService, SemanticKernelService>();

        return services;
    }
}

/// <summary>
/// Extension methods for configuring application services
/// </summary>
public static class ApplicationServiceExtensions
{
    public static IServiceCollection ConfigureApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure file upload settings
        services.Configure<FileUploadSettings>(configuration.GetSection("FileUpload"));

        // Configure threading settings
        services.Configure<ThreadPoolConfig>(configuration.GetSection("Threading"));

        // Register application services
        services.AddScoped<IFileUploadService, FileUploadService>();
        services.AddScoped<IQuestionParsingService, QuestionParsingService>();
        services.AddSingleton<IThreadPoolManager, ThreadPoolManager>();
        services.AddSingleton<IProgressMonitoringService, ProgressMonitoringService>();
        services.AddScoped<IFileProcessingService, FileProcessingService>();
        services.AddSingleton<IPerformanceMonitoringService, PerformanceMonitoringService>();

        // Configure HTTP client for external services
        services.AddHttpClient("DoubaoClient", client =>
        {
            var doubaoSettings = configuration.GetSection("DoubaoSettings").Get<DoubaoSettings>();
            if (doubaoSettings != null)
            {
                client.BaseAddress = new Uri(doubaoSettings.BaseUrl);
                client.Timeout = doubaoSettings.RequestTimeout;
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {doubaoSettings.ApiKey}");
            }
        });

        // Configure memory cache for conversation history
        services.AddMemoryCache();

        // Configure background services
        services.AddHostedService<ProcessingCleanupService>();

        return services;
    }
}

/// <summary>
/// File upload configuration settings
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
/// Extension methods for performance monitoring
/// </summary>
public static class PerformanceExtensions
{
    public static long GetMemoryUsage()
    {
        return GC.GetTotalMemory(false);
    }

    public static double GetCpuUsage()
    {
        // Simplified CPU usage calculation
        // In a real implementation, you might use PerformanceCounter or more sophisticated methods
        return Environment.ProcessorCount > 0 ?
            Math.Min(100.0, (Environment.WorkingSet / (1024.0 * 1024.0)) / Environment.ProcessorCount) : 0.0;
    }

    public static int GetActiveThreadCount()
    {
        return System.Threading.ThreadPool.ThreadCount;
    }
}

/// <summary>
/// Extension methods for string operations
/// </summary>
public static class StringExtensions
{
    public static string TruncateWithEllipsis(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength - 3) + "...";
    }

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
/// Extension methods for validation
/// </summary>
public static class ValidationExtensions
{
    public static bool IsValidFileExtension(this string fileName, string[] allowedExtensions)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return allowedExtensions.Contains(extension);
    }

    public static bool IsValidFileSize(this long fileSize, long maxFileSize)
    {
        return fileSize > 0 && fileSize <= maxFileSize;
    }

    public static List<string> ValidateUploadedFile(this IFormFile file, FileUploadSettings settings)
    {
        var errors = new List<string>();

        if (file == null)
        {
            errors.Add("No file provided");
            return errors;
        }

        if (string.IsNullOrEmpty(file.FileName))
        {
            errors.Add("File name is required");
        }

        if (file.Length <= 0)
        {
            errors.Add("File is empty");
        }
        else if (!file.Length.IsValidFileSize(settings.MaxFileSize))
        {
            errors.Add($"File size exceeds maximum allowed size of {settings.MaxFileSize / (1024 * 1024)}MB");
        }

        if (!file.FileName.IsValidFileExtension(settings.AllowedExtensions))
        {
            errors.Add($"File type not allowed. Allowed types: {string.Join(", ", settings.AllowedExtensions)}");
        }

        return errors;
    }
}