// 试卷识别系统演示应用
let demoTimer = null;
let currentStep = 0;
let isDemo = true;

// 演示用样本数据
const sampleQuestions = [
    {
        questionNumber: 1,
        content: "以下哪个不是面向对象编程的原则？",
        type: "SingleChoice",
        typeDisplayName: "单选题",
        points: 3.0,
        options: ["A. 封装", "B. 继承", "C. 多态", "D. 编译"],
        answer: "D"
    },
    {
        questionNumber: 2,
        content: "使用云计算的主要优势是什么？（选择所有适用的选项）",
        type: "MultipleChoice", 
        typeDisplayName: "多选题",
        points: 4.0,
        options: ["A. 可扩展性", "B. 成本效率", "C. 可访问性", "D. 安全风险"],
        answer: "A, B, C"
    },
    {
        questionNumber: 3,
        content: "二分查找算法的时间复杂度是______。",
        type: "FillInBlank",
        typeDisplayName: "填空题", 
        points: 2.0,
        options: [],
        answer: "O(log n)"
    },
    {
        questionNumber: 4,
        content: "解释SQL和NoSQL数据库之间的区别。请提供每种类型的例子。",
        type: "ShortAnswer",
        typeDisplayName: "简答题",
        points: 8.0,
        options: [],
        answer: null
    },
    {
        questionNumber: 5,
        content: "讨论人工智能对现代软件开发的影响。包括伦理、生产力和未来趋势的考虑。",
        type: "Essay",
        typeDisplayName: "论述题",
        points: 15.0,
        options: [],
        answer: null
    },
    {
        questionNumber: 6,
        content: "哪个HTTP状态码表示请求成功？",
        type: "SingleChoice",
        typeDisplayName: "单选题",
        points: 2.0,
        options: ["A. 404", "B. 500", "C. 200", "D. 302"],
        answer: "C"
    },
    {
        questionNumber: 7,
        content: "在JavaScript中，哪个方法用于向数组末尾添加元素？",
        type: "SingleChoice",
        typeDisplayName: "单选题", 
        points: 2.0,
        options: ["A. push()", "B. pop()", "C. shift()", "D. unshift()"],
        answer: "A"
    },
    {
        questionNumber: 8,
        content: "在Web服务中，REST代表什么？",
        type: "FillInBlank",
        typeDisplayName: "填空题",
        points: 3.0,
        options: [],
        answer: "表示性状态传输 (Representational State Transfer)"
    }
];

// 初始化演示
$(document).ready(function() {
    initializeDemoHandlers();
    showToast('info', '演示模式已激活。使用演示控制来模拟系统工作流程。');
});

function initializeDemoHandlers() {
    $('#demoUpload').click(simulateFileUpload);
    $('#demoProcessing').click(simulateProcessing);
    $('#demoResults').click(showSampleResults);
    $('#demoReset').click(resetDemo);
    $('#exportResults').click(exportDemoResults);
    $('#newUpload').click(resetDemo);
    
    // 过滤器处理器
    $('#typeFilter').change(filterQuestions);
    $('#searchInput').on('input', filterQuestions);
}

function simulateFileUpload() {
    showToast('success', '模拟文件上传：sample_exam.pdf (2.5 MB)');
    
    // 显示文件预览模拟
    $('#uploadArea').html(`
        <div class="file-preview" style="display: block;">
            <div class="file-item">
                <div class="file-icon">
                    <i class="fas fa-file-pdf" style="color: #f56565;"></i>
                </div>
                <div class="file-details">
                    <div class="file-name">sample_exam.pdf</div>
                    <div class="file-size">2.5 MB</div>
                </div>
            </div>
        </div>
        <div class="processing-config" style="display: block; margin-top: 20px;">
            <h3>处理配置</h3>
            <div class="config-row">
                <label>每线程题目数：</label>
                <input type="number" value="3" min="1" max="20" readonly>
            </div>
            <div class="config-row">
                <label>最大并发线程数：</label>
                <input type="number" value="3" min="1" max="16" readonly>
            </div>
        </div>
        <div class="upload-actions" style="display: block; margin-top: 20px;">
            <button class="btn btn-success" onclick="simulateProcessing()">
                <i class="fas fa-play"></i> 开始处理（演示）
            </button>
            <button class="btn btn-secondary" onclick="resetDemo()">
                <i class="fas fa-times"></i> 取消
            </button>
        </div>
    `);
}

function simulateProcessing() {
    showToast('success', '开始模拟处理...');
    
    // 切换到进度视图
    $('.upload-section').hide();
    $('#progressSection').show();
    
    // 初始化进度显示
    $('#totalQuestions').text(sampleQuestions.length);
    $('#completedQuestions').text(0);
    $('#activeThreads').text(3);
    
    // 创建线程状态显示
    createDemoThreads();
    
    // 开始进度模拟
    currentStep = 0;
    startProgressSimulation();
}

function createDemoThreads() {
    const threadList = $('#threadList');
    threadList.empty();
    
    const threads = [
        { id: 0, questions: '1-3', status: '等待中' },
        { id: 1, questions: '4-6', status: '等待中' },
        { id: 2, questions: '7-8', status: '等待中' }
    ];
    
    threads.forEach(thread => {
        const threadItem = $(`
            <div class="thread-item" id="thread-${thread.id}">
                <div class="thread-info">
                    <div class="thread-id">Thread ${thread.id}</div>
                    <div class="thread-questions">Questions ${thread.questions}</div>
                </div>
                <div class="thread-status-badge status-pending">${thread.status}</div>
            </div>
        `);
        threadList.append(threadItem);
    });
}

function startProgressSimulation() {
    const totalQuestions = sampleQuestions.length;
    const duration = 15000; // 15 seconds total
    const interval = duration / totalQuestions;
    
    demoTimer = setInterval(() => {
        currentStep++;
        
        // 更新进度
        const progress = (currentStep / totalQuestions) * 100;
        $('#progressFill').css('width', progress + '%');
        $('#progressText').text(Math.round(progress) + '%');
        $('#completedQuestions').text(currentStep);
        
        // 更新处理速度
        const speed = (currentStep / (currentStep * interval / 1000)).toFixed(1);
        $('#processingSpeed').text(speed);
        
        // 更新系统指标
        const cpuUsage = (20 + Math.random() * 30).toFixed(1);
        const memoryUsage = (512 + Math.random() * 256).toFixed(0);
        const time = formatTime(currentStep * interval);
        
        $('#cpuUsage').text(cpuUsage + '%');
        $('#memoryUsage').text(memoryUsage + ' MB');
        $('#processingTime').text(time);
        
        // 更新线程状态
        updateDemoThreadStatus(currentStep);
        
        // 检查是否完成
        if (currentStep >= totalQuestions) {
            clearInterval(demoTimer);
            completeProcessing();
        }
    }, interval);
}

function updateDemoThreadStatus(step) {
    const threadMappings = [
        { threadId: 0, start: 1, end: 3 },
        { threadId: 1, start: 4, end: 6 },
        { threadId: 2, start: 7, end: 8 }
    ];
    
    threadMappings.forEach(thread => {
        const threadElement = $(`#thread-${thread.threadId} .thread-status-badge`);
        
        if (step < thread.start) {
            threadElement.removeClass().addClass('thread-status-badge status-pending').text('等待中');
        } else if (step < thread.end) {
            threadElement.removeClass().addClass('thread-status-badge status-inprogress').text('处理中');
        } else {
            threadElement.removeClass().addClass('thread-status-badge status-completed').text('已完成');
        }
    });
}

function completeProcessing() {
    showToast('success', '处理成功完成！');
    setTimeout(() => {
        showSampleResults();
    }, 1000);
}

function showSampleResults() {
    // 切换到结果视图
    $('#progressSection').hide();
    $('#resultsSection').show();
    
    // 更新摘要
    $('#totalExtracted').text(sampleQuestions.length);
    $('#finalProcessingTime').text('00:00:15');
    $('#averageSpeed').text('0.5 Q/s');
    
    // 显示题目
    displayDemoQuestions(sampleQuestions);
    
    showToast('success', '示例结果加载成功！');
}

function displayDemoQuestions(questions) {
    const questionsList = $('#questionsList');
    questionsList.empty();
    
    questions.forEach(question => {
        const questionItem = createDemoQuestionElement(question);
        questionsList.append(questionItem);
    });
}

function createDemoQuestionElement(question) {
    const typeColors = {
        'SingleChoice': '#4facfe',
        'MultipleChoice': '#48bb78', 
        'FillInBlank': '#ed8936',
        'ShortAnswer': '#9f7aea',
        'Essay': '#f56565',
        'Unknown': '#a0aec0'
    };
    
    const typeColor = typeColors[question.type] || '#a0aec0';
    
    let optionsHtml = '';
    if (question.options && question.options.length > 0) {
        optionsHtml = '<div class="question-options">';
        question.options.forEach(option => {
            optionsHtml += `<div class="option-item">${escapeHtml(option)}</div>`;
        });
        optionsHtml += '</div>';
    }
    
    let answerHtml = '';
    if (question.answer) {
        answerHtml = `<div class="question-answer"><strong>答案：</strong> ${escapeHtml(question.answer)}</div>`;
    }
    
    return $(`
        <div class="question-item" data-type="${question.type}" data-content="${escapeHtml(question.content.toLowerCase())}">
            <div class="question-header">
                <div style="display: flex; align-items: center; gap: 10px;">
                    <span class="question-number">Q${question.questionNumber}</span>
                    <span class="question-type" style="background-color: ${typeColor}; color: white;">${question.typeDisplayName}</span>
                </div>
                <div class="question-points">${question.points} 分</div>
            </div>
            <div class="question-content">${escapeHtml(question.content)}</div>
            ${optionsHtml}
            ${answerHtml}
        </div>
    `);
}

function filterQuestions() {
    const typeFilter = $('#typeFilter').val();
    const searchText = $('#searchInput').val().toLowerCase();
    
    $('.question-item').each(function() {
        const $item = $(this);
        const itemType = $item.data('type');
        const itemContent = $item.data('content');
        
        let visible = true;
        
        // 类型过滤
        if (typeFilter !== 'all' && itemType !== typeFilter) {
            visible = false;
        }
        
        // 搜索过滤
        if (searchText && !itemContent.includes(searchText)) {
            visible = false;
        }
        
        $item.toggle(visible);
    });
}

function exportDemoResults() {
    const results = {
        sessionId: 'demo-session-123',
        fileName: 'sample_exam.pdf',
        processedAt: new Date().toISOString(),
        totalQuestions: sampleQuestions.length,
        questions: sampleQuestions
    };
    
    const dataStr = JSON.stringify(results, null, 2);
    const dataBlob = new Blob([dataStr], {type: 'application/json'});
    
    const link = document.createElement('a');
    link.href = URL.createObjectURL(dataBlob);
    link.download = 'demo_exam_results.json';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    
    showToast('success', '演示结果导出成功！');
}

function resetDemo() {
    // 清除计时器
    if (demoTimer) {
        clearInterval(demoTimer);
        demoTimer = null;
    }
    
    // 重置界面
    $('.upload-section').show();
    $('#progressSection').hide();
    $('#resultsSection').hide();
    
    // 重置上传区域
    $('#uploadArea').html(`
        <div class="upload-icon">
            <i class="fas fa-cloud-upload-alt"></i>
        </div>
        <p>演示模式 - 点击上方“模拟文件上传”开始</p>
        <p class="file-info">支持格式：PDF、DOCX、JPEG、PNG（最大10MB）</p>
    `);
    
    // 重置过滤器
    $('#typeFilter').val('all');
    $('#searchInput').val('');
    
    // 重置进度
    currentStep = 0;
    $('#progressFill').css('width', '0%');
    $('#progressText').text('0%');
    
    showToast('info', '演示已重置。准备重新开始！');
}

// 工具函数
function showToast(type, message, duration = 5000) {
    const icons = {
        success: 'fas fa-check-circle',
        error: 'fas fa-exclamation-circle', 
        warning: 'fas fa-exclamation-triangle',
        info: 'fas fa-info-circle'
    };
    
    const toast = $(`
        <div class="toast ${type}">
            <i class="toast-icon ${icons[type]}"></i>
            <span class="toast-message">${escapeHtml(message)}</span>
            <button class="toast-close">&times;</button>
        </div>
    `);
    
    // 为关闭按钮添加点击处理器
    toast.find('.toast-close').click(function() {
        toast.remove();
    });
    
    // 添加到容器
    $('#toastContainer').append(toast);
    
    // 持续时间后自动移除
    setTimeout(() => {
        toast.fadeOut(300, () => toast.remove());
    }, duration);
}

function formatTime(milliseconds) {
    const totalSeconds = Math.floor(milliseconds / 1000);
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;
    
    return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}