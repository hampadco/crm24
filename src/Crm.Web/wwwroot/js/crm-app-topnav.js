(function () {
  function init() {
    var toggle = document.getElementById('crmTopnavToggle');
    var panel = document.getElementById('crm-topnav-panel');
    var backdrop = document.getElementById('crm-topnav-backdrop');
    var closeBtn = document.getElementById('crmTopnavClose');
    if (!toggle || !panel || !backdrop) return;

    var closingTimer = null;

    function openMenu() {
      if (closingTimer) {
        clearTimeout(closingTimer);
        closingTimer = null;
      }
      panel.hidden = false;
      backdrop.hidden = false;
      // force reflow so transition runs
      void panel.offsetWidth;
      panel.classList.add('is-open');
      backdrop.classList.add('is-visible');
      toggle.classList.add('is-open');
      toggle.setAttribute('aria-expanded', 'true');
      document.body.classList.add('crm-topnav-open');
    }

    function closeMenu() {
      panel.classList.remove('is-open');
      backdrop.classList.remove('is-visible');
      toggle.classList.remove('is-open');
      toggle.setAttribute('aria-expanded', 'false');
      document.body.classList.remove('crm-topnav-open');
      closingTimer = setTimeout(function () {
        panel.hidden = true;
        backdrop.hidden = true;
        closingTimer = null;
      }, 320);
    }

    function isOpen() {
      return panel.classList.contains('is-open');
    }

    function selectCat(cat) {
      panel.querySelectorAll('.crm-menu-cat').forEach(function (btn) {
        btn.classList.toggle('is-active', btn.getAttribute('data-cat') === cat);
      });
      panel.querySelectorAll('.crm-menu-pane').forEach(function (pane) {
        var active = pane.getAttribute('data-pane') === cat;
        pane.classList.toggle('is-active', active);
        if (active) {
          pane.style.animation = 'none';
          void pane.offsetWidth;
          pane.style.animation = '';
        }
      });
    }

    toggle.addEventListener('click', function (e) {
      e.preventDefault();
      e.stopPropagation();
      if (isOpen()) closeMenu();
      else openMenu();
    });

    if (closeBtn) closeBtn.addEventListener('click', closeMenu);
    backdrop.addEventListener('click', closeMenu);

    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && isOpen()) closeMenu();
    });

    panel.querySelectorAll('.crm-menu-cat').forEach(function (btn) {
      btn.addEventListener('click', function () {
        selectCat(btn.getAttribute('data-cat'));
      });
    });

    panel.querySelectorAll('.crm-menu-links a').forEach(function (a) {
      a.addEventListener('click', closeMenu);
    });
  }

  if (document.readyState === 'loading')
    document.addEventListener('DOMContentLoaded', init);
  else
    init();
})();
