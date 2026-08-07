using System.Globalization;
using System.Text.RegularExpressions;
using Crm.Core.Entities;

namespace Crm.Infrastructure.Services;

/// <summary>
/// ارزیاب سادهٔ فرمول‌های عددی روی فیلدهای همان رکورد.
/// پشتیبانی: + - * / و پرانتز و ارجاع فیلد با نام (مثلاً unitPrice * quantity).
/// </summary>
public static class FormulaEvaluator
{
    private static readonly Regex TokenRegex = new(
        @"\s*([A-Za-z_][A-Za-z0-9_]*|\d+(?:\.\d+)?|[+\-*/()])\s*",
        RegexOptions.Compiled);

    public static void ApplyFormulas(IReadOnlyList<FieldDef> fields, Dictionary<string, string?> data)
    {
        var formulaFields = fields
            .Where(f => !string.IsNullOrWhiteSpace(f.FormulaExpression))
            .ToList();
        if (formulaFields.Count == 0)
            return;

        // مرتب‌سازی تقریبی: فیلدهایی که به بقیه ارجاع نمی‌دهند اول
        var pending = formulaFields.ToList();
        var guard = 0;
        while (pending.Count > 0 && guard++ < 20)
        {
            var progressed = false;
            foreach (var field in pending.ToList())
            {
                try
                {
                    var result = Evaluate(field.FormulaExpression!, data);
                    data[field.Name] = result.ToString("0.##", CultureInfo.InvariantCulture);
                    pending.Remove(field);
                    progressed = true;
                }
                catch
                {
                    // وابستگی هنوز محاسبه نشده — دور بعد
                }
            }
            if (!progressed)
                break;
        }
    }

    public static decimal Evaluate(string expression, Dictionary<string, string?> data)
    {
        var tokens = Tokenize(expression);
        var values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, raw) in data)
        {
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
                || decimal.TryParse(raw, out v))
                values[key] = v;
        }

        var rpn = ToRpn(tokens, values);
        return EvalRpn(rpn);
    }

    private static List<string> Tokenize(string expression)
    {
        var list = new List<string>();
        var pos = 0;
        while (pos < expression.Length)
        {
            var m = TokenRegex.Match(expression, pos);
            if (!m.Success || m.Index != pos)
                throw new InvalidOperationException("عبارت فرمول نامعتبر است.");
            list.Add(m.Groups[1].Value);
            pos = m.Index + m.Length;
        }
        return list;
    }

    private static List<object> ToRpn(List<string> tokens, Dictionary<string, decimal> values)
    {
        var output = new List<object>();
        var ops = new Stack<string>();
        int Prec(string op) => op is "*" or "/" ? 2 : 1;

        foreach (var t in tokens)
        {
            if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var lit))
            {
                output.Add(lit);
            }
            else if (char.IsLetter(t[0]) || t[0] == '_')
            {
                if (!values.TryGetValue(t, out var v))
                    throw new InvalidOperationException($"فیلد «{t}» مقدار عددی ندارد.");
                output.Add(v);
            }
            else if (t is "+" or "-" or "*" or "/")
            {
                while (ops.Count > 0 && ops.Peek() is "+" or "-" or "*" or "/" && Prec(ops.Peek()) >= Prec(t))
                    output.Add(ops.Pop());
                ops.Push(t);
            }
            else if (t == "(")
            {
                ops.Push(t);
            }
            else if (t == ")")
            {
                while (ops.Count > 0 && ops.Peek() != "(")
                    output.Add(ops.Pop());
                if (ops.Count == 0 || ops.Pop() != "(")
                    throw new InvalidOperationException("پرانتز نامتوازن.");
            }
            else
            {
                throw new InvalidOperationException($"توکن ناشناخته: {t}");
            }
        }

        while (ops.Count > 0)
        {
            var op = ops.Pop();
            if (op is "(" or ")")
                throw new InvalidOperationException("پرانتز نامتوازن.");
            output.Add(op);
        }

        return output;
    }

    private static decimal EvalRpn(List<object> rpn)
    {
        var stack = new Stack<decimal>();
        foreach (var item in rpn)
        {
            if (item is decimal d)
            {
                stack.Push(d);
                continue;
            }

            if (stack.Count < 2)
                throw new InvalidOperationException("فرمول ناقص است.");
            var b = stack.Pop();
            var a = stack.Pop();
            stack.Push((string)item switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => b == 0 ? 0 : a / b,
                _ => throw new InvalidOperationException("عملگر نامعتبر.")
            });
        }

        if (stack.Count != 1)
            throw new InvalidOperationException("فرمول ناقص است.");
        return stack.Pop();
    }
}
