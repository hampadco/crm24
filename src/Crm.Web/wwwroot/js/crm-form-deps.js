/**
 * Form field/block visibility dependencies.
 * Rules on .crm-dep-target[data-visibility-rule]: {"field":"stage","op":"eq","value":"Closed Won"}
 * Watches inputs named f_{field} (CRM form) or fields[field].
 */
(function () {
  function findInput(fieldName) {
    return (
      document.querySelector('[name="f_' + fieldName + '"]') ||
      document.querySelector('[name="fields[' + fieldName + ']"]')
    );
  }

  function getValue(fieldName) {
    var el = findInput(fieldName);
    if (!el) return '';
    if (el.type === 'checkbox') return el.checked ? 'true' : 'false';
    return (el.value || '').toString();
  }

  function evalRule(rule, current) {
    var expected = (rule.value || '').toString();
    var op = (rule.op || 'eq').toLowerCase();
    if (op === 'eq') return current === expected;
    if (op === 'neq') return current !== expected;
    if (op === 'contains') return current.indexOf(expected) !== -1;
    return true;
  }

  function applyAll() {
    document.querySelectorAll('.crm-dep-target[data-visibility-rule]').forEach(function (target) {
      var raw = target.getAttribute('data-visibility-rule');
      if (!raw) {
        target.style.display = '';
        return;
      }
      var rule;
      try {
        rule = JSON.parse(raw);
      } catch {
        target.style.display = '';
        return;
      }
      if (!rule || !rule.field) {
        target.style.display = '';
        return;
      }
      var current = getValue(rule.field);
      target.style.display = evalRule(rule, current) ? '' : 'none';
    });
  }

  function bind() {
    document.addEventListener('change', function (e) {
      var name = e.target && e.target.name;
      if (!name) return;
      if (name.indexOf('f_') === 0 || name.indexOf('fields[') === 0) applyAll();
    });
    document.addEventListener('input', function (e) {
      var name = e.target && e.target.name;
      if (!name) return;
      if (name.indexOf('f_') === 0 || name.indexOf('fields[') === 0) applyAll();
    });
    applyAll();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', bind);
  } else {
    bind();
  }
})();
