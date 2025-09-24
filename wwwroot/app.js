// 全局变量
let currentSessionId = null;
let progressTimer = null;
let currentQuestions = [];
let isProcessing = false;

// API configuration
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

// Initialize application
$(document).ready(function() {
    initializeEventHandlers();
    checkSystemHealth();
    
    // Check system health every 30 seconds
    setInterval(checkSystemHealth, 30000);
});

// Event handlers
function initializeEventHandlers() {
    // Unbind all events first to prevent duplicate binding
    $('#browseBtn').off('click');
    $('#fileInput').off('change');
    $('#uploadArea').off('click dragover dragleave drop');
    
    // File upload handlers
    $('#browseBtn').click(function(e) {
        e.preventDefault();
        e.stopPropagation(); // Prevent event bubbling
        $('#fileInput').trigger('click');
    });
    $('#fileInput').change(handleFileSelect);
    
    // Drag and drop handlers
    $('#uploadArea')
        .on('dragover', handleDragOver)
        .on('dragleave', handleDragLeave)
        .on('drop', handleFileDrop)
        .click(function(e) {
            // Only trigger file selection when clicking is not on button or its child elements
            if (!$(e.target).closest('#browseBtn').length && e.target !== this) {
                return; // Avoid triggering in button area
            }
            if (e.target === this) {
                $('#fileInput').trigger('click');
            }
        });
    
    // Action buttons
    $('#removeFile').click(clearFileSelection);
    $('#startProcessing').click(startProcessing);
    $('#cancelUpload').click(clearFileSelection);
    $('#cancelProcessing').click(cancelProcessing);
    $('#exportResults').click(exportResults);
    $('#newUpload').click(resetToUpload);
    
    // Filter handlers
    $('#typeFilter').change(filterQuestions);
    $('#searchInput').on('input', filterQuestions);
    
    // Prevent document's default drag behavior
    $(document).on('dragover drop', function(e) {
        e.preventDefault();
    });
}

// System health check
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

// File handling
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
    // Validate file
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
    
    // Display file preview
    displayFilePreview(file);
}

function displayFilePreview(file) {
    $('#fileName').text(file.name);
    $('#fileSize').text(formatFileSize(file.size));
    
    // Set file icon based on type
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
    
    // Show preview and configuration sections
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

// Processing functions
function startProcessing() {
    const file = $('#fileInput')[0].files[0];
    if (!file) {
        showToast('error', '请先选择文件。');
        return;
    }
    
    // Show loading state
    showLoading('正在上传文件...');
    
    // Create form data
    const formData = new FormData();
    formData.append('file', file);
    
    // Upload file
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
            const errorMsg = xhr.responseJSON?.message || 'Upload failed';
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
                showToast('success', 'Processing started successfully');
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
    // First clear any existing timer
    if (progressTimer) {
        clearInterval(progressTimer);
        progressTimer = null;
    }
    
    // Update progress immediately
    updateProgress();
    
    // Set timer for progress updates
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
                // Check if processing is completed
                if (data.status === 4) { // Completed
                    processingCompleted();
                } else if (data.status === 5) { // Failed
                    processingFailed(data.errorMessage);
                } else if (data.status === 6) { // Cancelled
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
    // Update statistics
    $('#completedQuestions').text(data.completedQuestions);
    $('#totalQuestions').text(data.totalQuestions);
    $('#processingSpeed').text(data.metrics.questionsPerSecond);
    $('#activeThreads').text(data.metrics.activeThreads);
    
    // Update progress bar
    const percentage = data.progressPercentage;
    $('#progressFill').css('width', percentage + '%');
    $('#progressText').text(Math.round(percentage) + '%');
    
    // Update thread status
    updateThreadStatus(data.taskStatuses);
    
    // Update performance metrics
    $('#cpuUsage').text(data.metrics.cpuUsagePercent.toFixed(1) + '%');
    $('#memoryUsage').text(formatBytes(data.metrics.memoryUsageBytes));
    $('#processingTime').text(formatDuration(data.metrics.processingDuration));
}

function updateThreadStatus(taskStatuses) {
    const threadList = $('#threadList');
    threadList.empty();
    
    taskStatuses.forEach(task => {
        // Ensure task.status is string type
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
    
    // Load results
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

// Result handling
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
    
    // Update summary
    $('#totalExtracted').text(data.totalQuestions);
    $('#finalProcessingTime').text(formatDuration(data.metrics.processingDuration));
    $('#averageSpeed').text(data.metrics.questionsPerSecond + ' Q/s');
    
    // Display questions
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
        answerHtml = `<div class="question-answer"><strong>Answer:</strong> ${escapeHtml(question.answer)}</div>`;
    }
    
    return $(`
        <div class="question-item" data-type="${question.type}" data-content="${escapeHtml(question.content.toLowerCase())}">
            <div class="question-header">
                <div style="display: flex; align-items: center; gap: 10px;">
                    <span class="question-number">Q${question.questionNumber}</span>
                    <span class="question-type" style="background-color: ${typeColor}; color: white;">${question.typeDisplayName}</span>
                </div>
                <div class="question-points">${question.points} pts</div>
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
        
        // Type filtering
        if (typeFilter !== 'all' && itemType !== typeFilter) {
            visible = false;
        }
        
        // Search filtering
        if (searchText && !itemContent.includes(searchText)) {
            visible = false;
        }
        
        $item.toggle(visible);
    });
}

// Export and reset functions
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
    // Clear data
    currentSessionId = null;
    currentQuestions = [];
    isProcessing = false;
    
    // Clear timer
    if (progressTimer) {
        clearInterval(progressTimer);
        progressTimer = null;
    }
    
    // Reset interface
    clearFileSelection();
    $('#progressSection').hide();
    $('#resultsSection').hide();
    $('.upload-section').show();
    
    // Reset form values
    $('#questionsPerThread').val(5);
    $('#maxThreads').val(4);
    $('#typeFilter').val('all');
    $('#searchInput').val('');
}

// Utility functions
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
    
    // Add click handler for close button
    toast.find('.toast-close').click(function() {
        toast.remove();
    });
    
    // Add to container
    $('#toastContainer').append(toast);
    
    // Auto remove after duration
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
    // Duration passed as TimeSpan string, e.g. "00:02:15"
    if (typeof duration === 'string') {
        return duration;
    }
    
    // If in milliseconds
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

// Handle browser forward/back
window.addEventListener('popstate', function() {
    // Handle navigation if needed
});

// Handle page unload
window.addEventListener('beforeunload', function(e) {
    if (isProcessing) {
        e.preventDefault();
        e.returnValue = '正在处理中。确定要离开吗？';
    }
});