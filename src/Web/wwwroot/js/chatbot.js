// ====================================================
// SalesMind AI — Chatbot Widget Logic
// ====================================================

(function () {
    'use strict';

    // DOM Elements
    const widget = document.getElementById('chatbotWidget');
    const fab = document.getElementById('chatbotFab');
    const chatWindow = document.getElementById('chatbotWindow');
    const chatMessages = document.getElementById('chatMessages');
    const chatInput = document.getElementById('chatInput');
    const btnSend = document.getElementById('btnSendMessage');
    const btnMinimize = document.getElementById('btnMinimize');
    const btnClear = document.getElementById('btnClearChat');
    const chatStatus = document.getElementById('chatStatus');
    const quickActions = document.querySelectorAll('.quick-action-btn');

    let isProcessing = false;
    let chatChartCounter = 0;
    
    // State
    const state = {
        useStreaming: true,
        history: [],
        charts: new Map()
    };

    // ===== INITIALIZATION =====
    document.addEventListener('DOMContentLoaded', function () {
        initChatToggle();
        initMessageSend();
        initQuickActions();
        initChatActions();
    });

    // ===== TOGGLE CHAT =====
    function initChatToggle() {
        if (fab) {
            fab.addEventListener('click', function () {
                widget.classList.toggle('open');
                if (widget.classList.contains('open')) {
                    chatInput.focus();
                }
            });
        }

        if (btnMinimize) {
            btnMinimize.addEventListener('click', function () {
                widget.classList.remove('open');
            });
        }
    }

    // ===== INIT MESSAGE SEND =====
    function initMessageSend() {
        if (btnSend) {
            btnSend.addEventListener('click', sendMessage);
        }
        if (chatInput) {
            chatInput.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    sendMessage();
                }
            });
        }
    }

    // ===== QUICK ACTIONS =====
    function initQuickActions() {
        quickActions.forEach(btn => {
            btn.addEventListener('click', function () {
                const message = this.dataset.message;
                if (message && !isProcessing) {
                    chatInput.value = message;
                    sendMessage();
                }
            });
        });
    }

    // ===== CHAT ACTIONS =====
    function initChatActions() {
        if (btnClear) {
            btnClear.addEventListener('click', function () {
                // Keep only the welcome message
                const messages = chatMessages.querySelectorAll('.chat-message, .typing-indicator');
                const all = Array.from(messages);
                all.forEach((msg, idx) => {
                    if (idx > 0) msg.remove();
                });
                state.history = [];
            });
        }
    }

    // ===== SEND MESSAGE =====
    function sendMessage() {
        const text = chatInput?.value?.trim();
        if (!text || isProcessing) return;

        appendUserMessage(text);
        chatInput.value = '';

        // Save to local history
        state.history.push({ role: 'user', content: text });

        setProcessing(true);
        showTypingIndicator();

        if (state.useStreaming) {
            sendStreaming(text);
        } else {
            sendAjax(text);
        }
    }

    // ===== AJAX SEND =====
    function sendAjax(text) {
        fetch('/api/chat/send', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                message: text,
                history: state.history.slice(-20)
            })
        })
        .then(res => {
            if (!res.ok) return res.json().then(err => Promise.reject(err));
            return res.json();
        })
        .then(response => {
            removeTypingIndicator();
            appendAiMessage(response);
            state.history.push({ role: 'assistant', content: JSON.stringify(response) });
            setProcessing(false);
        })
        .catch(err => {
            removeTypingIndicator();
            appendAiMessage({
                type: 'error',
                text: err?.text || '❌ Xin lỗi, đã xảy ra lỗi. Vui lòng thử lại.',
                suggestions: ['Xem xu hướng doanh thu', 'So sánh khu vực', 'Tổng quan KPI']
            });
            setProcessing(false);
        });
    }

    // ===== STREAMING SEND =====
    function sendStreaming(text) {
        const url = `/api/chat/stream?message=${encodeURIComponent(text)}`;
        const es = new EventSource(url);

        let fullData = '';

        es.onmessage = e => {
            if (e.data === '[DONE]') {
                es.close();
                removeTypingIndicator();
                try {
                    let parsed = null;
                    if (fullData.trim().startsWith('{')) {
                         const jsonStart = fullData.indexOf('{');
                         const jsonEnd = fullData.lastIndexOf('}');
                         if (jsonStart >= 0 && jsonEnd > jsonStart) {
                             parsed = JSON.parse(fullData.slice(jsonStart, jsonEnd + 1));
                         }
                    } else if (fullData.length > 0) {
                        parsed = { type: 'analysis', text: fullData };
                    }
                    if (parsed) {
                        appendAiMessage(parsed);
                        state.history.push({ role: 'assistant', content: JSON.stringify(parsed) });
                    }
                } catch (ex) {
                    appendAiMessage({ type: 'error', text: '❌ Phản hồi không hợp lệ.', suggestions: [] });
                }
                setProcessing(false);
                return;
            }

            // Immediately handle rate limit JSON wrapped chunk or error
            if (e.data.trim().startsWith('{') && e.data.includes('"type":"error"')) {
                try {
                    const parsedError = JSON.parse(e.data);
                    es.close();
                    removeTypingIndicator();
                    appendAiMessage(parsedError);
                    setProcessing(false);
                } catch(ex){}
                return;
            }

            const chunk = e.data.startsWith('data: ') ? e.data.slice(6) : e.data;
            fullData += chunk;
        };

        es.onerror = () => {
            es.close();
            removeTypingIndicator();
            appendAiMessage({ type: 'error', text: '❌ Lỗi kết nối. Vui lòng thử lại.', suggestions: [] });
            setProcessing(false);
        };
    }

    // ===== MESSAGE RENDERING =====
    function appendUserMessage(text) {
        const now = new Date();
        const timeStr = now.getHours().toString().padStart(2, '0') + ':' +
            now.getMinutes().toString().padStart(2, '0');

        const messageHtml = `
            <div class="chat-message user-message">
                <div class="message-content">
                    <div class="message-bubble">${escapeHtml(text)}</div>
                    <span class="message-time">${timeStr}</span>
                </div>
            </div>`;
        chatMessages.insertAdjacentHTML('beforeend', messageHtml);
        scrollToBottom();
    }

    function appendAiMessage(response) {
        const now = new Date();
        const timeStr = now.getHours().toString().padStart(2, '0') + ':' +
            now.getMinutes().toString().padStart(2, '0');

        let formattedText = formatText(response.text || '');

        let chartHtml = '';
        if (response.chart && response.data && response.data.length > 0) {
            const chartId = 'chatChart_' + (++chatChartCounter);
            chartHtml = `
                <div class="chat-chart-container" onclick="window.ChatBot.expandChart('${chartId}', '${escapeAttr(response.chart.title || '')}')">
                    <div class="chat-chart-title">
                        <i class="fas fa-chart-bar"></i> ${escapeHtml(response.chart.title || 'Biểu đồ')}
                    </div>
                    <canvas id="${chartId}" height="140"></canvas>
                    <div class="chart-expand-hint">
                        <i class="fas fa-expand-alt"></i> Nhấn để phóng to
                    </div>
                </div>`;
        }

        let suggestionsHtml = '';
        if (response.suggestions && response.suggestions.length > 0) {
            suggestionsHtml = '<div class="message-suggestions">';
            response.suggestions.forEach(s => {
                suggestionsHtml += `<button class="suggestion-btn" onclick="window.ChatBot.sendSuggestion('${escapeAttr(s)}')">${escapeHtml(s)}</button>`;
            });
            suggestionsHtml += '</div>';
        }

        let modelHtml = '';
        if (response.model) {
            modelHtml = `<div class="message-model-tag"><i class="fas fa-microchip"></i> AI: ${escapeHtml(response.model)}</div>`;
        }

        const messageHtml = `
            <div class="chat-message ai-message ${response.type === 'error' ? 'error' : ''}">
                <div class="message-avatar">
                    <i class="fas fa-robot"></i>
                </div>
                <div class="message-content">
                    <div class="message-bubble">
                        ${formattedText}
                        ${chartHtml}
                    </div>
                    ${modelHtml}
                    ${suggestionsHtml}
                    <span class="message-time">${timeStr}</span>
                </div>
            </div>`;
        chatMessages.insertAdjacentHTML('beforeend', messageHtml);

        if (response.chart && response.data && response.data.length > 0) {
            const chartId = 'chatChart_' + chatChartCounter;
            
            // Store data for expansion
            state.charts.set(chartId, { info: response.chart, data: response.data });

            setTimeout(() => {
                if (window.DashboardCharts) {
                    window.DashboardCharts.renderChart(chartId, response.chart, response.data);
                }
            }, 100);
        }

        scrollToBottom();
    }

    // ===== TYPING INDICATOR =====
    function showTypingIndicator() {
        if (document.getElementById('typingIndicator')) return;
        const html = `
            <div class="typing-indicator" id="typingIndicator">
                <div class="message-avatar">
                    <i class="fas fa-robot"></i>
                </div>
                <div class="typing-dots">
                    <span></span><span></span><span></span>
                </div>
            </div>`;
        chatMessages.insertAdjacentHTML('beforeend', html);
        scrollToBottom();
    }

    function removeTypingIndicator() {
        const indicator = document.getElementById('typingIndicator');
        if (indicator) indicator.remove();
    }

    // ===== STATUS UPDATE =====
    function setProcessing(val) {
        isProcessing = val;
        updateStatus(val ? 'thinking' : 'online');
        if (btnSend) btnSend.disabled = val;
    }

    function updateStatus(status) {
        if (!chatStatus) return;
        if (status === 'thinking') {
            chatStatus.innerHTML = '<span class="status-dot thinking"></span> Đang phân tích...';
        } else {
            chatStatus.innerHTML = '<span class="status-dot online"></span> Sẵn sàng';
        }
    }

    // ===== HELPERS =====
    function scrollToBottom() {
        if (chatMessages) {
            setTimeout(() => {
                chatMessages.scrollTop = chatMessages.scrollHeight;
            }, 50);
        }
    }

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function escapeAttr(text) {
        return (text || '').toString().replace(/'/g, "\\'").replace(/"/g, '&quot;');
    }

    function formatText(text) {
        text = text.replace(/\*\*(.*)\*\*/gm, '<strong>$1</strong>');
        text = text.replace(/\n((?:\*|-)\s)/g, '<br>$1');
        text = text.replace(/\n/g, '<br>');
        return '<p>' + text + '</p>';
    }

    // ===== PUBLIC API =====
    window.ChatBot = {
        sendSuggestion: function (message) {
            if (!isProcessing) {
                chatInput.value = message;
                sendMessage();
            }
        },
        expandChart: function (chartId, title) {
            const modal = document.getElementById('chartModal');
            const modalTitle = document.getElementById('chartModalTitle');
            const modalCanvas = document.getElementById('chartModalCanvas');

            if (!modal || !modalCanvas) return;

            const chartData = state.charts.get(chartId);
            if (!chartData) return;

            if (modalTitle) modalTitle.textContent = title || chartData.info.title || 'Biểu đồ';

            const existingChart = Chart.getChart(modalCanvas);
            if (existingChart) existingChart.destroy();

            // Render fresh chart in modal using global logic
            if (window.DashboardCharts) {
                // We use a clone of info to display legend in modal
                const modalChartInfo = { ...chartData.info };
                window.DashboardCharts.renderChart('chartModalCanvas', modalChartInfo, chartData.data);
                
                // Final tweak: ensure legend is visible in modal for better UX
                const mChart = Chart.getChart(modalCanvas);
                if (mChart && mChart.config.options.plugins) {
                    mChart.config.options.plugins.legend = mChart.config.options.plugins.legend || {};
                    mChart.config.options.plugins.legend.display = true;
                    mChart.update();
                }
            }

            const bsModal = new bootstrap.Modal(modal);
            bsModal.show();
        }
    };

})();
