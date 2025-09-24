using System.Net;
using System.Text.Json;
using ExamRecognitionSystem.DTOs;

namespace ExamRecognitionSystem.Middleware;

/// <summary>
/// 全局异常处理中间件
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理请求 {Path} 时发生未处理的异常", context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        
        var response = exception switch
        {
            ArgumentException or ArgumentNullException => new ApiResponse<object>
            {
                Success = false,
                Message = "无效的请求参数",
                Errors = new List<string> { exception.Message }
            },
            FileNotFoundException => new ApiResponse<object>
            {
                Success = false,
                Message = "请求的文件未找到",
                Errors = new List<string> { exception.Message }
            },
            UnauthorizedAccessException => new ApiResponse<object>
            {
                Success = false,
                Message = "访问被拒绝",
                Errors = new List<string> { "您没有权限访问此资源" }
            },
            TimeoutException => new ApiResponse<object>
            {
                Success = false,
                Message = "操作超时",
                Errors = new List<string> { "操作完成时间过长" }
            },
            OperationCanceledException => new ApiResponse<object>
            {
                Success = false,
                Message = "操作已取消",
                Errors = new List<string> { "操作被用户或系统取消" }
            },
            InvalidOperationException => new ApiResponse<object>
            {
                Success = false,
                Message = "无效操作",
                Errors = new List<string> { exception.Message }
            },
            NotSupportedException => new ApiResponse<object>
            {
                Success = false,
                Message = "不支持的操作",
                Errors = new List<string> { exception.Message }
            },
            _ => new ApiResponse<object>
            {
                Success = false,
                Message = "发生内部服务器错误",
                Errors = new List<string> { "请稍后重试或联系支持" }
            }
        };

        context.Response.StatusCode = exception switch
        {
            ArgumentException or ArgumentNullException => (int)HttpStatusCode.BadRequest,
            FileNotFoundException => (int)HttpStatusCode.NotFound,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            TimeoutException => (int)HttpStatusCode.RequestTimeout,
            OperationCanceledException => (int)HttpStatusCode.BadRequest,
            InvalidOperationException => (int)HttpStatusCode.BadRequest,
            NotSupportedException => (int)HttpStatusCode.BadRequest,
            _ => (int)HttpStatusCode.InternalServerError
        };

        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        await context.Response.WriteAsync(jsonResponse);
    }
}

/// <summary>
/// 请求/响应日志记录中间件
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N")[..8];
        
        // 记录请求
        _logger.LogInformation("请求 {RequestId} 开始：{Method} {Path} 来自 {RemoteIpAddress}",
            requestId,
            context.Request.Method,
            context.Request.Path,
            context.Connection.RemoteIpAddress);

        // Add request ID to response headers
        context.Response.Headers["X-Request-ID"] = requestId;

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            
            // 记录响应
            _logger.LogInformation("请求 {RequestId} 完成：{StatusCode} 耗时 {ElapsedMs}ms",
                requestId,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }
}

/// <summary>
/// API rate limiting middleware
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly Dictionary<string, ClientRequestInfo> _clients;
    private readonly object _lock = new object();
    private readonly Timer _cleanupTimer;

    // 速率限制配置
    private const int MaxRequestsPerMinute = 60;
    private const int MaxConcurrentUploads = 5;

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _clients = new Dictionary<string, ClientRequestInfo>();
        
        // 每分钟清理过期条目
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientIdentifier(context);
        var isUploadRequest = context.Request.Path.StartsWithSegments("/api/fileupload/upload", StringComparison.OrdinalIgnoreCase);

        if (IsRateLimited(clientId, isUploadRequest))
        {
            _logger.LogWarning("客户端 {ClientId} 在路径 {Path} 上超出速率限制", clientId, context.Request.Path);
            
            context.Response.StatusCode = 429; // 请求过多
            context.Response.Headers["Retry-After"] = "60";
            
            var response = new ApiResponse<object>
            {
                Success = false,
                Message = "超出速率限制",
                Errors = new List<string> { "请求过多。请稍等后再试。" }
            };

            var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(jsonResponse);
            return;
        }

        await _next(context);
    }

    private string GetClientIdentifier(HttpContext context)
    {
        // Use IP address as client identifier (in production, consider using user ID or API key)
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private bool IsRateLimited(string clientId, bool isUploadRequest)
    {
        lock (_lock)
        {
            if (!_clients.TryGetValue(clientId, out var clientInfo))
            {
                clientInfo = new ClientRequestInfo();
                _clients[clientId] = clientInfo;
            }

            var now = DateTime.UtcNow;

            // 清理旧请求（超过 1 分钟的）
            clientInfo.RequestTimes.RemoveAll(time => now - time > TimeSpan.FromMinutes(1));

            // 检查一般速率限制
            if (clientInfo.RequestTimes.Count >= MaxRequestsPerMinute)
            {
                return true;
            }

            // 检查并发上传限制
            if (isUploadRequest && clientInfo.ConcurrentUploads >= MaxConcurrentUploads)
            {
                return true;
            }

            // 更新跟踪
            clientInfo.RequestTimes.Add(now);
            if (isUploadRequest)
            {
                clientInfo.ConcurrentUploads++;
                
                // 安排上传完成跟踪（简化版）
                _ = Task.Delay(TimeSpan.FromMinutes(10)).ContinueWith(_ =>
                {
                    lock (_lock)
                    {
                        if (_clients.TryGetValue(clientId, out var info))
                        {
                            info.ConcurrentUploads = Math.Max(0, info.ConcurrentUploads - 1);
                        }
                    }
                });
            }

            clientInfo.LastActivity = now;
            return false;
        }
    }

    private void CleanupExpiredEntries(object? state)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(5);
            var expiredClients = _clients
                .Where(kvp => kvp.Value.LastActivity < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var clientId in expiredClients)
            {
                _clients.Remove(clientId);
            }

            if (expiredClients.Count > 0)
            {
                _logger.LogDebug("清理了 {Count} 个过期的客户端速率限制条目", expiredClients.Count);
            }
        }
    }

    private class ClientRequestInfo
    {
        public List<DateTime> RequestTimes { get; } = new List<DateTime>();
        public int ConcurrentUploads { get; set; } = 0;
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    }
}

/// <summary>
/// 设置安全头的中间件
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 添加安全头
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'";

        // 移除服务器头
        context.Response.Headers.Remove("Server");

        await _next(context);
    }
}