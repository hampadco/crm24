(function () {
  function num(el) {
    if (!el) return 0;
    var v = parseFloat(String(el.value || '').replace(/,/g, ''));
    return isNaN(v) ? 0 : v;
  }

  /** قیمت: بدون اعشار، با جداکننده هزارگان — مثل 1,000 */
  function fmtInt(n) {
    return Math.round(Number(n) || 0).toLocaleString('en-US', {
      maximumFractionDigits: 0,
      minimumFractionDigits: 0
    });
  }

  function digitsOnly(s) {
    return String(s || '').replace(/[^\d]/g, '');
  }

  function formatMoneyIntInput(el) {
    if (!el) return;
    var raw = digitsOnly(el.value);
    el.value = raw ? fmtInt(raw) : '';
  }

  /** فرمت زنده هنگام تایپ + حفظ موقعیت کرسر */
  function formatMoneyIntLive(el) {
    if (!el) return;
    var raw = digitsOnly(el.value);
    if (!raw) {
      el.value = '';
      return;
    }
    var pos = el.selectionStart || 0;
    var digitsBefore = digitsOnly(el.value.slice(0, pos)).length;
    var formatted = fmtInt(raw);
    el.value = formatted;
    var seen = 0;
    var newPos = formatted.length;
    for (var i = 0; i < formatted.length; i++) {
      if (/\d/.test(formatted.charAt(i))) {
        seen++;
        if (seen >= digitsBefore) {
          newPos = i + 1;
          break;
        }
      }
    }
    try { el.setSelectionRange(newPos, newPos); } catch (e) { /* ignore */ }
  }

  function bindMoneyInt(el) {
    if (!el || el.dataset.moneyBoundBound) return;
    el.dataset.moneyBoundBound = '1';
    el.addEventListener('input', function () {
      formatMoneyIntLive(el);
      var root = el.closest('[data-line-items]');
      if (root) recalcAll(root);
    });
    el.addEventListener('blur', function () {
      formatMoneyIntInput(el);
      var root = el.closest('[data-line-items]');
      if (root) recalcAll(root);
    });
  }

  function selectedPriceBookId() {
    var el = document.querySelector('[data-price-book], select[name="f_priceBook"], input[name="f_priceBook"]');
    if (!el) return 0;
    var v = parseInt(el.value || '0', 10);
    return isNaN(v) ? 0 : v;
  }

  function initRowSelect2(row) {
    if (typeof window.initCrmSelect2 === 'function') {
      window.initCrmSelect2(row);
    }
  }

  function destroyRowSelect2(row) {
    if (typeof jQuery === 'undefined' || !jQuery.fn.select2) return;
    jQuery(row).find('select.crm-select2').each(function () {
      var $el = jQuery(this);
      if ($el.hasClass('select2-hidden-accessible')) {
        $el.select2('destroy');
      }
    });
  }

  function applyProductDefaults(row, opt) {
    var baseEl = row.querySelector('[data-base-price]');
    if (!opt || !opt.value) {
      if (baseEl) baseEl.value = '';
      return Promise.resolve();
    }

    var priceEl = row.querySelector('[data-line-price]');
    var taxEl = row.querySelector('[data-line-tax]');
    var titleEl = row.querySelector('[data-line-title]');
    var totalEl = row.querySelector('[data-line-total]');
    var productId = parseInt(opt.value || '0', 10);
    var fallbackPrice = opt.getAttribute('data-price');
    var fallbackTax = opt.getAttribute('data-tax');
    var isPriceBook = !!row.closest('[data-pricebook-lines]');

    // دادهٔ غنی از Select2 Ajax (price/tax)
    var sel = row.querySelector('[data-line-product]');
    if (window.jQuery && sel) {
      var s2 = window.jQuery(sel).select2('data');
      if (s2 && s2[0]) {
        if (s2[0].price != null && (fallbackPrice == null || fallbackPrice === ''))
          fallbackPrice = String(s2[0].price);
        if (s2[0].tax != null && (fallbackTax == null || fallbackTax === ''))
          fallbackTax = String(s2[0].tax);
        if (opt && s2[0].price != null) opt.setAttribute('data-price', String(s2[0].price));
        if (opt && s2[0].tax != null) opt.setAttribute('data-tax', String(s2[0].tax));
      }
    }

    if (baseEl) {
      baseEl.value = fallbackPrice != null && fallbackPrice !== '' ? fmtInt(fallbackPrice) : '';
    }

    if (titleEl && (!titleEl.value || titleEl.dataset.autoTitle === '1')) {
      titleEl.value = (opt.textContent || '').trim();
      titleEl.dataset.autoTitle = '1';
    }
    if (!isPriceBook && taxEl && fallbackTax) taxEl.value = fallbackTax;

    function setPrice(raw) {
      if (!priceEl || raw == null || raw === '') return;
      priceEl.value = fmtInt(raw);
      if (isPriceBook && totalEl) totalEl.value = String(Math.round(num(priceEl)));
    }

    var bookId = selectedPriceBookId();
    if (!isPriceBook && bookId > 0 && productId > 0) {
      return fetch('/App/m/pricebooks/price?priceBookId=' + bookId + '&productId=' + productId, {
        headers: { 'Accept': 'application/json' }
      })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (data) {
          if (data && data.price != null) setPrice(data.price);
          else if (fallbackPrice) setPrice(fallbackPrice);
          if (data && taxEl && data.tax != null) taxEl.value = data.tax;
        })
        .catch(function () {
          if (fallbackPrice) setPrice(fallbackPrice);
        });
    }

    // در دفترچه قیمت، قیمت پایه فقط نمایش است؛ قیمت دفترچه را فقط اگر خالی/صفر بود پر کن
    if (isPriceBook) {
      if (fallbackPrice && (!priceEl || !num(priceEl))) setPrice(fallbackPrice);
      return Promise.resolve();
    }

    if (fallbackPrice) setPrice(fallbackPrice);
    return Promise.resolve();
  }

  function recalcRow(row) {
    var isPriceBook = !!row.closest('[data-pricebook-lines]');
    var priceEl = row.querySelector('[data-line-price]');
    if (priceEl && priceEl !== document.activeElement) {
      formatMoneyIntInput(priceEl);
    }

    if (isPriceBook) {
      var priceOnly = num(priceEl);
      var out = row.querySelector('[data-line-total]');
      if (out) out.value = String(Math.round(priceOnly));
      return { net: priceOnly, tax: 0 };
    }

    var qty = num(row.querySelector('[data-line-qty]'));
    var price = num(priceEl);
    var disc = num(row.querySelector('[data-line-disc]'));
    var tax = num(row.querySelector('[data-line-tax]'));
    var net = qty * price * (1 - disc / 100);
    var lineTax = net * (tax / 100);
    var total = net + lineTax;
    var out2 = row.querySelector('[data-line-total]');
    if (out2) out2.value = fmtInt(total);
    return { net: net, tax: lineTax };
  }

  function recalcAll(root) {
    if (root.hasAttribute('data-pricebook-lines')) {
      root.querySelectorAll('[data-line-row]').forEach(recalcRow);
      return;
    }
    var sub = 0, tax = 0;
    root.querySelectorAll('[data-line-row]').forEach(function (row) {
      var r = recalcRow(row);
      sub += r.net;
      tax += r.tax;
    });
    var elSub = root.querySelector('[data-sum-sub]');
    var elTax = root.querySelector('[data-sum-tax]');
    var elGrand = root.querySelector('[data-sum-grand]');
    if (elSub) elSub.textContent = fmtInt(sub);
    if (elTax) elTax.textContent = fmtInt(tax);
    if (elGrand) elGrand.textContent = fmtInt(sub + tax);

    var discInput = document.querySelector('input[name="f_discountPercent"]');
    var disc = discInput ? num(discInput) : 0;
    var discountAmount = sub * (disc / 100);
    var grand = sub - discountAmount + tax;
    var gt = document.querySelector('input[name="f_grandTotal"]');
    var st = document.querySelector('input[name="f_subTotal"]');
    var tt = document.querySelector('input[name="f_taxTotal"]');
    var da = document.querySelector('input[name="f_discountAmount"]');
    var am = document.querySelector('input[name="f_amount"]');
    if (st) st.value = String(Math.round(sub));
    if (tt) tt.value = String(Math.round(tax));
    if (da) da.value = String(Math.round(discountAmount));
    if (gt) gt.value = String(Math.round(grand));
    if (am) am.value = String(Math.round(grand));
  }

  function reindex(root) {
    root.querySelectorAll('[data-line-row]').forEach(function (row, i) {
      row.querySelectorAll('input,select').forEach(function (input) {
        if (!input.name) return;
        input.name = input.name.replace(/li\[\d+\]/, 'li[' + i + ']');
      });
    });
  }

  function onProductChange(row, root, sel) {
    var opt = sel.options[sel.selectedIndex];
    applyProductDefaults(row, opt).then(function () { recalcAll(root); });
  }

  function bindRow(row, root) {
    row.querySelectorAll('[data-money-int]').forEach(bindMoneyInt);
    initRowSelect2(row);

    var productSel = row.querySelector('[data-line-product]');
    if (productSel && window.jQuery) {
      window.jQuery(productSel).off('change.crmLine').on('change.crmLine', function () {
        onProductChange(row, root, productSel);
      });
    }

    row.addEventListener('input', function (e) {
      if (e.target && e.target.matches && e.target.matches('[data-money-int]')) return;
      recalcAll(root);
    });
    row.addEventListener('change', function (e) {
      if (e.target && e.target.matches && e.target.matches('[data-line-product]')) {
        // handled via jQuery/select2 above; keep native fallback
        if (!window.jQuery) onProductChange(row, root, e.target);
        return;
      }
      if (e.target && e.target.matches && e.target.matches('[data-money-int]')) {
        formatMoneyIntInput(e.target);
      }
      recalcAll(root);
    });

    var titleEl = row.querySelector('[data-line-title]');
    if (titleEl) {
      titleEl.addEventListener('input', function () {
        titleEl.dataset.autoTitle = titleEl.value ? '0' : '1';
      });
    }

    var removeBtn = row.querySelector('[data-remove-line]');
    if (removeBtn) {
      removeBtn.addEventListener('click', function () {
        destroyRowSelect2(row);
        row.remove();
        reindex(root);
        recalcAll(root);
      });
    }
  }

  function refreshAllProductPrices(root) {
    var tasks = [];
    root.querySelectorAll('[data-line-row]').forEach(function (row) {
      var sel = row.querySelector('[data-line-product]');
      if (!sel || !sel.value) return;
      var opt = sel.options[sel.selectedIndex];
      tasks.push(applyProductDefaults(row, opt));
    });
    Promise.all(tasks).then(function () { recalcAll(root); });
  }

  function stripMoneyCommas(root) {
    root.querySelectorAll('[data-money-int]').forEach(function (el) {
      el.value = String(el.value || '').replace(/,/g, '');
    });
  }

  function init(root) {
    root.querySelectorAll('[data-line-row]').forEach(function (row) { bindRow(row, root); });
    var tpl = document.getElementById('line-row-template');
    var body = root.querySelector('[data-lines-body]');
    function addLine() {
      if (!tpl || !body) return;
      var idx = body.querySelectorAll('[data-line-row]').length;
      var html = tpl.innerHTML.replace(/__IDX__/g, String(idx));
      var wrap = document.createElement('tbody');
      wrap.innerHTML = html.trim();
      var row = wrap.firstElementChild;
      body.appendChild(row);
      bindRow(row, root);
      reindex(root);
      recalcAll(root);
    }
    root.querySelectorAll('[data-add-line]').forEach(function (btn) {
      btn.addEventListener('click', addLine);
    });

    root.querySelectorAll('[data-money-int]').forEach(formatMoneyIntInput);

    var form = root.closest('form');
    if (form && !form.dataset.moneyStripBound) {
      form.dataset.moneyStripBound = '1';
      form.addEventListener('submit', function () {
        document.querySelectorAll('[data-line-items]').forEach(stripMoneyCommas);
      });
    }

    recalcAll(root);
  }

  document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('[data-line-items]').forEach(init);
    var disc = document.querySelector('input[name="f_discountPercent"]');
    if (disc) {
      disc.addEventListener('input', function () {
        document.querySelectorAll('[data-line-items]').forEach(recalcAll);
      });
    }
    var book = document.querySelector('[data-price-book], select[name="f_priceBook"]');
    if (book) {
      var handler = function () {
        document.querySelectorAll('[data-line-items]').forEach(refreshAllProductPrices);
      };
      book.addEventListener('change', handler);
      if (window.jQuery) {
        window.jQuery(book).on('change.select2', handler);
      }
    }
  });
})();
