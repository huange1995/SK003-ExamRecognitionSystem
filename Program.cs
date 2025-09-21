using ExamRecognitionSystem.Extensions;
using ExamRecognitionSystem.Middleware;
using ExamRecognitionSystem.Services;
using Serilog;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure custom services
builder.Services.ConfigureSemanticKernel(builder.Configuration);
builder.Services.ConfigureApplicationServices(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 配置默认文件映射 - 必须在 UseStaticFiles 之前
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = { "index.html" }
});
// Configure static files
app.UseStaticFiles();

// Add custom middleware
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseAuthorization();
app.MapControllers();

// 系统信息端点
app.MapGet("/info", () => new
{
Application = "Semantic Kernel 试卷解析系统",
Version = "1.0.0",
Environment = app.Environment.EnvironmentName,
MachineName = Environment.MachineName,
ProcessorCount = Environment.ProcessorCount,
StartTime = Process.GetCurrentProcess().StartTime,
WorkingSet = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024) + " MB"
})
.WithName("GetSystemInfo")
.WithTags("System");

try
{
    Log.Information("Starting Exam Recognition System");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}