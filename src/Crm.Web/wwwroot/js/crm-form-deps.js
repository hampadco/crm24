/**
 * Form field/block visibility dependencies.
 * Supports legacy {"field","op","value"} and
 * {"action":"show","logic":"and","conditions":[{field,op,value},...]}.
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
    if (el.multiple) {
      return Array.prototype.slice
        .call(el.selectedOptions || [])
        .map(function (o) {
          return o.value;
        })
        .join(',');
    }
    return (el.value || '').toString();
  }

  function evalCondition(current, op, expected) {
    expected = (expected || '').toString();
    current = (current || '').toString();
    op = (op || 'eq').toLowerCase();
    if (op === 'eq') return current === expected;
    if (op === 'neq') return current !== expected;
    if (op === 'contains') return current.indexOf(expected) !== -1;
    return true;
  }

  function normalizeRule(raw) {
    if (!raw) return null;
    var rule;
    try {
      rule = JSON.parse(raw);
    } catch (e) {
      return null;
    }
    if (!rule) return null;
    if (Array.isArray(rule.conditions) && rule.conditions.length) {
      return {
        logic: (rule.logic || 'and').toLowerCase(),
        conditions: rule.conditions
      };
    }
    if (rule.field) {
      return {
        logic: 'and',
        conditions: [{ field: rule.field, op: rule.op || 'eq', value: rule.value || '' }]
      };
    }
    return null;
  }

  function evalRule(normalized) {
    if (!normalized || !normalized.conditions || !normalized.conditions.length) return true;
    var logic = normalized.logic === 'or' ? 'or' : 'and';
    var results = normalized.conditions.map(function (c) {
      if (!c || !c.field) return true;
      return evalCondition(getValue(c.field), c.op, c.value);
    });
    if (logic === 'or') return results.some(Boolean);
    return results.every(Boolean);
  }

  function applyAll() {
    document.querySelectorAll('.crm-dep-target[data-visibility-rule]').forEach(function (target) {
      var raw = target.getAttribute('data-visibility-rule');
      if (!raw) {
        target.style.display = '';
        return;
      }
      var normalized = normalizeRule(raw);
      if (!normalized) {
        target.style.display = '';
        return;
      }
      target.style.display = evalRule(normalized) ? '' : 'none';
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
