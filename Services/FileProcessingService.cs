using ExamRecognitionSystem.Models;
using ExamRecognitionSystem.Extensions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using System.Text;

namespace ExamRecognitionSystem.Services;

/// <summary>
/// 文件处理操作接口
/// </summary>
public interface IFileProcessingService
{
    Task<string> ConvertToTextAsync(string filePath, FileType fileType, CancellationToken cancellationToken = default);
    Task<string> ConvertImageToBase64Async(string imagePath, CancellationToken cancellationToken = default);
    Task<bool> ValidateFileAsync(string filePath, FileType fileType);
    Task<List<string>> ExtractImagesFromDocumentAsync(string filePath, FileType fileType, string outputDirectory);
}

/// <summary>
/// 题目解析操作接口
/// </summary>
public interface IQuestionParsingService
{
    Task<List<ExamQuestion>> ParseQuestionsAsync(string filePath, List<int> questionNumbers, CancellationToken cancellationToken = default);
    Task<List<ExamQuestion>> ParseQuestionsFromTextAsync(string text, List<int> questionNumbers, CancellationToken cancellationToken = default);
    Task<List<ExamQuestion>> ParseQuestionsFromImageAsync(string imagePath, List<int> questionNumbers, CancellationToken cancellationToken = default);
    Task<List<ExamQuestion>> MergeQuestionSetsAsync(List<List<ExamQuestion>> questionSets);
}

/// <summary>
/// 用于处理各种文件格式并提取内容的服务
/// </summary>
public class FileProcessingService : IFileProcessingService
{
    private readonly ILogger<FileProcessingService> _logger;

    public FileProcessingService(ILogger<FileProcessingService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ConvertToTextAsync(string filePath, FileType fileType, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("正在将文件 {FilePath}（类型：{FileType}）转换为文本", filePath, fileType);

            var text = fileType switch
            {
                FileType.Pdf => await ExtractTextFromPdfAsync(filePath, cancellationToken),
                FileType.Docx => await ExtractTextFromDocxAsync(filePath, cancellationToken),
                FileType.Jpeg or FileType.Png => await ExtractTextFromImageAsync(filePath, cancellationToken),
                _ => throw new NotSupportedException($"File type {fileType} is not supported")
            };

            _logger.LogInformation("成功从文件 {FileName} 提取了 {TextLength} 个字符", 
                text.Length, Path.GetFileName(filePath));

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "将文件 {FilePath} 转换为文本时发生错误：{Error}", filePath, ex.Message);
            throw;
        }
    }

    public async Task<string> ConvertImageToBase64Async(string imagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            var base64String = Convert.ToBase64String(imageBytes);
            
            _logger.LogDebug("已将图像 {ImagePath} 转换为base64字符串（{Size} 字节）", 
                imagePath, imageBytes.Length);
            
            return base64String;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "将图像 {ImagePath} 转换为base64时发生错误：{Error}", imagePath, ex.Message);
            throw;
        }
    }

    public async Task<bool> ValidateFileAsync(string filePath, FileType fileType)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("文件 {FilePath} 不存在", filePath);
                return false;
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                _logger.LogWarning("文件 {FilePath} 为空", filePath);
                return false;
            }

            // 根据类型验证文件格式
            var isValid = fileType switch
            {
                FileType.Pdf => await ValidatePdfFileAsync(filePath),
                FileType.Docx => await ValidateDocxFileAsync(filePath),
                FileType.Jpeg or FileType.Png => await ValidateImageFileAsync(filePath),
                _ => false
            };

            _logger.LogDebug("文件 {FilePath} 验证结果：{IsValid}", filePath, isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证文件 {FilePath} 时发生错误：{Error}", filePath, ex.Message);
            return false;
        }
    }

    public async Task<List<string>> ExtractImagesFromDocumentAsync(string filePath, FileType fileType, string outputDirectory)
    {
        var extractedImages = new List<string>();

        try
        {
            Directory.CreateDirectory(outputDirectory);

            switch (fileType)
            {
                case FileType.Pdf:
                    extractedImages = await ExtractImagesFromPdfAsync(filePath, outputDirectory);
                    break;
                case FileType.Docx:
                    extractedImages = await ExtractImagesFromDocxAsync(filePath, outputDirectory);
                    break;
                default:
                    _logger.LogWarning("不支持从文件类型 {FileType} 中提取图像", fileType);
                    break;
            }

            _logger.LogInformation("从文件 {FileName} 提取了 {ImageCount} 张图像", 
                extractedImages.Count, Path.GetFileName(filePath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从文件 {FilePath} 提取图像时发生错误：{Error}", filePath, ex.Message);
        }

        return extractedImages;
    }

    private async Task<string> ExtractTextFromPdfAsync(string filePath, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();

        using var reader = new PdfReader(filePath);
        using var document = new PdfDocument(reader);
        
        for (int page = 1; page <= document.GetNumberOfPages(); page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var pdfPage = document.GetPage(page);
            var pageText = PdfTextExtractor.GetTextFromPage(pdfPage);
            text.AppendLine(pageText);
        }

        return await Task.FromResult(text.ToString());
    }

    private async Task<string> ExtractTextFromDocxAsync(string filePath, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();

        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document.Body;
        
        if (body != null)
        {
            foreach (var paragraph in body.Elements<Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                text.AppendLine(paragraph.InnerText);
            }
        }

        return await Task.FromResult(text.ToString());
    }

    private async Task<string> ExtractTextFromImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        // 对于图像文本提取，我们返回占位符，因为OCR需要额外的库
        // 在实际实现中，您需要集成Tesseract.NET等库
        var imageInfo = await GetImageInfoAsync(imagePath, cancellationToken);
        return $"[IMAGE: {imageInfo.Width}x{imageInfo.Height}, Format: {imageInfo.Format}]";
    }

    private async Task<bool> ValidatePdfFileAsync(string filePath)
    {
        try
        {
            using var reader = new PdfReader(filePath);
            using var document = new PdfDocument(reader);
            return document.GetNumberOfPages() > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ValidateDocxFileAsync(string filePath)
    {
        try
        {
            using var doc = WordprocessingDocument.Open(filePath, false);
            return doc.MainDocumentPart?.Document != null;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ValidateImageFileAsync(string filePath)
    {
        try
        {
            using var image = await Image.LoadAsync(filePath);
            return image.Width > 0 && image.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(int Width, int Height, string Format)> GetImageInfoAsync(string imagePath, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync(imagePath, cancellationToken);
        return (image.Width, image.Height, image.Metadata.DecodedImageFormat?.Name ?? "Unknown");
    }

    private async Task<List<string>> ExtractImagesFromPdfAsync(string filePath, string outputDirectory)
    {
        var extractedImages = new List<string>();
        
        // PDF图像提取很复杂，需要额外的库
        // 这是一个占位符实现
        _logger.LogInformation("PDF图像提取功能尚未实现");
        
        return await Task.FromResult(extractedImages);
    }

    private async Task<List<string>> ExtractImagesFromDocxAsync(string filePath, string outputDirectory)
    {
        var extractedImages = new List<string>();

        try
        {
            using var doc = WordprocessingDocument.Open(filePath, false);
            var imageParts = doc.MainDocumentPart?.ImageParts;
            
            if (imageParts != null)
            {
                int imageIndex = 0;
                foreach (var imagePart in imageParts)
                {
                    var extension = GetImageExtension(imagePart.ContentType);
                    var fileName = $"image_{imageIndex++}{extension}";
                    var outputPath = Path.Combine(outputDirectory, fileName);

                    using var imageStream = imagePart.GetStream();
                    using var fileStream = File.Create(outputPath);
                    await imageStream.CopyToAsync(fileStream);
                    
                    extractedImages.Add(outputPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从DOCX文件提取图像时发生错误：{Error}", ex.Message);
        }

        return extractedImages;
    }

    private string GetImageExtension(string contentType)
    {
        return contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => ".jpg"
        };
    }
}

/// <summary>
/// 使用Semantic Kernel解析题目的服务
/// </summary>
public class QuestionParsingService : IQuestionParsingService
{
    private readonly ISemanticKernelService _semanticKernelService;
    private readonly IFileProcessingService _fileProcessingService;
    private readonly ILogger<QuestionParsingService> _logger;

    public QuestionParsingService(
        ISemanticKernelService semanticKernelService,
        IFileProcessingService fileProcessingService,
        ILogger<QuestionParsingService> logger)
    {
        _semanticKernelService = semanticKernelService;
        _fileProcessingService = fileProcessingService;
        _logger = logger;
    }

    public async Task<List<ExamQuestion>> ParseQuestionsAsync(string filePath, List<int> questionNumbers, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("正在从文件 {FilePath} 解析题目，题目编号：{QuestionNumbers}",
                filePath, string.Join(", ", questionNumbers));

            var fileType = filePath.GetFileTypeFromExtension();
            
            // 首先验证文件
            if (!await _fileProcessingService.ValidateFileAsync(filePath, fileType))
            {
                throw new InvalidOperationException($"File {filePath} is not valid or corrupted");
            }

            var questions = fileType switch
            {
                FileType.Pdf or FileType.Docx => await ParseQuestionsFromDocumentAsync(filePath, fileType, questionNumbers, cancellationToken),
                FileType.Jpeg or FileType.Png => await ParseQuestionsFromImageAsync(filePath, questionNumbers, cancellationToken),
                _ => throw new NotSupportedException($"File type {fileType} is not supported")
            };

            _logger.LogInformation("成功从文件 {FileName} 解析出 {QuestionCount} 个题目",
                questions.Count, Path.GetFileName(filePath));

            return questions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从文件 {FilePath} 解析题目时发生错误：{Error}", filePath, ex.Message);
            throw;
        }
    }

    public async Task<List<ExamQuestion>> ParseQuestionsFromTextAsync(string text, List<int> questionNumbers, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("正在从文本内容解析题目，题目编号：{QuestionNumbers}",
                string.Join(", ", questionNumbers));

            return await _semanticKernelService.ExtractQuestionsFromTextAsync(text, questionNumbers, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从文本解析题目时发生错误：{Error}", ex.Message);
            throw;
        }
    }

    public async Task<List<ExamQuestion>> ParseQuestionsFromImageAsync(string imagePath, List<int> questionNumbers, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("正在从图像 {ImagePath} 解析题目，题目编号：{QuestionNumbers}",
                imagePath, string.Join(", ", questionNumbers));

            // 将图像转换为base64
            var imageBase64 = await _fileProcessingService.ConvertImageToBase64Async(imagePath, cancellationToken);

            // 创建视觉分析请求
            var request = new VisionAnalysisRequest
            {
                ImageBase64 = imageBase64,
                Prompt = "Analyze this exam paper image and extract the specified questions.",
                QuestionNumbers = questionNumbers.Select(n => n.ToString()).ToList(),
                MaxTokens = 4000,
                Temperature = 0.3
            };

            // 使用Semantic Kernel进行分析
            var response = await _semanticKernelService.AnalyzeImageAsync(request, cancellationToken);
            
            if (!response.Success)
            {
                throw new InvalidOperationException($"Vision analysis failed: {response.ErrorMessage}");
            }

            return response.ExtractedQuestions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从图像 {ImagePath} 解析题目时发生错误：{Error}", imagePath, ex.Message);
            throw;
        }
    }

    public async Task<List<ExamQuestion>> MergeQuestionSetsAsync(List<List<ExamQuestion>> questionSets)
    {
        try
        {
            _logger.LogInformation("正在合并 {SetCount} 个题目集合", questionSets.Count);

            if (questionSets.Count == 0)
                return new List<ExamQuestion>();

            if (questionSets.Count == 1)
                return questionSets[0];

            // 按题目编号合并题目，优先选择更完整的数据
            var mergedQuestions = new Dictionary<int, ExamQuestion>();

            foreach (var questionSet in questionSets)
            {
                foreach (var question in questionSet)
                {
                    if (mergedQuestions.TryGetValue(question.QuestionNumber, out var existing))
                    {
                        // 与现有题目合并，优先选择更完整的数据
                        var merged = MergeQuestions(existing, question);
                        mergedQuestions[question.QuestionNumber] = merged;
                    }
                    else
                    {
                        mergedQuestions[question.QuestionNumber] = question;
                    }
                }
            }

            var result = mergedQuestions.Values.OrderBy(q => q.QuestionNumber).ToList();
            
            _logger.LogInformation("已将题目集合合并为 {QuestionCount} 个唯一题目", result.Count);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "合并题目集合时发生错误：{Error}", ex.Message);
            throw;
        }
    }

    private async Task<List<ExamQuestion>> ParseQuestionsFromDocumentAsync(string filePath, FileType fileType, List<int> questionNumbers, CancellationToken cancellationToken)
    {
        // 从文档中提取文本
        var text = await _fileProcessingService.ConvertToTextAsync(filePath, fileType, cancellationToken);
        
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("未从文档 {FilePath} 中提取到文本", filePath);
            return new List<ExamQuestion>();
        }

        // 从提取的文本中解析题目
        return await ParseQuestionsFromTextAsync(text, questionNumbers, cancellationToken);
    }

    private ExamQuestion MergeQuestions(ExamQuestion existing, ExamQuestion newQuestion)
    {
        var merged = new ExamQuestion
        {
            QuestionNumber = existing.QuestionNumber,
            Content = ChooseBetterContent(existing.Content, newQuestion.Content),
            Type = existing.Type != QuestionType.Unknown ? existing.Type : newQuestion.Type,
            Points = existing.Points > 0 ? existing.Points : newQuestion.Points,
            Options = existing.Options.Count > 0 ? existing.Options : newQuestion.Options,
            Answer = !string.IsNullOrEmpty(existing.Answer) ? existing.Answer : newQuestion.Answer,
            Explanation = !string.IsNullOrEmpty(existing.Explanation) ? existing.Explanation : newQuestion.Explanation
        };

        return merged;
    }

    private string ChooseBetterContent(string content1, string content2)
    {
        if (string.IsNullOrWhiteSpace(content1)) return content2;
        if (string.IsNullOrWhiteSpace(content2)) return content1;
        
        // 优先选择更长、更详细的内容
        return content1.Length >= content2.Length ? content1 : content2;
    }
}