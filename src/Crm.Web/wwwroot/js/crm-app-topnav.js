(function () {
  function init() {
    var toggle = document.getElementById('crmTopnavToggle');
    var panel = document.getElementById('crm-topnav-panel');
    var backdrop = document.getElementById('crm-topnav-backdrop');
    if (!toggle || !panel || !backdrop) return;

    function openMenu() {
      panel.hidden = false;
      backdrop.hidden = false;
      toggle.classList.add('is-open');
      toggle.setAttribute('aria-expanded', 'true');
      document.body.classList.add('crm-topnav-open');
    }

    function closeMenu() {
      panel.hidden = true;
      backdrop.hidden = true;
      toggle.classList.remove('is-open');
      toggle.setAttribute('aria-expanded', 'false');
      document.body.classList.remove('crm-topnav-open');
    }

    function isOpen() {
      return !panel.hidden;
    }

    toggle.addEventListener('click', function (e) {
      e.preventDefault();
      e.stopPropagation();
      if (isOpen()) closeMenu();
      else openMenu();
    });

    backdrop.addEventListener('click', closeMenu);

    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && isOpen()) closeMenu();
    });

    panel.querySelectorAll('a').forEach(function (a) {
      a.addEventListener('click', closeMenu);
    });
  }

  if (document.readyState === 'loading')
    document.addEventListener('DOMContentLoaded', init);
  else
    init();
})();
