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
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // 配置JSON序列化选项
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.WriteIndented = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure custom services
builder.Services.ConfigureApplicationServices(builder.Configuration);
builder.Services.ConfigureSemanticKernel(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ����Ĭ���ļ�ӳ�� - ������ UseStaticFiles ֮ǰ
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

// ϵͳ��Ϣ�˵�
app.MapGet("/info", () => new
{
Application = "Semantic Kernel �Ծ�����ϵͳ",
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