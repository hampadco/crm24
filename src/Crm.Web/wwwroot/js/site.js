// Global customer-facing site scripts.

(function () {
    'use strict';

    function initMobileMenu() {
        var toggle = document.getElementById('tabenMenuToggle');
        var closeBtn = document.getElementById('tabenNavClose');
        var nav = document.getElementById('tabenNav');
        var backdrop = document.getElementById('tabenNavBackdrop');
        if (!toggle || !nav) {
            return;
        }

        function open() {
            nav.classList.add('is-open');
            if (backdrop) {
                backdrop.setAttribute('aria-hidden', 'false');
                requestAnimationFrame(function () {
                    backdrop.classList.add('is-visible');
                });
            }
            toggle.setAttribute('aria-expanded', 'true');
            document.body.classList.add('taben-nav-lock');
        }

        function close() {
            nav.classList.remove('is-open');
            if (backdrop) {
                backdrop.classList.remove('is-visible');
                backdrop.setAttribute('aria-hidden', 'true');
            }
            toggle.setAttribute('aria-expanded', 'false');
            document.body.classList.remove('taben-nav-lock');
        }

        toggle.addEventListener('click', function () {
            if (nav.classList.contains('is-open')) {
                close();
            } else {
                open();
            }
        });

        if (closeBtn) {
            closeBtn.addEventListener('click', close);
        }

        if (backdrop) {
            backdrop.addEventListener('click', close);
        }

        nav.addEventListener('click', function (e) {
            if (e.target.closest('a')) {
                close();
            }
        });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                close();
            }
        });

        window.addEventListener('resize', function () {
            if (window.innerWidth >= 992) {
                close();
            }
        });
    }

    function initHeaderScroll() {
        var header = document.querySelector('.taben-header');
        if (!header) {
            return;
        }
        function onScroll() {
            if (window.scrollY > 8) {
                header.classList.add('is-scrolled');
            } else {
                header.classList.remove('is-scrolled');
            }
        }
        onScroll();
        window.addEventListener('scroll', onScroll, { passive: true });
    }

    function initFaqAccordion() {
        document.querySelectorAll('[data-taben-faq]').forEach(function (list) {
            var items = Array.prototype.slice.call(list.querySelectorAll('.taben-faq-item'));
            items.forEach(function (item) {
                var btn = item.querySelector('.taben-faq-q');
                if (!btn) {
                    return;
                }
                btn.addEventListener('click', function () {
                    var isOpen = item.classList.contains('is-open');
                    items.forEach(function (other) {
                        other.classList.remove('is-open');
                        var ob = other.querySelector('.taben-faq-q');
                        if (ob) {
                            ob.setAttribute('aria-expanded', 'false');
                        }
                    });
                    if (!isOpen) {
                        item.classList.add('is-open');
                        btn.setAttribute('aria-expanded', 'true');
                    }
                });
            });
        });
    }

    function initFooterAccordion() {
        var blocks = document.querySelectorAll('.taben-footer-block');
        if (blocks.length === 0) {
            return;
        }

        function sync() {
            var desktop = window.innerWidth >= 992;
            blocks.forEach(function (block, index) {
                if (desktop || index === 0) {
                    block.setAttribute('open', '');
                } else {
                    block.removeAttribute('open');
                }
            });
        }

        sync();
        window.addEventListener('resize', sync);
    }

    document.addEventListener('DOMContentLoaded', function () {
        initMobileMenu();
        initHeaderScroll();
        initFaqAccordion();
        initFooterAccordion();
    });
})();
