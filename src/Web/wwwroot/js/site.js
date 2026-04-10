// ====================================================
// SalesMind AI — Global Utilities
// ====================================================

(function () {
    'use strict';

    // ===== HEADER SCROLL EFFECT =====
    const header = document.getElementById('appHeader');
    if (header) {
        window.addEventListener('scroll', function () {
            if (window.scrollY > 10) {
                header.classList.add('scrolled');
            } else {
                header.classList.remove('scrolled');
            }
        }, { passive: true });
    }

    // ===== FORMAT UTILITIES =====
    window.SalesMind = {
        formatCurrency: function (value) {
            if (value >= 1e6) {
                return '$' + (value / 1e6).toFixed(1) + 'M';
            } else if (value >= 1e3) {
                return '$' + (value / 1e3).toFixed(1) + 'K';
            } else {
                return '$' + new Intl.NumberFormat('en-US').format(value);
            }
        },
        formatNumber: function (value) {
            return new Intl.NumberFormat('en-US').format(value);
        },
        formatPercent: function (value) {
            return Math.round(value * 100) + '%';
        }
    };

})();
