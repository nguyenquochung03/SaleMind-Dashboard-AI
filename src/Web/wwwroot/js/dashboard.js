// ====================================================
// SalesMind AI — Dashboard Charts & Data Logic
// ====================================================

(function () {
    'use strict';

    // Chart.js default configuration
    const chartDefaults = {
        fontFamily: "'Google Sans', 'Inter', sans-serif",
        colors: {
            primary: '#3B82F6',
            primaryLight: '#93C5FD',
            accent: '#6366F1',
            success: '#10B981',
            warning: '#F59E0B',
            danger: '#EF4444',
            info: '#06B6D4',
            purple: '#8B5CF6',
            grid: '#E2E8F0',
            text: '#64748B',
            textDark: '#0F172A',
        }
    };

    // Chart instances storage
    let charts = {};

    // ===== INITIALIZATION =====
    document.addEventListener('DOMContentLoaded', function () {
        setupChartDefaults();
        initAllCharts();
        initFilters();
    });

    function setupChartDefaults() {
        if (typeof Chart === 'undefined') return;
        Chart.defaults.font.family = chartDefaults.fontFamily;
        Chart.defaults.font.size = 12;
        Chart.defaults.plugins.legend.labels.usePointStyle = true;
        Chart.defaults.plugins.legend.labels.padding = 16;
        Chart.defaults.plugins.tooltip.cornerRadius = 8;
        Chart.defaults.plugins.tooltip.padding = 10;
        Chart.defaults.plugins.tooltip.titleFont = { weight: '600' };
        Chart.defaults.animation.duration = 800;
        Chart.defaults.animation.easing = 'easeOutQuart';
    }

    async function initAllCharts() {
        const defaultRange = '12months';
        await refreshDashboard(defaultRange);
    }

    async function refreshDashboard(range) {
        // Refresh all charts & KPIs in parallel
        await Promise.all([
            loadChartData('revenue', range),
            loadChartData('region', range),
            loadChartData('pipeline', range),
            loadChartData('categories', range),
            loadChartData('products', range),
            loadKpiData(range)
        ]);
    }

    async function loadKpiData(range) {
        try {
            const response = await fetch(`/api/dashboard/kpi?range=${range}`);
            if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
            const data = await response.json();

            // Update KPI cards
            if (data.formattedRevenue) document.getElementById('kpiRevenueValue').textContent = data.formattedRevenue;
            if (data.totalOrders !== undefined) document.getElementById('kpiOrdersValue').textContent = data.totalOrders.toLocaleString('en-US');
            if (data.conversionRate !== undefined) document.getElementById('kpiConversionValue').textContent = Math.round(data.conversionRate * 100) + '%';
            if (data.formattedAvgDealSize) document.getElementById('kpiAvgDealValue').textContent = data.formattedAvgDealSize;

        } catch (error) {
            console.error('Error loading KPI data:', error);
        }
    }

    async function loadChartData(type, range) {
        const loadingId = getLoadingId(type);
        showLoading(loadingId);

        try {
            const response = await fetch(`/api/dashboard/${type}?range=${range}`);
            if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
            const data = await response.json();

            switch (type) {
                case 'revenue': renderRevenueChart(data); updateTable(data); break;
                case 'region': renderRegionChart(data); break;
                case 'pipeline': renderPipelineChart(data); break;
                case 'categories': renderCategoryChart(data); break;
                case 'products': renderProductChart(data); break;
            }
        } catch (error) {
            console.error(`Error loading ${type} data:`, error);
        } finally {
            hideLoading(loadingId);
        }
    }

    function initFilters() {
        // Global Dashboard Filter (Button Group)
        const filterContainer = document.getElementById('globalDashboardFilter');
        if (filterContainer) {
            const buttons = filterContainer.querySelectorAll('.filter-btn');
            buttons.forEach(btn => {
                btn.addEventListener('click', async function () {
                    // Remove active class from all and add to clicked
                    buttons.forEach(b => b.classList.remove('active'));
                    this.classList.add('active');

                    const range = this.getAttribute('data-range');
                    await refreshDashboard(range);
                });
            });
        }

        // Global Refresh Button
        const refreshBtn = document.getElementById('btnRefresh');
        if (refreshBtn) {
            refreshBtn.addEventListener('click', async function () {
                // Find currently active range
                const activeBtn = document.querySelector('#globalDashboardFilter .filter-btn.active');
                const range = activeBtn ? activeBtn.getAttribute('data-range') : '12months';

                // Visual feedback: Spin icon and disable button
                const icon = this.querySelector('i');
                if (icon) icon.classList.add('fa-spin');
                this.style.opacity = '0.5';
                this.style.pointerEvents = 'none';

                try {
                    await refreshDashboard(range);
                } finally {
                    // Restore state
                    if (icon) icon.classList.remove('fa-spin');
                    this.style.opacity = '1';
                    this.style.pointerEvents = 'auto';
                }
            });
        }
    }

    // ===== RENDER FUNCTIONS =====

    function renderRevenueChart(data) {
        const ctx = document.getElementById('revenueChart');
        if (!ctx) return;
        const canvas = ctx.getContext('2d');
        const gradient = canvas.createLinearGradient(0, 0, 0, 300);
        gradient.addColorStop(0, 'rgba(59, 130, 246, 0.15)');
        gradient.addColorStop(1, 'rgba(59, 130, 246, 0.01)');

        const labels = data.map(d => formatMonth(getValue(d, 'Date')));
        const values = data.map(d => Number(getValue(d, 'Revenue')) || 0);

        if (charts.revenue) charts.revenue.destroy();
        charts.revenue = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [{
                    label: 'Doanh thu',
                    data: values,
                    borderColor: chartDefaults.colors.primary,
                    backgroundColor: gradient,
                    borderWidth: 2.5,
                    fill: true,
                    tension: 0.4,
                    pointRadius: 4,
                    pointHoverRadius: 6,
                    pointBackgroundColor: '#fff',
                    pointBorderColor: chartDefaults.colors.primary,
                    pointBorderWidth: 2
                }]
            },
            options: getCommonOptions('currency')
        });
    }

    function renderRegionChart(data) {
        const ctx = document.getElementById('regionChart');
        if (!ctx) return;
        if (charts.region) charts.region.destroy();

        const labels = data.map(d => getValue(d, 'Region'));
        const values = data.map(d => Number(getValue(d, 'Revenue')) || 0);

        charts.region = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    data: values,
                    backgroundColor: chartDefaults.colors.primary + 'CC',
                    borderRadius: 6
                }]
            },
            options: getCommonOptions('currency', false)
        });
    }

    function renderPipelineChart(data) {
        const ctx = document.getElementById('pipelineChart');
        if (!ctx) return;
        if (charts.pipeline) charts.pipeline.destroy();

        charts.pipeline = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: data.map(d => getValue(d, 'Status')),
                datasets: [{
                    data: data.map(d => getValue(d, 'OrderCount')),
                    backgroundColor: [chartDefaults.colors.success, chartDefaults.colors.warning, chartDefaults.colors.info, chartDefaults.colors.danger]
                }]
            },
            options: { cutout: '70%', plugins: { legend: { position: 'bottom' } } }
        });
    }

    function renderCategoryChart(data) {
        const ctx = document.getElementById('categoryChart');
        if (!ctx) return;
        if (charts.categories) charts.categories.destroy();

        charts.categories = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: data.map(d => getValue(d, 'ProductCategory')),
                datasets: [{
                    label: 'Doanh thu',
                    data: data.map(d => getValue(d, 'Revenue')),
                    backgroundColor: [
                        chartDefaults.colors.primary,
                        chartDefaults.colors.accent,
                        chartDefaults.colors.purple,
                        chartDefaults.colors.success,
                        chartDefaults.colors.warning
                    ],
                    borderRadius: 4
                }]
            },
            options: {
                indexAxis: 'y',
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: function (ctx) {
                                return `Doanh thu: ${formatCurrency(ctx.parsed.x)}`;
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        beginAtZero: true,
                        ticks: {
                            callback: function (val) { return formatShortCurrency(val); }
                        }
                    }
                }
            }
        });
    }

    function renderProductChart(data) {
        const ctx = document.getElementById('productChart');
        if (!ctx) return;
        if (charts.products) charts.products.destroy();

        charts.products = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: data.map(d => getValue(d, 'ProductName')),
                datasets: [{
                    label: 'Doanh thu',
                    data: data.map(d => getValue(d, 'Revenue')),
                    backgroundColor: chartDefaults.colors.accent,
                    borderRadius: 4
                }]
            },
            options: { indexAxis: 'y', plugins: { legend: { display: false } } }
        });
    }

    // ===== UTILS =====

    function getCommonOptions(formatType, showLegend = false) {
        return {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { display: showLegend },
                tooltip: {
                    callbacks: {
                        label: function (ctx) {
                            let val = ctx.parsed.y || ctx.parsed || 0;
                            if (formatType === 'currency') return `Revenue: ${formatCurrency(val)}`;
                            return `Value: ${val}`;
                        }
                    }
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    ticks: {
                        callback: function (val) {
                            if (formatType === 'currency') return formatShortCurrency(val);
                            return val;
                        }
                    }
                }
            }
        };
    }

    function formatCurrency(val) {
        return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }).format(val);
    }

    function formatShortCurrency(val) {
        if (val >= 1000000) return '$' + (val / 1000000).toFixed(1) + 'M';
        if (val >= 1000) return '$' + (val / 1000).toFixed(0) + 'K';
        return '$' + val;
    }

    function getValue(obj, prop) {
        return obj[prop] !== undefined ? obj[prop] : obj[prop.charAt(0).toLowerCase() + prop.slice(1)];
    }

    function formatMonth(dateStr) {
        if (!dateStr) return '';
        const parts = dateStr.split('-');
        if (parts.length < 2) return dateStr;
        return `${parts[1]}/${parts[0].slice(2)}`;
    }

    function updateTable(data) {
        const tbody = document.getElementById('salesTableBody');
        if (!tbody) return;
        tbody.innerHTML = data.slice(0, 10).map(item => {
            const status = getValue(item, 'Status') || 'Thành công';
            const badgeClass = getStatusBadgeClass(status);
            return `
                <tr>
                    <td>${formatMonth(getValue(item, 'Date'))}</td>
                    <td class="text-center">${formatCurrency(getValue(item, 'Revenue'))}</td>
                    <td class="text-center">${getValue(item, 'OrderCount')}</td>
                    <td><span class="status-badge ${badgeClass}">${status}</span></td>
                </tr>
            `;
        }).join('');
    }

    function getStatusBadgeClass(status) {
        if (!status) return 'info';
        const s = status.toLowerCase();
        if (s.includes('thành công') || s.includes('hoàn thành')) return 'success';
        if (s.includes('xử lý')) return 'info';
        if (s.includes('chờ')) return 'warning';
        if (s.includes('thất bại') || s.includes('hủy')) return 'danger';
        return 'info';
    }

    function getLoadingId(type) {
        const map = { revenue: 'loadingRevenue', region: 'loadingRegion', pipeline: 'loadingPipeline', categories: 'loadingCategories', products: 'loadingProducts' };
        return map[type];
    }

    function showLoading(id) { const el = document.getElementById(id); if (el) el.classList.add('active'); }
    function hideLoading(id) { const el = document.getElementById(id); if (el) el.classList.remove('active'); }

    // ===== EXPOSE FOR CHATBOT =====
    window.DashboardCharts = {
        renderChart: function (canvasId, chartInfo, data) {
            const ctx = document.getElementById(canvasId);
            if (!ctx || !data || !data.length) return;

            const type = (chartInfo.type || 'line').toLowerCase();
            const xField = chartInfo.xField;
            const yField = chartInfo.yField;

            const labels = data.map(d => {
                const val = getValue(d, xField);
                // If it's a date/month field, try to format it
                if (xField.toLowerCase().includes('date') || xField.toLowerCase().includes('month')) {
                    const formatted = formatMonth(val);
                    return formatted || val;
                }
                return val;
            });
            
            const values = data.map(d => {
                const val = getValue(d, yField);
                return typeof val === 'number' ? val : (Number(val) || 0);
            });

            // Destroy existing chart if any
            const existingChart = Chart.getChart(ctx);
            if (existingChart) existingChart.destroy();

            const colors = [
                chartDefaults.colors.primary,
                chartDefaults.colors.accent,
                chartDefaults.colors.purple,
                chartDefaults.colors.success,
                chartDefaults.colors.warning,
                chartDefaults.colors.info,
                chartDefaults.colors.danger
            ];

            const config = {
                type: type === 'doughnut' ? 'doughnut' : (type === 'bar' ? 'bar' : 'line'),
                data: {
                    labels: labels,
                    datasets: [{
                        label: chartInfo.title || 'Dữ liệu',
                        data: values,
                        backgroundColor: type === 'doughnut' ? colors : colors[0] + 'CC',
                        borderColor: type === 'doughnut' ? '#fff' : colors[0],
                        borderWidth: type === 'line' ? 2 : 1
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: type === 'doughnut' },
                        tooltip: {
                            callbacks: {
                                label: function(context) {
                                    let label = context.dataset.label || '';
                                    if (label) label += ': ';
                                    if (context.parsed.y !== undefined) label += formatCurrency(context.parsed.y);
                                    else if (context.parsed !== undefined) label += formatCurrency(context.parsed);
                                    return label;
                                }
                            }
                        }
                    }
                }
            };

            // Custom adjustments for types
            if (type === 'line') {
                config.data.datasets[0].fill = true;
                config.data.datasets[0].tension = 0.4;
                config.data.datasets[0].backgroundColor = (context) => {
                    const canvas = context.chart.canvas;
                    const ctx = canvas.getContext('2d');
                    const gradient = ctx.createLinearGradient(0, 0, 0, 140);
                    gradient.addColorStop(0, chartDefaults.colors.primary + '33');
                    gradient.addColorStop(1, chartDefaults.colors.primary + '03');
                    return gradient;
                };
                config.options.scales = {
                    y: { beginAtZero: true, ticks: { callback: (v) => formatShortCurrency(v) } }
                };
            } else if (type === 'bar') {
                config.options.scales = {
                    y: { beginAtZero: true, ticks: { callback: (v) => formatShortCurrency(v) } }
                };
            } else if (type === 'doughnut') {
                config.options.cutout = '60%';
            }

            new Chart(ctx, config);
        }
    };

})();
