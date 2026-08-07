(function () {
  function num(el) {
    if (!el) return 0;
    var v = parseFloat(String(el.value || '').replace(/,/g, ''));
    return isNaN(v) ? 0 : v;
  }

  function fmt(n) {
    return (Math.round(n * 100) / 100).toLocaleString('en-US');
  }

  function recalcRow(row) {
    var qty = num(row.querySelector('[data-line-qty]'));
    var price = num(row.querySelector('[data-line-price]'));
    var disc = num(row.querySelector('[data-line-disc]'));
    var tax = num(row.querySelector('[data-line-tax]'));
    var net = qty * price * (1 - disc / 100);
    var lineTax = net * (tax / 100);
    var total = net + lineTax;
    var out = row.querySelector('[data-line-total]');
    if (out) out.value = fmt(total);
    return { net: net, tax: lineTax };
  }

  function recalcAll(root) {
    var sub = 0, tax = 0;
    root.querySelectorAll('[data-line-row]').forEach(function (row) {
      var r = recalcRow(row);
      sub += r.net;
      tax += r.tax;
    });
    var elSub = root.querySelector('[data-sum-sub]');
    var elTax = root.querySelector('[data-sum-tax]');
    var elGrand = root.querySelector('[data-sum-grand]');
    if (elSub) elSub.textContent = fmt(sub);
    if (elTax) elTax.textContent = fmt(tax);
    if (elGrand) elGrand.textContent = fmt(sub + tax);

    var discInput = document.querySelector('input[name="f_discountPercent"]');
    var disc = discInput ? num(discInput) : 0;
    var discountAmount = sub * (disc / 100);
    var grand = sub - discountAmount + tax;
    var gt = document.querySelector('input[name="f_grandTotal"]');
    var st = document.querySelector('input[name="f_subTotal"]');
    var tt = document.querySelector('input[name="f_taxTotal"]');
    var da = document.querySelector('input[name="f_discountAmount"]');
    var am = document.querySelector('input[name="f_amount"]');
    if (st) st.value = fmt(sub).replace(/,/g, '');
    if (tt) tt.value = fmt(tax).replace(/,/g, '');
    if (da) da.value = fmt(discountAmount).replace(/,/g, '');
    if (gt) gt.value = fmt(grand).replace(/,/g, '');
    if (am) am.value = fmt(grand).replace(/,/g, '');
  }

  function reindex(root) {
    root.querySelectorAll('[data-line-row]').forEach(function (row, i) {
      row.querySelectorAll('input,select').forEach(function (input) {
        if (!input.name) return;
        input.name = input.name.replace(/li\[\d+\]/, 'li[' + i + ']');
      });
    });
  }

  function bindRow(row, root) {
    row.addEventListener('input', function () { recalcAll(root); });
    row.addEventListener('change', function (e) {
      var sel = e.target.closest('[data-line-product]');
      if (sel) {
        var opt = sel.options[sel.selectedIndex];
        if (opt) {
          var price = opt.getAttribute('data-price');
          var tax = opt.getAttribute('data-tax');
          var priceEl = row.querySelector('[data-line-price]');
          var taxEl = row.querySelector('[data-line-tax]');
          var titleEl = row.querySelector('[data-line-title]');
          if (priceEl && price) priceEl.value = price;
          if (taxEl && tax) taxEl.value = tax;
          if (titleEl && !titleEl.value) titleEl.value = opt.textContent.trim();
        }
      }
      recalcAll(root);
    });
    var removeBtn = row.querySelector('[data-remove-line]');
    if (removeBtn) {
      removeBtn.addEventListener('click', function () {
        row.remove();
        reindex(root);
        recalcAll(root);
      });
    }
  }

  function init(root) {
    root.querySelectorAll('[data-line-row]').forEach(function (row) { bindRow(row, root); });
    var addBtn = root.querySelector('[data-add-line]');
    var tpl = document.getElementById('line-row-template');
    var body = root.querySelector('[data-lines-body]');
    if (addBtn && tpl && body) {
      addBtn.addEventListener('click', function () {
        var idx = body.querySelectorAll('[data-line-row]').length;
        var html = tpl.innerHTML.replace(/__IDX__/g, String(idx));
        var wrap = document.createElement('tbody');
        wrap.innerHTML = html.trim();
        var row = wrap.firstElementChild;
        body.appendChild(row);
        bindRow(row, root);
        reindex(root);
        recalcAll(root);
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
  });
})();
