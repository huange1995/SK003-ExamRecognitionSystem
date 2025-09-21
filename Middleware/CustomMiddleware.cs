using System.Net;
using System.Text.Json;
using ExamRecognitionSystem.DTOs;

namespace ExamRecognitionSystem.Middleware;

/// <summary>
/// Middleware for global exception handling
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
            _logger.LogError(ex, "An unhandled exception occurred while processing request {Path}", context.Request.Path);
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
                Message = "Invalid request parameters",
                Errors = new List<string> { exception.Message }
            },
            FileNotFoundException => new ApiResponse<object>
            {
                Success = false,
                Message = "Requested file not found",
                Errors = new List<string> { exception.Message }
            },
            UnauthorizedAccessException => new ApiResponse<object>
            {
                Success = false,
                Message = "Access denied",
                Errors = new List<string> { "You don't have permission to access this resource" }
            },
            TimeoutException => new ApiResponse<object>
            {
                Success = false,
                Message = "Operation timed out",
                Errors = new List<string> { "The operation took too long to complete" }
            },
            OperationCanceledException => new ApiResponse<object>
            {
                Success = false,
                Message = "Operation was cancelled",
                Errors = new List<string> { "The operation was cancelled by user or system" }
            },
            InvalidOperationException => new ApiResponse<object>
            {
                Success = false,
                Message = "Invalid operation",
                Errors = new List<string> { exception.Message }
            },
            NotSupportedException => new ApiResponse<object>
            {
                Success = false,
                Message = "Operation not supported",
                Errors = new List<string> { exception.Message }
            },
            _ => new ApiResponse<object>
            {
                Success = false,
                Message = "An internal server error occurred",
                Errors = new List<string> { "Please try again later or contact support" }
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
/// Middleware for request/response logging
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
        
        // Log request
        _logger.LogInformation("Request {RequestId} started: {Method} {Path} from {RemoteIpAddress}",
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
            
            // Log response
            _logger.LogInformation("Request {RequestId} completed: {StatusCode} in {ElapsedMs}ms",
                requestId,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }
}

/// <summary>
/// Middleware for API rate limiting
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly Dictionary<string, ClientRequestInfo> _clients;
    private readonly object _lock = new object();
    private readonly Timer _cleanupTimer;

    // Rate limiting configuration
    private const int MaxRequestsPerMinute = 60;
    private const int MaxConcurrentUploads = 5;

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _clients = new Dictionary<string, ClientRequestInfo>();
        
        // Cleanup expired entries every minute
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientIdentifier(context);
        var isUploadRequest = context.Request.Path.StartsWithSegments("/api/fileupload/upload", StringComparison.OrdinalIgnoreCase);

        if (IsRateLimited(clientId, isUploadRequest))
        {
            _logger.LogWarning("Rate limit exceeded for client {ClientId} on path {Path}", clientId, context.Request.Path);
            
            context.Response.StatusCode = 429; // Too Many Requests
            context.Response.Headers["Retry-After"] = "60";
            
            var response = new ApiResponse<object>
            {
                Success = false,
                Message = "Rate limit exceeded",
                Errors = new List<string> { "Too many requests. Please wait before trying again." }
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

            // Clean up old requests (older than 1 minute)
            clientInfo.RequestTimes.RemoveAll(time => now - time > TimeSpan.FromMinutes(1));

            // Check general rate limit
            if (clientInfo.RequestTimes.Count >= MaxRequestsPerMinute)
            {
                return true;
            }

            // Check concurrent upload limit
            if (isUploadRequest && clientInfo.ConcurrentUploads >= MaxConcurrentUploads)
            {
                return true;
            }

            // Update tracking
            clientInfo.RequestTimes.Add(now);
            if (isUploadRequest)
            {
                clientInfo.ConcurrentUploads++;
                
                // Schedule upload completion tracking (simplified)
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
                _logger.LogDebug("Cleaned up {Count} expired client rate limit entries", expiredClients.Count);
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
/// Middleware for setting security headers
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
        // Add security headers
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'";

        // Remove server header
        context.Response.Headers.Remove("Server");

        await _next(context);
    }
}