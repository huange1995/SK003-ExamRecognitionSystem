# 高性能多线程图片试卷识别系统

基于 .NET 8.0、Semantic Kernel 和 Doubao-Seed-1.6-thinking 视觉模型开发的智能试卷识别系统。

## 功能特性

### 🚀 核心功能
- **多轮对话机制**: 基于 Semantic Kernel 与视觉模型的多轮对话，支持历史上下文缓存
- **智能多线程处理**: 每个线程处理5道题目，支持动态线程池管理
- **文件格式支持**: PDF、DOCX、JPEG、PNG 格式文件上传和处理
- **智能题目解析**: 自动识别题目类型、分值、选项和答案

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
                                              │ Doubao 视觉模型 │
                                              └─────────────────┘
```

## 快速开始

### 环境要求
- .NET 8.0 SDK
- Windows 10/11 或 Linux
- 至少 4GB RAM
- Doubao API 密钥

### 安装步骤

1. **克隆项目**
```bash
git clone <repository-url>
cd SK003
```

2. **配置 API 密钥**
编辑 `appsettings.json` 文件：
```json
{
  "DoubaoSettings": {
    "ApiKey": "your-doubao-api-key",
    "BaseUrl": "https://ark.cn-beijing.volces.com/api/v3",
    "ModelId": "ep-20241203135326-gd8tx"
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
  "DoubaoSettings": {
    "ApiKey": "your-api-key",
    "BaseUrl": "https://ark.cn-beijing.volces.com/api/v3",
    "ModelId": "ep-20241203135326-gd8tx",
    "MaxTokens": 4000,
    "Temperature": 0.7
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

**2. API 密钥错误**
检查 `appsettings.json` 中的 Doubao API 配置

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
1. 实现 `IChatCompletionService` 接口
2. 在 `SemanticKernelExtensions` 中配置
3. 更新配置文件结构

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

**版本**: 1.0.0  
**最后更新**: 2025-09-20  
**开发状态**: 生产就绪