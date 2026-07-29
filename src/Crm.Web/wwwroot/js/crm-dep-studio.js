/**
 * Inline multi-action dependency editor for Customize Studio (?tab=dependencies).
 * Supports nested "افزودن عمل" and batch save to /visibility/batch.
 */
(function () {
  var list = document.getElementById('crmDepList');
  if (!list) return;

  var metaEl = document.getElementById('crm-dep-meta');
  var editorTpl = document.getElementById('crm-dep-editor-tpl');
  var actionTpl = document.getElementById('crm-dep-action-tpl');
  if (!metaEl || !editorTpl || !actionTpl) return;

  var meta;
  try {
    meta = JSON.parse(metaEl.textContent || '{}');
  } catch (e) {
    meta = {};
  }

  var mode = meta.mode || list.getAttribute('data-mode') || 'field';
  var moduleName = meta.module || list.getAttribute('data-module') || '';
  var editingHost = null;

  function parseFullRule(json) {
    if (!json) return null;
    try {
      var r = JSON.parse(json);
      if (!r) return null;
      if (Array.isArray(r.conditions) && r.conditions.length) {
        return {
          action: r.action || 'show',
          logic: r.logic || 'and',
          conditions: r.conditions
        };
      }
      if (r.field) {
        return {
          action: 'show',
          logic: 'and',
          conditions: [{ field: r.field, op: r.op || 'eq', value: r.value || '' }]
        };
      }
      return null;
    } catch (e) {
      return null;
    }
  }

  function buildFullRule(conditions) {
    var cleaned = (conditions || []).filter(function (c) {
      return c && c.field;
    });
    if (!cleaned.length) return '';
    return JSON.stringify({
      action: 'show',
      logic: 'and',
      conditions: cleaned.map(function (c) {
        return { field: c.field, op: c.op || 'eq', value: c.value || '' };
      })
    });
  }

  function fillTargetSelect(sel, kind, selectedId, preferredBlockId) {
    sel.innerHTML = '';
    var opt0 = document.createElement('option');
    opt0.value = '';
    opt0.textContent = 'انتخاب';
    sel.appendChild(opt0);

    var items =
      kind === 'block'
        ? meta.blocks || []
        : (meta.fields || []).slice().sort(function (a, b) {
            if (preferredBlockId) {
              var aIn = String(a.blockId) === String(preferredBlockId) ? 0 : 1;
              var bIn = String(b.blockId) === String(preferredBlockId) ? 0 : 1;
              if (aIn !== bIn) return aIn - bIn;
            }
            return String(a.label).localeCompare(String(b.label), 'fa');
          });

    items.forEach(function (item) {
      var o = document.createElement('option');
      o.value = String(item.id);
      o.textContent = item.label;
      if (selectedId != null && String(selectedId) === String(item.id)) o.selected = true;
      sel.appendChild(o);
    });
    sel.setAttribute('data-target-kind', kind);
  }

  function fillControllingSelect(sel, selectedName, excludeNames) {
    sel.innerHTML = '';
    var opt0 = document.createElement('option');
    opt0.value = '';
    opt0.textContent = 'فیلد';
    sel.appendChild(opt0);
    var exclude = excludeNames || [];
    (meta.controlling || []).forEach(function (f) {
      if (exclude.indexOf(f.name) !== -1) return;
      var o = document.createElement('option');
      o.value = f.name;
      o.textContent = f.label;
      if (selectedName && selectedName === f.name) o.selected = true;
      sel.appendChild(o);
    });
  }

  function fillValueSelect(sel, fieldName, selectedValue) {
    sel.innerHTML = '';
    var opt0 = document.createElement('option');
    opt0.value = '';
    opt0.textContent = 'انتخاب';
    sel.appendChild(opt0);
    var opts = (meta.picklists && meta.picklists[fieldName]) || [];
    opts.forEach(function (p) {
      var o = document.createElement('option');
      o.value = p.value;
      o.textContent = p.label;
      if (selectedValue != null && String(selectedValue) === String(p.value)) o.selected = true;
      sel.appendChild(o);
    });
  }

  function wireCondRow(condEl) {
    var fieldSel = condEl.querySelector('[data-dep-cfield]');
    var valueSel = condEl.querySelector('[data-dep-cvalue]');
    fieldSel.addEventListener('change', function () {
      fillValueSelect(valueSel, fieldSel.value, '');
      refreshExcludes(editingHost);
    });
  }

  function syncCondRemoveButtons(actionEl) {
    var conds = actionEl.querySelectorAll('[data-dep-cond]');
    conds.forEach(function (c) {
      var btn = c.querySelector('[data-dep-cond-remove]');
      if (btn) btn.hidden = conds.length < 2;
    });
  }

  function syncActionRemoveButtons(form) {
    var actions = form.querySelectorAll('[data-dep-action]');
    actions.forEach(function (a, idx) {
      var btn = a.querySelector('[data-dep-action-remove]');
      if (btn) btn.hidden = actions.length < 2 || idx === 0;
      a.classList.toggle('is-nested', idx > 0);
    });
  }

  function collectExcludeNames(form) {
    var names = [];
    form.querySelectorAll('[data-dep-action]').forEach(function (actionEl) {
      var kind = actionEl.querySelector('[data-dep-target]').getAttribute('data-target-kind');
      if (kind !== 'field') return;
      var id = actionEl.querySelector('[data-dep-target]').value;
      var f = (meta.fields || []).find(function (x) {
        return String(x.id) === String(id);
      });
      if (f && f.name) names.push(f.name);
    });
    return names;
  }

  function refreshExcludes(host) {
    if (!host) return;
    var form = host.querySelector('[data-dep-form]');
    if (!form) return;
    var excludes = collectExcludeNames(form);
    form.querySelectorAll('[data-dep-action]').forEach(function (actionEl) {
      var targetSel = actionEl.querySelector('[data-dep-target]');
      var kind = targetSel.getAttribute('data-target-kind');
      var ownName = null;
      if (kind === 'field') {
        var f = (meta.fields || []).find(function (x) {
          return String(x.id) === String(targetSel.value);
        });
        ownName = f ? f.name : null;
      }
      var localExclude = excludes.filter(function (n) {
        return n !== ownName;
      });
      if (ownName) localExclude = localExclude.concat([ownName]);

      actionEl.querySelectorAll('[data-dep-cond]').forEach(function (condEl) {
        var fs = condEl.querySelector('[data-dep-cfield]');
        var vs = condEl.querySelector('[data-dep-cvalue]');
        var cur = fs.value;
        if (localExclude.indexOf(cur) !== -1) cur = '';
        fillControllingSelect(fs, cur, localExclude);
        fillValueSelect(vs, fs.value, vs.value);
      });
    });
  }

  function readActionConditions(actionEl) {
    var conds = [];
    actionEl.querySelectorAll('[data-dep-cond]').forEach(function (condEl) {
      conds.push({
        field: condEl.querySelector('[data-dep-cfield]').value,
        op: condEl.querySelector('[data-dep-cop]').value,
        value: condEl.querySelector('[data-dep-cvalue]').value
      });
    });
    return conds;
  }

  function applyConditionsToAction(actionEl, conditions, excludes) {
    var condsWrap = actionEl.querySelector('[data-dep-conds]');
    var firstCond = condsWrap.querySelector('[data-dep-cond]');
    // remove extra conds
    Array.prototype.slice.call(condsWrap.querySelectorAll('[data-dep-cond]')).forEach(function (c, i) {
      if (i > 0) c.remove();
    });
    var list = conditions && conditions.length ? conditions : [{ field: '', op: 'eq', value: '' }];
    list.forEach(function (c, idx) {
      var condEl;
      if (idx === 0) {
        condEl = firstCond;
      } else {
        condEl = firstCond.cloneNode(true);
        condsWrap.insertBefore(condEl, condsWrap.querySelector('[data-dep-add-cond]'));
      }
      fillControllingSelect(condEl.querySelector('[data-dep-cfield]'), c.field || '', excludes || []);
      condEl.querySelector('[data-dep-cop]').value = c.op || 'eq';
      fillValueSelect(condEl.querySelector('[data-dep-cvalue]'), c.field || '', c.value || '');
      wireCondRow(condEl);
    });
    syncCondRemoveButtons(actionEl);
  }

  function createActionEl(opts) {
    opts = opts || {};
    var node = actionTpl.content.cloneNode(true);
    var actionEl = node.querySelector('[data-dep-action]');
    var targetSel = actionEl.querySelector('[data-dep-target]');
    var kind = opts.kind || (mode === 'block' ? 'block' : 'field');
    // nested actions (index > 0) are always fields
    if (opts.forceField) kind = 'field';

    fillTargetSelect(targetSel, kind, opts.id || '', opts.preferredBlockId || null);
    if (opts.lockTarget) targetSel.disabled = true;

    var rule = parseFullRule(opts.rule || '');
    var conditions = (rule && rule.conditions) || [{ field: '', op: 'eq', value: '' }];
    applyConditionsToAction(actionEl, conditions, opts.excludes || []);

    actionEl.querySelector('[data-dep-add-cond]').addEventListener('click', function () {
      var condsWrap = actionEl.querySelector('[data-dep-conds]');
      var base = condsWrap.querySelector('[data-dep-cond]');
      var neu = base.cloneNode(true);
      fillControllingSelect(neu.querySelector('[data-dep-cfield]'), '', collectExcludeNames(editingHost.querySelector('[data-dep-form]')));
      neu.querySelector('[data-dep-cop]').value = 'eq';
      fillValueSelect(neu.querySelector('[data-dep-cvalue]'), '', '');
      wireCondRow(neu);
      condsWrap.insertBefore(neu, condsWrap.querySelector('[data-dep-add-cond]'));
      syncCondRemoveButtons(actionEl);
    });

    actionEl.querySelector('[data-dep-conds]').addEventListener('click', function (e) {
      var btn = e.target.closest('[data-dep-cond-remove]');
      if (!btn) return;
      var cond = btn.closest('[data-dep-cond]');
      if (!cond) return;
      if (actionEl.querySelectorAll('[data-dep-cond]').length < 2) return;
      cond.remove();
      syncCondRemoveButtons(actionEl);
    });

    targetSel.addEventListener('change', function () {
      refreshExcludes(editingHost);
    });

    actionEl.querySelector('[data-dep-action-remove]').addEventListener('click', function () {
      var form = actionEl.closest('[data-dep-form]');
      if (!form) return;
      if (form.querySelectorAll('[data-dep-action]').length < 2) return;
      actionEl.remove();
      syncActionRemoveButtons(form);
      refreshExcludes(editingHost);
    });

    return actionEl;
  }

  function closeEditor() {
    if (!editingHost) return;
    var isNew = editingHost.getAttribute('data-dep-new') === '1';
    var form = editingHost.querySelector('[data-dep-form]');
    if (form) form.remove();

    if (isNew) {
      editingHost.remove();
    } else {
      editingHost.classList.remove('is-editing');
      editingHost.querySelectorAll('[data-dep-summary]').forEach(function (s) {
        s.hidden = false;
      });
      editingHost.querySelectorAll('[data-dep-nest]').forEach(function (n) {
        n.hidden = false;
      });
    }
    editingHost = null;
  }

  function gatherInitialActions(rowOrGroup) {
    var actions = [];
    var group = rowOrGroup.closest('[data-dep-group]') || rowOrGroup;

    if (group.hasAttribute('data-dep-group') && group.getAttribute('data-kind') === 'block') {
      actions.push({
        kind: 'block',
        id: group.getAttribute('data-id'),
        rule: group.getAttribute('data-rule') || '',
        lockTarget: true
      });
      group.querySelectorAll(':scope > .crm-dep-nest [data-dep-row]').forEach(function (child) {
        actions.push({
          kind: 'field',
          id: child.getAttribute('data-id'),
          rule: child.getAttribute('data-rule') || '',
          lockTarget: true,
          forceField: true,
          preferredBlockId: group.getAttribute('data-id')
        });
      });
      return actions;
    }

    // single row (field or standalone)
    actions.push({
      kind: rowOrGroup.getAttribute('data-kind') || (mode === 'block' ? 'block' : 'field'),
      id: rowOrGroup.getAttribute('data-id'),
      name: rowOrGroup.getAttribute('data-name'),
      rule: rowOrGroup.getAttribute('data-rule') || '',
      lockTarget: !!rowOrGroup.getAttribute('data-id'),
      preferredBlockId: rowOrGroup.getAttribute('data-block-id') || null,
      forceField: (rowOrGroup.getAttribute('data-kind') || '') === 'field'
    });
    return actions;
  }

  function openEditor(host, isNew, seedActions) {
    closeEditor();
    editingHost = host;
    host.classList.add('is-editing');

    host.querySelectorAll('[data-dep-summary]').forEach(function (s) {
      s.hidden = true;
    });
    host.querySelectorAll('[data-dep-nest]').forEach(function (n) {
      n.hidden = true;
    });

    var formNode = editorTpl.content.cloneNode(true);
    var form = formNode.querySelector('[data-dep-form]');
    var actionsWrap = form.querySelector('[data-dep-actions]');
    var preferredBlockId = null;

    var initial = seedActions || gatherInitialActions(host);
    if (!initial.length) {
      initial = [
        {
          kind: mode === 'block' ? 'block' : 'field',
          forceField: mode === 'field'
        }
      ];
    }

    initial.forEach(function (a, idx) {
      if (a.kind === 'block' && a.id) preferredBlockId = a.id;
      var el = createActionEl({
        kind: a.kind,
        id: a.id,
        rule: a.rule,
        lockTarget: a.lockTarget,
        forceField: idx > 0 || a.forceField,
        preferredBlockId: a.preferredBlockId || preferredBlockId,
        excludes: []
      });
      actionsWrap.appendChild(el);
    });

    syncActionRemoveButtons(form);
    refreshExcludes(host);

    form.querySelector('[data-dep-add-action]').addEventListener('click', function () {
      var primary = form.querySelector('[data-dep-action] [data-dep-target]');
      var blockId = null;
      if (primary && primary.getAttribute('data-target-kind') === 'block') {
        blockId = primary.value || preferredBlockId;
      }
      var el = createActionEl({
        forceField: true,
        preferredBlockId: blockId
      });
      actionsWrap.appendChild(el);
      syncActionRemoveButtons(form);
      refreshExcludes(host);
    });

    form.querySelector('[data-dep-cancel]').addEventListener('click', function () {
      closeEditor();
    });

    form.addEventListener('submit', function (e) {
      var payload = [];
      var actionEls = form.querySelectorAll('[data-dep-action]');
      for (var i = 0; i < actionEls.length; i++) {
        var actionEl = actionEls[i];
        var targetSel = actionEl.querySelector('[data-dep-target]');
        var tid = targetSel.value;
        var kind = targetSel.getAttribute('data-target-kind') || 'field';
        if (!tid) {
          e.preventDefault();
          alert('هدف عمل شماره ' + (i + 1) + ' را انتخاب کنید.');
          return;
        }
        var conds = readActionConditions(actionEl);
        for (var j = 0; j < conds.length; j++) {
          if (!conds[j].field) {
            e.preventDefault();
            alert('فیلد شرط را در عمل شماره ' + (i + 1) + ' انتخاب کنید.');
            return;
          }
          if (!conds[j].value) {
            e.preventDefault();
            alert('مقدار شرط را در عمل شماره ' + (i + 1) + ' انتخاب کنید.');
            return;
          }
        }
        var rule = buildFullRule(conds);
        if (!rule) {
          e.preventDefault();
          alert('شرط عمل شماره ' + (i + 1) + ' نامعتبر است.');
          return;
        }
        payload.push({ kind: kind, id: parseInt(tid, 10), rule: rule });
      }
      form.querySelector('[data-dep-actions-json]').value = JSON.stringify(payload);
    });

    // Place form: for group, append after summaries area; for row, append inside
    if (host.hasAttribute('data-dep-group')) {
      host.appendChild(form);
    } else {
      host.appendChild(form);
    }
    if (isNew) host.setAttribute('data-dep-new', '1');
  }

  function startAdd() {
    var empty = list.querySelector('[data-dep-empty]');
    if (empty) empty.remove();

    var host;
    if (mode === 'block') {
      host = document.createElement('div');
      host.className = 'crm-dep-group is-editing';
      host.setAttribute('data-dep-group', '');
      host.setAttribute('data-kind', 'block');
      host.setAttribute('data-rule', '');
    } else {
      host = document.createElement('div');
      host.className = 'crm-dep-row is-editing';
      host.setAttribute('data-dep-row', '');
      host.setAttribute('data-kind', 'field');
      host.setAttribute('data-rule', '');
    }
    list.appendChild(host);
    openEditor(host, true, [
      {
        kind: mode === 'block' ? 'block' : 'field',
        forceField: mode === 'field'
      }
    ]);
  }

  var addBtn = document.getElementById('crmDepAddBtn');
  var addLink = document.getElementById('crmDepAddLink');
  if (addBtn) addBtn.addEventListener('click', startAdd);
  if (addLink) addLink.addEventListener('click', startAdd);

  list.addEventListener('click', function (e) {
    var editBtn = e.target.closest('[data-dep-edit]');
    if (!editBtn) return;
    var row = editBtn.closest('[data-dep-row]');
    if (!row) return;
    var group = row.closest('[data-dep-group]');
    // editing a child field under a block group → open whole group
    if (group && row.getAttribute('data-kind') === 'field' && row.classList.contains('crm-dep-row-child')) {
      openEditor(group, false);
      return;
    }
    if (group && row.getAttribute('data-kind') === 'block') {
      openEditor(group, false);
      return;
    }
    openEditor(row, false);
  });
})();
