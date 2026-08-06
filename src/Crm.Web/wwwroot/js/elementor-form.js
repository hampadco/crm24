/**
 * قبل از submit فرم‌های data-elementor-form، HTML بوم Elementor را در فیلد hidden می‌نویسد.
 */
(function () {
  function init(root) {
    (root || document).querySelectorAll('form[data-elementor-form]').forEach(function (form) {
      if (form.dataset.elementorSubmitInit === '1') return;
      form.dataset.elementorSubmitInit = '1';

      form.addEventListener('submit', function (e) {
        if (!window.elementorBuilder || form.dataset.contentSaved === '1') return;
        e.preventDefault();
        try {
          window.elementorBuilder.save();
        } catch (err) {
          console.error(err);
        }
        form.dataset.contentSaved = '1';
        if (typeof form.requestSubmit === 'function') form.requestSubmit();
        else form.submit();
      });
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function () { init(document); });
  } else {
    init(document);
  }

  window.initElementorFormSubmit = init;
})();
