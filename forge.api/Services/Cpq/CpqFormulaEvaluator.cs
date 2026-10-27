using System.Globalization;

namespace Forge.Api.Services.Cpq;

/// <summary>
/// Evaluates a configurator pricing formula: a small arithmetic language over decimal numbers, parsed by hand
/// so no text is ever compiled or executed. Supported: numbers; variables (a letter or underscore followed by
/// letters, digits or underscores, or any name in square brackets such as <c>[Overall length]</c>), matched
/// case-insensitively; <c>+ - * / %</c>; comparisons <c>== != &lt; &lt;= &gt; &gt;=</c> and <c>&amp;&amp; ||</c>, which
/// give 1 or 0; parentheses; and the functions <c>min</c>, <c>max</c>, <c>abs</c>, <c>round(x)</c> or
/// <c>round(x, digits)</c>, <c>ceil</c>, <c>floor</c> and <c>if(condition, then, else)</c>. An unknown variable,
/// a division by zero or a syntax error throws <see cref="CpqFormulaException"/>.
/// </summary>
public static class CpqFormulaEvaluator
{
    public const int MaxLength = 2000;
    private const int MaxDepth = 64;

    public static decimal Evaluate(string expression, IReadOnlyDictionary<string, decimal> variables)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return 0m;
        if (expression.Length > MaxLength)
            throw new CpqFormulaException($"The pricing formula is longer than {MaxLength} characters.");

        var lookup = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in variables)
            lookup[name] = value;

        var parser = new Parser(expression, lookup);
        return parser.ParseAll();
    }

    /// <summary>The identifier a configurator option is reachable by without brackets: every character that
    /// is not a letter or digit becomes an underscore.</summary>
    public static string IdentifierFor(string name)
    {
        var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var id = new string(chars);
        return id.Length > 0 && char.IsDigit(id[0]) ? "_" + id : id;
    }

    private sealed class Parser(string text, Dictionary<string, decimal> variables)
    {
        private int _pos;
        private int _depth;

        public decimal ParseAll()
        {
            var value = ParseOr();
            SkipSpace();
            if (_pos < text.Length)
                throw Error($"Unexpected '{text[_pos]}' at position {_pos + 1}.");
            return value;
        }

        private decimal ParseOr()
        {
            var left = ParseAnd();
            while (Match("||"))
            {
                var right = ParseAnd();
                left = left != 0m || right != 0m ? 1m : 0m;
            }
            return left;
        }

        private decimal ParseAnd()
        {
            var left = ParseComparison();
            while (Match("&&"))
            {
                var right = ParseComparison();
                left = left != 0m && right != 0m ? 1m : 0m;
            }
            return left;
        }

        private decimal ParseComparison()
        {
            var left = ParseAdditive();
            while (true)
            {
                if (Match("==")) left = left == ParseAdditive() ? 1m : 0m;
                else if (Match("!=")) left = left != ParseAdditive() ? 1m : 0m;
                else if (Match("<=")) left = left <= ParseAdditive() ? 1m : 0m;
                else if (Match(">=")) left = left >= ParseAdditive() ? 1m : 0m;
                else if (Match("<")) left = left < ParseAdditive() ? 1m : 0m;
                else if (Match(">")) left = left > ParseAdditive() ? 1m : 0m;
                else return left;
            }
        }

        private decimal ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                if (Match("+")) left += ParseMultiplicative();
                else if (Match("-")) left -= ParseMultiplicative();
                else return left;
            }
        }

        private decimal ParseMultiplicative()
        {
            var left = ParseUnary();
            while (true)
            {
                if (Match("*"))
                    left *= ParseUnary();
                else if (Match("/"))
                {
                    var divisor = ParseUnary();
                    if (divisor == 0m) throw Error("The pricing formula divides by zero.");
                    left /= divisor;
                }
                else if (Match("%"))
                {
                    var divisor = ParseUnary();
                    if (divisor == 0m) throw Error("The pricing formula divides by zero.");
                    left %= divisor;
                }
                else return left;
            }
        }

        private decimal ParseUnary()
        {
            if (Match("-")) return -ParseUnary();
            if (Match("+")) return ParseUnary();
            if (Match("!")) return ParseUnary() == 0m ? 1m : 0m;
            return ParsePrimary();
        }

        private decimal ParsePrimary()
        {
            if (++_depth > MaxDepth)
                throw Error("The pricing formula is nested too deeply.");
            try
            {
                SkipSpace();
                if (_pos >= text.Length)
                    throw Error("The pricing formula ends too early.");

                var c = text[_pos];
                if (c == '(')
                {
                    _pos++;
                    var inner = ParseOr();
                    Expect(')');
                    return inner;
                }
                if (char.IsDigit(c) || c == '.')
                    return ParseNumber();
                if (c == '[')
                {
                    var end = text.IndexOf(']', _pos + 1);
                    if (end < 0) throw Error("A bracketed name is missing its closing ']'.");
                    var name = text[(_pos + 1)..end].Trim();
                    _pos = end + 1;
                    return Variable(name);
                }
                if (char.IsLetter(c) || c == '_')
                {
                    var start = _pos;
                    while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] == '_'))
                        _pos++;
                    var name = text[start.._pos];
                    SkipSpace();
                    if (_pos < text.Length && text[_pos] == '(')
                    {
                        _pos++;
                        return Function(name);
                    }
                    return Variable(name);
                }
                throw Error($"Unexpected '{c}' at position {_pos + 1}.");
            }
            finally
            {
                _depth--;
            }
        }

        private decimal Function(string name)
        {
            var args = new List<decimal>();
            SkipSpace();
            if (!Match(")"))
            {
                do args.Add(ParseOr());
                while (Match(","));
                Expect(')');
            }

            return name.ToLowerInvariant() switch
            {
                "min" when args.Count > 0 => args.Min(),
                "max" when args.Count > 0 => args.Max(),
                "abs" when args.Count == 1 => Math.Abs(args[0]),
                "ceil" when args.Count == 1 => Math.Ceiling(args[0]),
                "floor" when args.Count == 1 => Math.Floor(args[0]),
                "round" when args.Count == 1 => Math.Round(args[0], MidpointRounding.AwayFromZero),
                "round" when args.Count == 2 && args[1] is >= 0m and <= 10m && args[1] == Math.Floor(args[1])
                    => Math.Round(args[0], (int)args[1], MidpointRounding.AwayFromZero),
                "if" when args.Count == 3 => args[0] != 0m ? args[1] : args[2],
                _ => throw Error($"'{name}' with {args.Count} argument(s) is not a pricing formula function."),
            };
        }

        private decimal Variable(string name)
        {
            if (variables.TryGetValue(name, out var value))
                return value;
            throw Error($"The pricing formula uses '{name}', which is not an option of this configurator.");
        }

        private decimal ParseNumber()
        {
            var start = _pos;
            while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] == '.'))
                _pos++;
            var token = text[start.._pos];
            if (!decimal.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                throw Error($"'{token}' is not a number.");
            return value;
        }

        private bool Match(string token)
        {
            SkipSpace();
            if (string.CompareOrdinal(text, _pos, token, 0, token.Length) != 0)
                return false;
            _pos += token.Length;
            return true;
        }

        private void Expect(char c)
        {
            SkipSpace();
            if (_pos >= text.Length || text[_pos] != c)
                throw Error($"Expected '{c}' at position {_pos + 1}.");
            _pos++;
        }

        private void SkipSpace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
                _pos++;
        }

        private static CpqFormulaException Error(string message) => new(message);
    }
}
