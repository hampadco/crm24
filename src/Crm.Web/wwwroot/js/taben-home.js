(function () {
    function initCarousel(carousel) {
        var viewport = carousel.querySelector('.taben-carousel-viewport');
        var track = carousel.querySelector('.taben-carousel-track');
        if (!viewport || !track) {
            return;
        }

        var slides = Array.prototype.slice.call(track.querySelectorAll('.taben-carousel-slide'));
        if (slides.length <= 1) {
            carousel.classList.remove('is-scrollable');
            return;
        }

        var prevBtn = carousel.querySelector('[data-carousel-prev]');
        var nextBtn = carousel.querySelector('[data-carousel-next]');

        function syncScrollableClass() {
            var canScroll = viewport.scrollWidth > viewport.clientWidth + 2;
            carousel.classList.toggle('is-scrollable', canScroll);
            return canScroll;
        }

        function pageSize() {
            var first = slides[0];
            var slideW = first ? first.getBoundingClientRect().width : viewport.clientWidth;
            var gap = parseFloat(getComputedStyle(track).columnGap || getComputedStyle(track).gap || '0') || 0;
            var perView = Math.max(1, Math.round(viewport.clientWidth / (slideW + gap)));
            return (slideW + gap) * perView;
        }

        function maxScroll() {
            return Math.max(0, viewport.scrollWidth - viewport.clientWidth);
        }

        function updateButtons() {
            syncScrollableClass();
            var pos = Math.abs(viewport.scrollLeft);
            var max = maxScroll();
            if (prevBtn) {
                prevBtn.disabled = pos <= 1;
            }
            if (nextBtn) {
                nextBtn.disabled = pos >= max - 1;
            }
        }

        function scrollByAmount(amount) {
            viewport.scrollBy({ left: amount, behavior: 'smooth' });
        }

        if (prevBtn) {
            prevBtn.addEventListener('click', function () {
                scrollByAmount(pageSize());
            });
        }
        if (nextBtn) {
            nextBtn.addEventListener('click', function () {
                scrollByAmount(-pageSize());
            });
        }

        viewport.addEventListener('scroll', function () {
            window.requestAnimationFrame(updateButtons);
        });
        window.addEventListener('resize', function () {
            window.requestAnimationFrame(updateButtons);
        });

        if (typeof ResizeObserver !== 'undefined') {
            var ro = new ResizeObserver(function () {
                window.requestAnimationFrame(updateButtons);
            });
            ro.observe(viewport);
            ro.observe(track);
        }

        // Mouse drag only — touch uses native horizontal scroll (pan-x).
        var drag = { active: false, startX: 0, startScroll: 0, moved: false };

        viewport.addEventListener('pointerdown', function (event) {
            if (event.pointerType === 'touch') {
                return;
            }
            if (event.pointerType === 'mouse' && event.button !== 0) {
                return;
            }
            drag.active = true;
            drag.moved = false;
            drag.startX = event.clientX;
            drag.startScroll = viewport.scrollLeft;
            viewport.classList.add('is-dragging');
            if (viewport.setPointerCapture) {
                viewport.setPointerCapture(event.pointerId);
            }
        });

        viewport.addEventListener('pointermove', function (event) {
            if (!drag.active) {
                return;
            }
            var delta = event.clientX - drag.startX;
            if (Math.abs(delta) > 4) {
                drag.moved = true;
            }
            viewport.scrollLeft = drag.startScroll - delta;
        });

        function endDrag(event) {
            if (!drag.active) {
                return;
            }
            drag.active = false;
            viewport.classList.remove('is-dragging');
            if (event && viewport.releasePointerCapture) {
                try {
                    viewport.releasePointerCapture(event.pointerId);
                } catch (err) {
                    /* pointer already released */
                }
            }
            updateButtons();
        }

        viewport.addEventListener('pointerup', endDrag);
        viewport.addEventListener('pointercancel', endDrag);

        viewport.addEventListener('click', function (event) {
            if (drag.moved) {
                event.preventDefault();
                event.stopPropagation();
                drag.moved = false;
            }
        }, true);

        updateButtons();
    }

    function init() {
        document.querySelectorAll('[data-taben-carousel]').forEach(initCarousel);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
