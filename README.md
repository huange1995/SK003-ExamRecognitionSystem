# 高性能多线程图片试卷识别系统

基于 .NET 8.0、Semantic Kernel 和多AI提供商（Ollama/OpenAI/豆包）开发的智能试卷识别系统。

## 功能特性

### 🚀 核心功能
- **多AI提供商支持**: 支持 Ollama 本地模型、OpenAI 云端模型和豆包(Doubao) AI，可灵活切换
- **多轮对话机制**: 基于 Semantic Kernel 与AI模型的多轮对话，支持历史上下文缓存
- **智能多线程处理**: 每个线程处理5道题目，支持动态线程池管理
- **文件格式支持**: PDF、DOCX、JPEG、PNG 格式文件上传和处理
- **智能题目解析**: 自动识别题目类型、分值、选项和答案
- **自定义AI服务**: 完整实现豆包AI的文本生成和聊天完成服务

### 🔧 技术特性
- **高性能架构**: 基于 .NET 8.0 和异步编程模式
- **智能负载均衡**: 基于 CPU 核心数和内存使用情况的自动线程配置
- **实时监控**: 完整的进度监控和性能计数器
- **安全防护**: 文件验证、请求限流和安全头设置

## 系统架构

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   文件上传API   │───▶│  多线程管理器   │───▶│  题目解析服务   │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │                       │
         ▼                       ▼                       ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   验证服务      │    │  进度监控器     │    │ Semantic Kernel │
└─────────────────┘    └─────────────────┘    └─────────────────┘
                                                        │
                                                        ▼
                                              ┌─────────────────┐
                                              │ AI 模型服务层   │
                                              │(Ollama/OpenAI/豆包)│
                                              └─────────────────┘
```

### 🔧 AI服务架构
- **统一接口**: 通过 Semantic Kernel 统一管理多种AI提供商
- **自定义服务**: DoubaoTextGenerationService 实现完整的豆包AI集成
- **智能路由**: 根据配置自动选择最适合的AI模型
- **插件系统**: 支持题目解析、图像分析、文件处理等专用插件

## 快速开始

### 环境要求
- .NET 8.0 SDK
- Windows 10/11 或 Linux
- 至少 4GB RAM
- AI服务配置（Ollama 本地服务 或 OpenAI API 密钥）

### 安装步骤

1. **克隆项目**
```bash
git clone <repository-url>
cd SK003
```

2. **配置 AI 提供商**
编辑 `appsettings.json` 文件：

**使用 Ollama 本地模型（推荐）：**
```json
{
  "AIProvider": {
    "Provider": "Ollama",
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "ModelId": "llava:13b",
      "MaxTokens": 4000,
      "Temperature": 0.7
    }
  }
}
```

**使用 OpenAI 云端模型：**
```json
{
  "AIProvider": {
    "Provider": "OpenAI",
    "OpenAI": {
      "ApiKey": "your-openai-api-key",
      "ModelId": "gpt-4o",
      "BaseUrl": "https://api.openai.com/v1",
      "MaxTokens": 4000,
      "Temperature": 0.7
    }
  }
}
```

**使用豆包(Doubao) AI 模型：**
```json
{
  "AIProvider": {
    "Provider": "Doubao",
    "Doubao": {
      "ApiKey": "your-doubao-api-key",
      "ModelId": "ep-20250921160727-qgzd9",
      "BaseUrl": "https://ark.cn-beijing.volces.com/api/v3",
      "MaxTokens": 4000,
      "Temperature": 0.7
    }
  }
}
```

3. **安装依赖**
```bash
dotnet restore
```

4. **运行应用**
```bash
dotnet run
```

5. **访问 API**
打开浏览器访问 `https://localhost:5001/swagger` 查看 API 文档

## API 接口

### 文件上传
```http
POST /api/fileupload/upload
Content-Type: multipart/form-data

参数:
- file: 试卷文件 (PDF/DOCX/JPEG/PNG, 最大10MB)
```

### 开始处理
```http
POST /api/fileupload/start-processing
Content-Type: application/json

{
  "sessionId": "session-id",
  "customConfig": {
    "questionsPerThread": 5,
    "maxConcurrentThreads": 8
  }
}
```

### 查询状态
```http
GET /api/monitoring/status/{sessionId}
```

### 获取结果
```http
GET /api/monitoring/questions/{sessionId}
```

## 配置说明

### 基础配置 (appsettings.json)
```json
{
  "AIProvider": {
    "Provider": "Ollama",
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "ModelId": "llava:13b",
      "MaxTokens": 4000,
      "Temperature": 0.7,
      "RequestTimeout": 100
    },
    "OpenAI": {
      "ApiKey": "",
      "ModelId": "gpt-4o",
      "BaseUrl": "https://api.openai.com/v1",
      "MaxTokens": 4000,
      "Temperature": 0.7,
      "RequestTimeout": 60
    },
    "Doubao": {
      "ApiKey": "",
      "BaseUrl": "https://ark.cn-beijing.volces.com/api/v3",
      "ModelId": "ep-20250921160727-qgzd9",
      "MaxTokens": 4000,
      "Temperature": 0.7,
      "RequestTimeout": 60
    }
  },
  "FileUpload": {
    "MaxFileSize": 10485760,
    "AllowedExtensions": [".pdf", ".docx", ".jpeg", ".jpg", ".png"],
    "TempDirectory": "UploadTemp"
  },
  "Threading": {
    "QuestionsPerThread": 5,
    "MaxConcurrentThreads": 8
  }
}
```

### 线程池配置
- `QuestionsPerThread`: 每个线程处理的题目数量 (默认: 5)
- `MaxConcurrentThreads`: 最大并发线程数 (默认: CPU核心数)
- `EnableAdaptiveThreading`: 是否启用自适应线程管理 (默认: true)

## 监控与日志

### 性能监控
- CPU 使用率监控
- 内存使用量追踪
- 活跃线程数统计
- 处理速度计算 (题目/秒)

### 日志系统
使用 Serilog 进行结构化日志记录：
- 控制台输出
- 文件滚动记录
- 不同级别的日志过滤

### 健康检查
```http
GET /api/monitoring/health
```

## 错误处理

系统提供三层错误处理机制：

1. **文件级异常**: 文件格式验证、大小检查
2. **线程级异常**: 单个线程失败不影响其他线程
3. **系统级异常**: 全局异常捕获和恢复

## 安全特性

### 文件安全
- 严格的文件类型验证
- 文件大小限制 (10MB)
- 临时文件自动清理

### API 安全
- 请求频率限制 (60次/分钟)
- 安全请求头设置
- 输入参数验证

### 数据安全
- 线程安全的数据结构
- 会话隔离
- 自动数据过期清理

## 性能优化

### 多线程优化
- 智能线程分配策略
- 基于负载的动态调整
- 线程池复用机制

### 内存优化
- 流式文件处理
- 及时资源释放
- 内存使用监控

### 网络优化
- HTTP 连接复用
- 请求超时控制
- 重试机制

## 故障排除

### 常见问题

**1. 编译错误**
```bash
dotnet clean
dotnet restore
dotnet build
```

**2. AI 服务连接错误**
- Ollama: 确保本地 Ollama 服务运行在 http://localhost:11434
- OpenAI: 检查 `appsettings.json` 中的 API 密钥配置

**3. 内存不足**
调整 `Threading.MaxConcurrentThreads` 参数

**4. 文件上传失败**
检查文件格式和大小限制

### 日志查看
```bash
# 查看当天日志
cat logs/exam-recognition-$(date +%Y%m%d).log

# 实时监控日志
tail -f logs/exam-recognition-$(date +%Y%m%d).log
```

## 前端文件架构

### wwwroot 目录结构
```
wwwroot/
├── index.html          # 系统首页 - 欢迎页面和功能介绍
├── app.html           # 主应用页面 - 文件上传和处理界面
├── demo.html          # 演示页面 - 系统功能演示
├── app.js             # 主应用逻辑 (636行)
├── demo.js            # 演示功能逻辑 (440行)
└── styles.css         # 全局样式表 (730行)
```

### 页面功能说明

#### 🏠 index.html - 系统首页
- **功能**: 系统欢迎页面和功能介绍
- **特色**: 响应式设计，功能特性展示
- **导航**: 提供到主应用和演示页面的链接
- **UI组件**: 特性卡片、导航按钮、系统介绍

#### 📋 app.html - 主应用页面
- **功能**: 完整的文件上传和处理工作流
- **核心特性**:
  - 拖拽上传支持 (PDF、DOCX、JPEG、PNG)
  - 实时系统状态监控
  - 多线程处理进度显示
  - 结果展示和导出功能
- **API集成**: 完整的后端API调用

#### 🎮 demo.html - 演示页面
- **功能**: 系统功能演示和测试
- **演示内容**: 模拟文件上传、处理流程、示例结果
- **示例数据**: 包含8道不同类型的示例题目
- **交互控制**: 演示控制按钮，重置功能

### JavaScript 功能模块

#### 📜 app.js - 主应用逻辑
- **文件管理**: 文件选择、拖拽上传、预览
- **API通信**: RESTful API调用，错误处理
- **进度监控**: 实时处理状态更新
- **结果处理**: 题目展示、筛选、导出
- **用户体验**: 加载动画、提示消息、状态指示

#### 🎯 demo.js - 演示功能
- **模拟处理**: 仿真文件上传和处理流程
- **示例数据**: 预定义的8道示例题目
- **交互演示**: 步骤化演示系统功能
- **数据展示**: 不同题型的展示效果

### 样式设计特色

#### 🎨 styles.css - 现代化UI设计
- **设计风格**: 现代扁平化设计，渐变背景
- **响应式布局**: 支持多种屏幕尺寸
- **交互效果**: 悬停动画、过渡效果
- **组件样式**: 卡片、按钮、表单、进度条
- **主题色彩**: 蓝紫渐变主题，专业感设计

### 技术特性

- **📱 响应式设计**: 适配桌面和移动设备
- **🎭 现代UI**: Font Awesome图标，jQuery交互
- **⚡ 异步处理**: Ajax调用，实时状态更新
- **🔄 实时监控**: WebSocket风格的状态轮询
- **📊 数据可视化**: 进度条、状态指示器
- **🎪 用户体验**: 拖拽上传、加载动画、消息提示

## 主要依赖包

### 核心框架
- **.NET 8.0**: 现代化的跨平台开发框架
- **ASP.NET Core**: Web API 和中间件支持

### AI 和机器学习
- **Microsoft.SemanticKernel (1.0.1)**: 语义内核框架
- **Microsoft.SemanticKernel.Connectors.Ollama (1.65.0-alpha)**: Ollama 连接器
- **Microsoft.SemanticKernel.Connectors.OpenAI (1.0.1)**: OpenAI 连接器
- **Microsoft.SemanticKernel.Plugins.Core (1.0.1-alpha)**: 核心插件

### 文件处理
- **iText7 (8.0.2)**: PDF 文件处理
- **DocumentFormat.OpenXml (3.0.0)**: DOCX 文件处理
- **SixLabors.ImageSharp (3.1.0)**: 图像处理

### 日志和监控
- **Serilog.AspNetCore (8.0.0)**: 结构化日志框架
- **Serilog.Sinks.Console (5.0.1)**: 控制台日志输出
- **Serilog.Sinks.File (5.0.0)**: 文件日志输出

### API 文档
- **Swashbuckle.AspNetCore (6.5.0)**: Swagger/OpenAPI 文档生成
- **Microsoft.AspNetCore.OpenApi (8.0.0)**: OpenAPI 规范支持

### 其他工具
- **System.Collections.Concurrent (4.3.0)**: 线程安全集合
- **Microsoft.Extensions.Http (8.0.0)**: HTTP 客户端工厂

## 扩展开发

### 添加新的文件格式支持
1. 在 `FileType` 枚举中添加新类型
2. 在 `FileProcessingService` 中实现处理逻辑
3. 更新文件验证配置

### 自定义 Semantic Kernel 插件
1. 在 `Plugins` 目录创建新插件类
2. 实现 `[KernelFunction]` 方法
3. 在 `ServiceExtensions` 中注册插件

### 添加新的AI模型支持
1. 在 `AIProviderSettings` 中添加新的提供商配置
2. 创建自定义的文本生成服务（参考 `DoubaoTextGenerationService`）
3. 在 `SemanticKernelExtensions.ConfigureSemanticKernel` 中添加配置逻辑
4. 更新 `appsettings.json` 配置结构
5. 支持的提供商类型：Ollama（本地）、OpenAI（云端）、豆包（云端）

### 豆包AI集成示例
```csharp
// DoubaoTextGenerationService 实现了完整的AI服务接口
public class DoubaoTextGenerationService : ITextGenerationService, IChatCompletionService
{
    // 支持文本生成
    public async Task<IReadOnlyList<TextContent>> GetTextContentsAsync(...)
    
    // 支持聊天完成
    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(...)
    
    // 支持流式响应
    public async IAsyncEnumerable<StreamingTextContent> GetStreamingTextContentsAsync(...)
}
```

## 许可证

本项目采用 MIT 许可证，详情请参见 [LICENSE](LICENSE) 文件。

## 贡献指南

欢迎提交 Pull Request 和 Issue！请遵循以下准则：

1. 代码风格遵循 .NET 标准
2. 添加适当的单元测试
3. 更新相关文档
4. 提交前运行完整测试套件

## 技术支持

如有问题或建议，请通过以下方式联系：

- 创建 GitHub Issue
- 发送邮件至技术支持团队
- 查看项目 Wiki 获取更多信息

---

**版本**: 1.0.1  
**最后更新**: 2025-01-23  
**开发状态**: 生产就绪  
**新增特性**: 豆包AI完整集成、自定义AI服务架构