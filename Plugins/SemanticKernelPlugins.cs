using Microsoft.SemanticKernel;
using ExamRecognitionSystem.Models;
using System.ComponentModel;
using System.Text.Json;

namespace ExamRecognitionSystem.Plugins;

/// <summary>
/// Semantic Kernel plugin for question parsing and analysis
/// </summary>
public class QuestionParsingPlugin
{
    [KernelFunction, Description("Parse and extract exam questions from text content")]
    public async Task<string> ParseQuestionsFromText(
        [Description("The text content containing exam questions")] string textContent,
        [Description("JSON array of question numbers to focus on, e.g., [1,2,3]")] string questionNumbers = "[]")
    {
        var targetNumbers = JsonSerializer.Deserialize<List<int>>(questionNumbers) ?? new List<int>();
        
        var prompt = $@"
            Extract exam questions from the following text content.
            {(targetNumbers.Any() ? $"Focus on questions numbered: {string.Join(", ", targetNumbers)}" : "Extract all questions found")}
            
            Text content:
            {textContent}
            
            Extract questions in this JSON format:
            {{
              ""questions"": [
                {{
                  ""questionNumber"": 1,
                  ""content"": ""Complete question text with all options"",
                  ""type"": ""SingleChoice|MultipleChoice|FillInBlank|ShortAnswer|Essay|Unknown"",
                  ""points"": 3.0,
                  ""options"": [""A. Option 1"", ""B. Option 2""],
                  ""answer"": ""A"" or null,
                  ""explanation"": null
                }}
              ]
            }}
            ";
        
        return await Task.FromResult(prompt);
    }

    [KernelFunction, Description("Analyze question type and structure")]
    public async Task<string> AnalyzeQuestionType(
        [Description("The question content to analyze")] string questionContent)
    {
        var prompt = $@"
            Analyze the following question and determine its type and characteristics:
            
            Question: {questionContent}
            
            Provide analysis in JSON format:
            {{
              ""type"": ""SingleChoice|MultipleChoice|FillInBlank|ShortAnswer|Essay|Unknown"",
              ""hasOptions"": true/false,
              ""optionCount"": 4,
              ""estimatedPoints"": 3.0,
              ""complexity"": ""Low|Medium|High"",
              ""keywords"": [""keyword1"", ""keyword2""],
              ""reasoning"": ""Explanation of type determination""
            }}
            ";
        
        return await Task.FromResult(prompt);
    }

    [KernelFunction, Description("Extract point values from question text")]
    public async Task<string> ExtractPointValues(
        [Description("Text containing point value indicators")] string text)
    {
        var prompt = $@"
            Extract point values from the following text. Look for patterns like:
            - ""3分"", ""5 points"", ""(4分)"", ""Score: 3"", etc.
            
            Text: {text}
            
            Return JSON format:
            {{
              ""pointValues"": [
                {{""questionNumber"": 1, ""points"": 3.0}},
                {{""questionNumber"": 2, ""points"": 5.0}}
              ]
            }}
            ";
        
        return await Task.FromResult(prompt);
    }

    [KernelFunction, Description("Validate and correct extracted question data")]
    public async Task<string> ValidateQuestionData(
        [Description("JSON string of extracted questions to validate")] string questionsJson)
    {
        var prompt = $@"
            Validate and correct the following extracted question data:
            
            {questionsJson}
            
            Check for:
            1. Valid question numbers (sequential, positive integers)
            2. Non-empty question content
            3. Appropriate question types
            4. Reasonable point values (0.5 to 20 points)
            5. Complete options for choice questions
            6. Proper JSON structure
            
            Return corrected JSON with validation notes:
            {{
              ""validatedQuestions"": [...],
              ""validationNotes"": [
                ""Fixed question 3 type from Unknown to SingleChoice"",
                ""Added missing option D for question 2""
              ],
              ""isValid"": true
            }}
            ";
        
        return await Task.FromResult(prompt);
    }

    [KernelFunction, Description("Merge questions from multiple sources")]
    public async Task<string> MergeQuestionSets(
        [Description("JSON array of question sets to merge")] string questionSetsJson)
    {
        var prompt = $@"
            Merge the following question sets, resolving conflicts and duplicates:
            
            {questionSetsJson}
            
            Rules:
            1. Keep questions with most complete information
            2. Resolve conflicts by preferring higher confidence data
            3. Maintain sequential question numbering
            4. Combine complementary information (e.g., content from one source, options from another)
            
            Return merged result:
            {{
              ""mergedQuestions"": [...],
              ""mergeNotes"": [""Merged question 3 content with options from set 2""],
              ""totalQuestions"": 15
            }}
            ";
        
        return await Task.FromResult(prompt);
    }
}

/// <summary>
/// Plugin for image analysis and OCR operations
/// </summary>
public class ImageAnalysisPlugin
{
    [KernelFunction, Description("Analyze exam paper image layout and structure")]
    public async Task<string> AnalyzeImageLayout(
        [Description("Base64 encoded image data")] string imageBase64)
    {
        var prompt = $@"
            Analyze the layout and structure of this exam paper image.
            
            Identify:
            1. Number of questions visible
            2. Question layout (single column, multiple columns)
            3. Question numbering system
            4. Presence of answer sheets or answer keys
            5. Text quality and readability
            6. Language of the exam (Chinese, English, etc.)
            
            Provide analysis in JSON format:
            {{
              ""layout"": {{
                ""questionCount"": 10,
                ""columns"": 1,
                ""numberingStyle"": ""1. 2. 3."",
                ""hasAnswerKey"": false,
                ""textQuality"": ""High|Medium|Low"",
                ""language"": ""Chinese|English|Mixed"",
                ""pageType"": ""Questions|Answers|Mixed""
              }},
              ""readabilityScore"": 0.85,
              ""recommendedProcessing"": ""Direct OCR|Enhanced preprocessing needed""
            }}
            ";
        
        return await Task.FromResult(prompt);
    }

    [KernelFunction, Description("Extract text regions from exam paper image")]
    public async Task<string> ExtractTextRegions(
        [Description("Base64 encoded image data")] string imageBase64,
        [Description("Specific regions to focus on (e.g., 'questions 1-5')")] string focusRegions = "")
    {
        var prompt = $@"
            Extract text from specific regions of this exam paper image.
            {(!string.IsNullOrEmpty(focusRegions) ? $"Focus on: {focusRegions}" : "Extract all readable text regions")}
            
            Organize extracted text by:
            1. Question numbers
            2. Question content
            3. Options (if any)
            4. Point values
            5. Other relevant text
            
            Return structured text:
            {{
              ""textRegions"": [
                {{
                  ""questionNumber"": 1,
                  ""questionText"": ""What is..."",
                  ""options"": [""A. ..."", ""B. ...""],
                  ""points"": ""3分"",
                  ""position"": {{""x"": 100, ""y"": 200, ""width"": 400, ""height"": 150}}
                }}
              ],
              ""confidence"": 0.92,
              ""processingNotes"": [""High contrast text"", ""Clear question boundaries""]
            }}
            ";
        
        return await Task.FromResult(prompt);
    }
}

/// <summary>
/// Plugin for file processing operations
/// </summary>
public class FileProcessingPlugin
{
    [KernelFunction, Description("Convert document to processable format")]
    public async Task<string> ConvertDocumentToText(
        [Description("Document file path")] string filePath,
        [Description("Document type (PDF, DOCX, etc.)")] string documentType)
    {
        var prompt = $@"
            Process document conversion for: {filePath} (Type: {documentType})
            
            Conversion requirements:
            1. Extract all text content preserving structure
            2. Maintain question numbering and formatting
            3. Preserve tables and special characters
            4. Handle multi-column layouts
            5. Extract embedded images if any
            
            Return conversion status:
            {{
              ""success"": true,
              ""extractedText"": ""..."",
              ""pageCount"": 3,
              ""hasImages"": true,
              ""imageLocations"": [
                {{""page"": 1, ""position"": ""top"", ""description"": ""Question diagram""}}
              ],
              ""conversionNotes"": [""Successfully extracted all text"", ""Found 2 images for separate processing""]
            }}
            ";
        
        return await Task.FromResult(prompt);
    }

    [KernelFunction, Description("Optimize image for OCR processing")]
    public async Task<string> OptimizeImageForOCR(
        [Description("Base64 encoded image data")] string imageBase64)
    {
        var prompt = $@"
            Optimize this image for better OCR accuracy.
            
            Apply optimizations:
            1. Contrast enhancement
            2. Noise reduction
            3. Deskewing if needed
            4. Resolution adjustment
            5. Binarization for text regions
            
            Return optimization report:
            {{
              ""optimizedImage"": ""base64_optimized_image_data"",
              ""optimizations"": [""Enhanced contrast"", ""Removed noise"", ""Deskewed 2.3 degrees""],
              ""qualityScore"": {{
                ""before"": 0.72,
                ""after"": 0.91
              }},
              ""recommendedOCRSettings"": {{
                ""language"": ""chi_sim+eng"",
                ""dpi"": 300,
                ""pageSegMode"": 1
              }}
            }}
            ";
        
        return await Task.FromResult(prompt);
    }
}