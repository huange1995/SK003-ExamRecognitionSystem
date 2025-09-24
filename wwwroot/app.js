// 全局变量
let currentSessionId = null;
let progressTimer = null;
let currentQuestions = [];
let isProcessing = false;

// API配置
const API_BASE = window.location.origin;
const API_ENDPOINTS = {
    upload: `${API_BASE}/api/fileupload/upload`,
    startProcessing: `${API_BASE}/api/fileupload/start-processing`,
    cancelProcessing: (sessionId) => `${API_BASE}/api/fileupload/cancel/${sessionId}`,
    getStatus: (sessionId) => `${API_BASE}/api/monitoring/status/${sessionId}`,
    getQuestions: (sessionId) => `${API_BASE}/api/monitoring/questions/${sessionId}`,
    getHealth: `${API_BASE}/api/monitoring/health`,
    exportResults: (sessionId) => `${API_BASE}/api/monitoring/export/${sessionId}`
};

// 初始化应用程序
$(document).ready(function() {
    initializeEventHandlers();
    checkSystemHealth();
    
    // 每30秒检查一次系统健康状态
    setInterval(checkSystemHealth, 30000);
});

// 事件处理器
function initializeEventHandlers() {
    // 首先解绑所有事件以防止重复绑定
    $('#browseBtn').off('click');
    $('#fileInput').off('change');
    $('#uploadArea').off('click dragover dragleave drop');
    
    // 文件上传处理器
    $('#browseBtn').click(function(e) {
        e.preventDefault();
        e.stopPropagation(); // 防止事件冒泡
        $('#fileInput').trigger('click');
    });
    $('#fileInput').change(handleFileSelect);
    
    // 拖拽处理器
    $('#uploadArea')
        .on('dragover', handleDragOver)
        .on('dragleave', handleDragLeave)
        .on('drop', handleFileDrop)
        .click(function(e) {
            // 只有在不点击按钮或其子元素时才触发文件选择
            if (!$(e.target).closest('#browseBtn').length && e.target !== this) {
                return; // 避免在按钮区域触发
            }
            if (e.target === this) {
                $('#fileInput').trigger('click');
            }
        });
    
    // 操作按钮
    $('#removeFile').click(clearFileSelection);
    $('#startProcessing').click(startProcessing);
    $('#cancelUpload').click(clearFileSelection);
    $('#cancelProcessing').click(cancelProcessing);
    $('#exportResults').click(exportResults);
    $('#newUpload').click(resetToUpload);
    
    // 过滤器处理器
    $('#typeFilter').change(filterQuestions);
    $('#searchInput').on('input', filterQuestions);
    
    // 防止文档的默认拖拽行为
    $(document).on('dragover drop', function(e) {
        e.preventDefault();
    });
}

// 系统健康检查
function checkSystemHealth() {
    $.ajax({
        url: API_ENDPOINTS.getHealth,
        method: 'GET',
        success: function(response) {
            if (response.success && response.data.isHealthy) {
                updateSystemStatus('healthy', '系统在线');
            } else {
                updateSystemStatus('warning', '检测到系统问题');
            }
        },
        error: function() {
            updateSystemStatus('error', '系统离线');
        }
    });
}

function updateSystemStatus(status, text) {
    const indicator = $('#statusIndicator');
    const statusText = $('#statusText');
    
    indicator.removeClass('warning error').addClass(status);
    statusText.text(text);
}

// 文件处理
function handleDragOver(e) {
    e.preventDefault();
    $(this).addClass('dragover');
}

function handleDragLeave(e) {
    e.preventDefault();
    $(this).removeClass('dragover');
}

function handleFileDrop(e) {
    e.preventDefault();
    $(this).removeClass('dragover');
    
    const files = e.originalEvent.dataTransfer.files;
    if (files.length > 0) {
        handleFile(files[0]);
    }
}

function handleFileSelect(e) {
    const files = e.target.files;
    if (files.length > 0) {
        handleFile(files[0]);
    }
}

function handleFile(file) {
    // 验证文件
    const validTypes = ['.pdf', '.docx', '.jpeg', '.jpg', '.png'];
    const fileExtension = '.' + file.name.split('.').pop().toLowerCase();
    
    if (!validTypes.includes(fileExtension)) {
        showToast('error', '无效的文件类型。请上传PDF、DOCX、JPEG或PNG文件。');
        return;
    }
    
    if (file.size > 10 * 1024 * 1024) { // 10MB
        showToast('error', '文件大小超过10MB限制。');
        return;
    }
    
    // 显示文件预览
    displayFilePreview(file);
}

function displayFilePreview(file) {
    $('#fileName').text(file.name);
    $('#fileSize').text(formatFileSize(file.size));
    
    // 根据类型设置文件图标
    const extension = file.name.split('.').pop().toLowerCase();
    let iconClass = 'fas fa-file';
    
    switch(extension) {
        case 'pdf':
            iconClass = 'fas fa-file-pdf';
            break;
        case 'docx':
            iconClass = 'fas fa-file-word';
            break;
        case 'jpg':
        case 'jpeg':
        case 'png':
            iconClass = 'fas fa-file-image';
            break;
    }
    
    $('.file-icon i').attr('class', iconClass);
    
    // 显示预览和配置部分
    $('#uploadArea').hide();
    $('#filePreview').show();
    $('#processingConfig').show();
    $('#uploadActions').show();
}

function clearFileSelection() {
    $('#fileInput').val('');
    $('#uploadArea').show();
    $('#filePreview').hide();
    $('#processingConfig').hide();
    $('#uploadActions').hide();
}

// 处理函数
function startProcessing() {
    const file = $('#fileInput')[0].files[0];
    if (!file) {
        showToast('error', '请先选择文件。');
        return;
    }
    
    // 显示加载状态
    showLoading('正在上传文件...');
    
    // 创建表单数据
    const formData = new FormData();
    formData.append('file', file);
    
    // 上传文件
    $.ajax({
        url: API_ENDPOINTS.upload,
        method: 'POST',
        data: formData,
        processData: false,
        contentType: false,
        success: function(response) {
            if (response.success) {
                currentSessionId = response.data.sessionId;
                startProcessingSession();
            } else {
                hideLoading();
                showToast('error', response.message || '上传失败');
            }
        },
        error: function(xhr) {
            hideLoading();
            const errorMsg = xhr.responseJSON?.message || '上传失败';
            showToast('error', errorMsg);
        }
    });
}

function startProcessingSession() {
    updateLoadingText('正在开始处理...');
    
    const config = {
        sessionId: currentSessionId,
        customConfig: {
            questionsPerThread: parseInt($('#questionsPerThread').val()),
            maxConcurrentThreads: parseInt($('#maxThreads').val())
        }
    };
    
    $.ajax({
        url: API_ENDPOINTS.startProcessing,
        method: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(config),
        success: function(response) {
            if (response.success) {
                hideLoading();
                showProcessingSection();
                startProgressMonitoring();
                isProcessing = true;
                showToast('success', '处理启动成功');
            } else {
                hideLoading();
                showToast('error', response.message || '启动处理失败');
            }
        },
        error: function(xhr) {
            hideLoading();
            const errorMsg = xhr.responseJSON?.message || '启动处理失败';
            showToast('error', errorMsg);
        }
    });
}

function showProcessingSection() {
    $('.upload-section').hide();
    $('#progressSection').show();
}

function startProgressMonitoring() {
    // 首先清除任何现有的计时器
    if (progressTimer) {
        clearInterval(progressTimer);
        progressTimer = null;
    }
    
    // 立即更新进度
    updateProgress();
    
    // 设置进度更新计时器
    progressTimer = setInterval(updateProgress, 2000);
}

function updateProgress() {
    if (!currentSessionId || !isProcessing) return;
    
    $.ajax({
        url: API_ENDPOINTS.getStatus(currentSessionId),
        method: 'GET',
        success: function(response) {
            if (response.success) {
                const data = response.data;
                updateProgressDisplay(data);
                // 检查处理是否完成
                if (data.status === 4) { // 已完成
                    processingCompleted();
                } else if (data.status === 5) { // 失败
                    processingFailed(data.errorMessage);
                } else if (data.status === 6) { // 已取消
                    processingCancelled();
                }
            }
        },
        error: function() {
            if (isProcessing) {
                showToast('warning', '获取进度更新失败');
            }
        }
    });
}

function updateProgressDisplay(data) {
    // 更新统计信息
    $('#completedQuestions').text(data.completedQuestions);
    $('#totalQuestions').text(data.totalQuestions);
    $('#processingSpeed').text(data.metrics.questionsPerSecond);
    $('#activeThreads').text(data.metrics.activeThreads);
    
    // 更新进度条
    const percentage = data.progressPercentage;
    $('#progressFill').css('width', percentage + '%');
    $('#progressText').text(Math.round(percentage) + '%');
    
    // 更新线程状态
    updateThreadStatus(data.taskStatuses);
    
    // 更新性能指标
    $('#cpuUsage').text(data.metrics.cpuUsagePercent.toFixed(1) + '%');
    $('#memoryUsage').text(formatBytes(data.metrics.memoryUsageBytes));
    $('#processingTime').text(formatDuration(data.metrics.processingDuration));
}

function updateThreadStatus(taskStatuses) {
    const threadList = $('#threadList');
    threadList.empty();
    
    taskStatuses.forEach(task => {
        // 确保task.status是字符串类型
        const status = task.status ? String(task.status) : 'unknown';
        const statusClass = `status-${status.toLowerCase().replace('progress', 'inprogress')}`;
        const threadItem = $(`
            <div class="thread-item">
                <div class="thread-info">
                    <div class="thread-id">Thread ${task.threadId || 'N/A'}</div>
                    <div class="thread-questions">Questions ${task.startQuestionNumber || 'N/A'}-${task.endQuestionNumber || 'N/A'}</div>
                </div>
                <div class="thread-status-badge ${statusClass}">${status}</div>
            </div>
        `);
        threadList.append(threadItem);
    });
}

function processingCompleted() {
    clearInterval(progressTimer);
    isProcessing = false;
    showToast('success', '处理成功完成！');
    
    // 加载结果
    loadResults();
}

function processingFailed(errorMessage) {
    clearInterval(progressTimer);
    isProcessing = false;
    
    showToast('error', '处理失败：' + (errorMessage || '未知错误'));
}

function processingCancelled() {
    clearInterval(progressTimer);
    isProcessing = false;
    
    showToast('warning', '处理已被取消');
    resetToUpload();
}

function cancelProcessing() {
    if (!currentSessionId) return;
    
    $.ajax({
        url: API_ENDPOINTS.cancelProcessing(currentSessionId),
        method: 'POST',
        success: function(response) {
            if (response.success) {
                showToast('success', '处理已取消');
                processingCancelled();
            } else {
                showToast('error', '取消处理失败');
            }
        },
        error: function() {
            showToast('error', '取消处理失败');
        }
    });
}

// 结果处理
function loadResults() {
    $.ajax({
        url: API_ENDPOINTS.getQuestions(currentSessionId),
        method: 'GET',
        success: function(response) {
            if (response.success) {
                currentQuestions = response.data.questions;
                showResultsSection(response.data);
            } else {
                showToast('error', '加载结果失败');
            }
        },
        error: function() {
            showToast('error', '加载结果失败');
        }
    });
}

function showResultsSection(data) {
    $('#progressSection').hide();
    $('#resultsSection').show();
    
    // 更新摘要
    $('#totalExtracted').text(data.totalQuestions);
    $('#finalProcessingTime').text(formatDuration(data.metrics.processingDuration));
    $('#averageSpeed').text(data.metrics.questionsPerSecond + ' Q/s');
    
    // 显示题目
    displayQuestions(currentQuestions);
}

function displayQuestions(questions) {
    const questionsList = $('#questionsList');
    questionsList.empty();
    
    questions.forEach(question => {
        const questionItem = createQuestionElement(question);
        questionsList.append(questionItem);
    });
}

function createQuestionElement(question) {
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
        answerHtml = `<div class="question-answer"><strong>答案:</strong> ${escapeHtml(question.answer)}</div>`;
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

// 导出和重置函数
function exportResults() {
    if (!currentSessionId) return;
    
    const link = document.createElement('a');
    link.href = API_ENDPOINTS.exportResults(currentSessionId);
    link.download = `exam_results_${currentSessionId}.json`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    
    showToast('success', '结果导出成功');
}

function resetToUpload() {
    // 清除数据
    currentSessionId = null;
    currentQuestions = [];
    isProcessing = false;
    
    // 清除计时器
    if (progressTimer) {
        clearInterval(progressTimer);
        progressTimer = null;
    }
    
    // 重置界面
    clearFileSelection();
    $('#progressSection').hide();
    $('#resultsSection').hide();
    $('.upload-section').show();
    
    // 重置表单值
    $('#questionsPerThread').val(5);
    $('#maxThreads').val(4);
    $('#typeFilter').val('all');
    $('#searchInput').val('');
}

// 工具函数
function showLoading(text = '加载中...') {
    $('#loadingText').text(text);
    $('#loadingOverlay').show();
}

function hideLoading() {
    $('#loadingOverlay').hide();
}

function updateLoadingText(text) {
    $('#loadingText').text(text);
}

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

function formatFileSize(bytes) {
    if (bytes === 0) return '0 Bytes';
    
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function formatBytes(bytes) {
    return formatFileSize(bytes);
}

function formatDuration(duration) {
    // 持续时间以TimeSpan字符串形式传递，例如 "00:02:15"
    if (typeof duration === 'string') {
        return duration;
    }
    
    // 如果是毫秒
    const totalSeconds = Math.floor(duration / 1000);
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

// 处理浏览器前进/后退
window.addEventListener('popstate', function() {
    // 如需要处理导航
});

// 处理页面卸载
window.addEventListener('beforeunload', function(e) {
    if (isProcessing) {
        e.preventDefault();
        e.returnValue = '正在处理中。确定要离开吗？';
    }
});