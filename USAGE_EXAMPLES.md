# 📚 使用示例

## 🚀 基本使用流程

### 1️⃣ 启动应用程序
```bash
dotnet run
```

应用程序将在以下地址启动：
- HTTP: `http://localhost:5000`
- HTTPS: `https://localhost:5001`
- Swagger UI: `https://localhost:5001/swagger`

### 2️⃣ 上传试卷文件

使用 curl 上传文件：
```bash
curl -X POST "https://localhost:5001/api/fileupload/upload" \
     -H "Content-Type: multipart/form-data" \
     -F "file=@exam_paper.pdf"
```

响应示例：
```json
{
  "success": true,
  "data": {
    "success": true,
    "sessionId": "abc123-def456-ghi789",
    "message": "File uploaded successfully",
    "fileName": "exam_paper.pdf",
    "fileSizeBytes": 2048576
  },
  "message": "File uploaded successfully"
}
```

### 3️⃣ 开始处理

```bash
curl -X POST "https://localhost:5001/api/fileupload/start-processing" \
     -H "Content-Type: application/json" \
     -d '{
       "sessionId": "abc123-def456-ghi789",
       "customConfig": {
         "questionsPerThread": 5,
         "maxConcurrentThreads": 4
       }
     }'
```

### 4️⃣ 监控处理进度

```bash
curl -X GET "https://localhost:5001/api/monitoring/status/abc123-def456-ghi789"
```

响应示例：
```json
{
  "success": true,
  "data": {
    "sessionId": "abc123-def456-ghi789",
    "status": "Processing",
    "totalQuestions": 20,
    "completedQuestions": 8,
    "progressPercentage": 40.0,
    "taskStatuses": [
      {
        "taskId": "task-001",
        "startQuestionNumber": 1,
        "endQuestionNumber": 5,
        "status": "Completed",
        "threadId": 0,
        "progress": 100
      },
      {
        "taskId": "task-002",
        "startQuestionNumber": 6,
        "endQuestionNumber": 10,
        "status": "InProgress",
        "threadId": 1,
        "progress": 60
      }
    ],
    "metrics": {
      "processingDuration": "00:02:15",
      "cpuUsagePercent": 45.2,
      "memoryUsageBytes": 256000000,
      "activeThreads": 3,
      "questionsPerSecond": 2
    }
  }
}
```

### 5️⃣ 获取解析结果

```bash
curl -X GET "https://localhost:5001/api/monitoring/questions/abc123-def456-ghi789"
```

响应示例：
```json
{
  "success": true,
  "data": {
    "success": true,
    "sessionId": "abc123-def456-ghi789",
    "questions": [
      {
        "questionNumber": 1,
        "content": "下列哪个不是面向对象编程的特点？",
        "type": "SingleChoice",
        "points": 3.0,
        "options": [
          "A. 封装",
          "B. 继承",
          "C. 多态",
          "D. 编译"
        ],
        "answer": "D",
        "explanation": null
      },
      {
        "questionNumber": 2,
        "content": "请解释什么是算法复杂度。",
        "type": "ShortAnswer",
        "points": 5.0,
        "options": [],
        "answer": null,
        "explanation": null
      }
    ],
    "totalQuestions": 20
  }
}
```

## 🔧 高级使用场景

### 📁 批量处理多个文件

```python
import requests
import time

def process_exam_files(file_paths):
    """批量处理多个试卷文件"""
    sessions = []
    
    # 1. 上传所有文件
    for file_path in file_paths:
        with open(file_path, 'rb') as f:
            response = requests.post(
                'https://localhost:5001/api/fileupload/upload',
                files={'file': f},
                verify=False
            )
            if response.json()['success']:
                session_id = response.json()['data']['sessionId']
                sessions.append(session_id)
    
    # 2. 开始所有处理任务
    for session_id in sessions:
        requests.post(
            'https://localhost:5001/api/fileupload/start-processing',
            json={'sessionId': session_id},
            verify=False
        )
    
    # 3. 监控所有任务完成
    while sessions:
        completed = []
        for session_id in sessions:
            status_response = requests.get(
                f'https://localhost:5001/api/monitoring/status/{session_id}',
                verify=False
            )
            status = status_response.json()['data']['status']
            if status == 'Completed':
                completed.append(session_id)
                
                # 获取结果
                questions_response = requests.get(
                    f'https://localhost:5001/api/monitoring/questions/{session_id}',
                    verify=False
                )
                save_results(session_id, questions_response.json())
        
        # 移除已完成的会话
        for session_id in completed:
            sessions.remove(session_id)
        
        time.sleep(5)  # 等待5秒后重新检查

def save_results(session_id, results):
    """保存处理结果"""
    with open(f'results_{session_id}.json', 'w', encoding='utf-8') as f:
        import json
        json.dump(results, f, ensure_ascii=False, indent=2)

# 使用示例
file_paths = ['exam1.pdf', 'exam2.docx', 'exam3.jpg']
process_exam_files(file_paths)
```

### ⚙️ 自定义处理配置

```javascript
// JavaScript 示例 - 动态调整处理配置
async function processWithOptimalConfig(sessionId, fileSize) {
    // 根据文件大小动态调整配置
    let config = {
        questionsPerThread: 5,
        maxConcurrentThreads: 4
    };
    
    if (fileSize > 5 * 1024 * 1024) { // 大于5MB
        config.questionsPerThread = 3;
        config.maxConcurrentThreads = 8;
    } else if (fileSize < 1 * 1024 * 1024) { // 小于1MB
        config.questionsPerThread = 10;
        config.maxConcurrentThreads = 2;
    }
    
    const response = await fetch('/api/fileupload/start-processing', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            sessionId: sessionId,
            customConfig: config
        })
    });
    
    return response.json();
}
```

### 📊 实时进度监控 (WebSocket 风格)

```python
import asyncio
import aiohttp

async def monitor_progress_realtime(session_id):
    """实时监控处理进度"""
    async with aiohttp.ClientSession() as session:
        while True:
            async with session.get(
                f'https://localhost:5001/api/monitoring/status/{session_id}',
                ssl=False
            ) as response:
                data = await response.json()
                
                if data['success']:
                    status_data = data['data']
                    print(f"进度: {status_data['progressPercentage']:.1f}% "
                          f"({status_data['completedQuestions']}/{status_data['totalQuestions']})")
                    
                    if status_data['status'] in ['Completed', 'Failed', 'Cancelled']:
                        print(f"处理完成，状态: {status_data['status']}")
                        break
                
                await asyncio.sleep(2)  # 每2秒检查一次

# 使用示例
# asyncio.run(monitor_progress_realtime('your-session-id'))
```

## ⚠️ 错误处理示例

### 📤 处理上传错误

```python
def safe_upload_file(file_path):
    """安全的文件上传，包含错误处理"""
    try:
        with open(file_path, 'rb') as f:
            response = requests.post(
                'https://localhost:5001/api/fileupload/upload',
                files={'file': f},
                verify=False,
                timeout=30
            )
            
            result = response.json()
            
            if result['success']:
                return result['data']['sessionId']
            else:
                print(f"上传失败: {result['message']}")
                if 'errors' in result['data']:
                    for error in result['data']['errors']:
                        print(f"  - {error}")
                return None
                
    except FileNotFoundError:
        print(f"文件不存在: {file_path}")
    except requests.RequestException as e:
        print(f"网络错误: {e}")
    except Exception as e:
        print(f"未知错误: {e}")
    
    return None
```

### 🔄 处理超时和重试

```python
import time
from functools import wraps

def retry_on_failure(max_retries=3, delay=5):
    """重试装饰器"""
    def decorator(func):
        @wraps(func)
        def wrapper(*args, **kwargs):
            for attempt in range(max_retries):
                try:
                    return func(*args, **kwargs)
                except Exception as e:
                    if attempt == max_retries - 1:
                        raise e
                    print(f"尝试 {attempt + 1} 失败: {e}, {delay}秒后重试...")
                    time.sleep(delay)
            return None
        return wrapper
    return decorator

@retry_on_failure(max_retries=3, delay=10)
def get_processing_status(session_id):
    """获取处理状态，支持重试"""
    response = requests.get(
        f'https://localhost:5001/api/monitoring/status/{session_id}',
        verify=False,
        timeout=15
    )
    response.raise_for_status()
    return response.json()
```

## 📈 性能监控示例

### 💚 监控系统健康状态

```bash
# 检查系统健康状态
curl -X GET "https://localhost:5001/api/monitoring/health"

# 获取性能历史数据
curl -X GET "https://localhost:5001/api/monitoring/performance/history?hours=2"

# 导出处理结果
curl -X GET "https://localhost:5001/api/monitoring/export/abc123-def456-ghi789" \
     -o exam_results.json
```

### 📋 自动化性能报告

```python
def generate_performance_report():
    """生成性能报告"""
    # 获取系统健康状态
    health_response = requests.get('https://localhost:5001/api/monitoring/health', verify=False)
    health_data = health_response.json()['data']
    
    # 获取性能历史
    perf_response = requests.get('https://localhost:5001/api/monitoring/performance/history?hours=1', verify=False)
    perf_data = perf_response.json()['data']
    
    # 生成报告
    report = {
        'timestamp': time.time(),
        'system_health': {
            'is_healthy': health_data['isHealthy'],
            'active_sessions': health_data['activeSessions'],
            'total_threads': health_data['totalThreads'],
            'issues': health_data['issues']
        },
        'performance_summary': {
            'avg_cpu': sum(p['cpuUsagePercent'] for p in perf_data) / len(perf_data) if perf_data else 0,
            'avg_memory_mb': sum(p['memoryUsageBytes'] for p in perf_data) / len(perf_data) / 1024 / 1024 if perf_data else 0,
            'max_threads': max(p['activeThreads'] for p in perf_data) if perf_data else 0
        }
    }
    
    return report
```

## 🚀 配置优化建议

### 🖥️ 根据硬件配置调整参数

```json
// 高性能服务器配置 (16核心, 32GB内存)
{
  "Threading": {
    "QuestionsPerThread": 3,
    "MaxConcurrentThreads": 12,
    "EnableAdaptiveThreading": true
  },
  "FileUpload": {
    "MaxFileSize": 52428800,  // 50MB
    "MaxConcurrentUploads": 10
  }
}

// 普通开发机配置 (4核心, 8GB内存)
{
  "Threading": {
    "QuestionsPerThread": 5,
    "MaxConcurrentThreads": 4,
    "EnableAdaptiveThreading": true
  },
  "FileUpload": {
    "MaxFileSize": 10485760,  // 10MB
    "MaxConcurrentUploads": 3
  }
}

// 低配置环境 (2核心, 4GB内存)
{
  "Threading": {
    "QuestionsPerThread": 8,
    "MaxConcurrentThreads": 2,
    "EnableAdaptiveThreading": false
  },
  "FileUpload": {
    "MaxFileSize": 5242880,   // 5MB
    "MaxConcurrentUploads": 2
  }
}
```

这些示例涵盖了系统的主要使用场景，包括基本操作、高级功能、错误处理和性能优化等方面。用户可以根据实际需求选择合适的使用方式。