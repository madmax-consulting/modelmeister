using System.Globalization;
using System.Text;

namespace ModelMeister.Scaffolder;

/// <summary>
/// Pratt-style parser for inriver Expression Engine strings. Produces a C# source fragment that
/// calls the typed <c>Ex.*</c> DSL. Falls back to <c>Ex.Raw&lt;string&gt;("...")</c> for unknown
/// constructs so the scaffolder always emits buildable code.
/// </summary>
public sealed class ExpressionParser
{
    private readonly string _src;
    private int _pos;
    private readonly List<string> _warnings = [];
    private readonly ExpressionContext _ctx;

    /// <summary>Diagnostic messages collected during parsing — unparsed input, unknown functions, etc.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    public ExpressionParser(string text) : this(text, ExpressionContext.Empty) { }

    public ExpressionParser(string text, ExpressionContext ctx)
    {
        _src = text;
        _pos = 0;
        _ctx = ctx;
    }

    /// <summary>
    /// Parse a top-level inriver expression (with or without a leading <c>=</c>) and return the
    /// equivalent C# source plus any warnings.
    /// </summary>
    public static (string CSharp, IReadOnlyList<string> Warnings) ParseTopLevel(string text)
        => ParseTopLevel(text, ExpressionContext.Empty);

    /// <summary>
    /// Context-aware overload: when <paramref name="ctx"/> contains the field/link/CVL id used by
    /// the expression, the emitted C# references the scaffolded class/property via <c>nameof</c>
    /// instead of a bare string. Falls back to a string literal for ids not in the context.
    /// </summary>
    public static (string CSharp, IReadOnlyList<string> Warnings) ParseTopLevel(string text, ExpressionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(text)) return ("Ex.S(\"\")", []);

        var trimmed = text.Trim();
        if (trimmed.StartsWith("=")) trimmed = trimmed.Substring(1).Trim();

        var p = new ExpressionParser(trimmed, ctx);
        try
        {
            var cs = p.ParseExpr(0);
            p.SkipWs();
            if (p._pos < p._src.Length)
                p._warnings.Add($"Unparsed trailing input at offset {p._pos}");
            return (cs, p._warnings);
        }
        catch (Exception ex)
        {
            // Always emit something that compiles — preserve the original text in an Ex.Raw and
            // attach the parse error as a /* */ comment.
            return ($"Ex.Raw<string>({Quote(text)}) /* parse failed: {EscapeComment(ex.Message)} */", [ex.Message]);
        }
    }

    // -- Pratt expression parser --
    private string ParseExpr(int minPrec)
    {
        var lhs = ParsePrimary();
        while (true)
        {
            SkipWs();
            if (_pos >= _src.Length) break;
            var op = PeekOp();
            if (op is null) break;
            var (sym, prec, rightAssoc) = op.Value;
            if (prec < minPrec) break;
            _pos += sym.Length;
            var rhs = ParseExpr(rightAssoc ? prec : prec + 1);
            lhs = sym switch
            {
                "+" or "-" or "*" or "/" or "<" or ">" or "<=" or ">=" => $"({lhs} {sym} {rhs})",
                "=" or "==" => $"Ex.Eq({lhs}, {rhs})",
                "<>" or "!=" => $"Ex.Not(Ex.Eq({lhs}, {rhs}))",
                "&" => $"Ex.LsConcatenate({lhs}, {rhs})",
                _ => $"/* unknown op {sym} */ {lhs}",
            };
        }
        return lhs;
    }

    private static readonly (string Sym, int Prec, bool RightAssoc)[] Ops =
    {
        ("<=", 2, false), (">=", 2, false), ("<>", 2, false),
        ("<", 2, false), (">", 2, false), ("=", 2, false), ("==", 2, false), ("!=", 2, false),
        ("+", 3, false), ("-", 3, false),
        ("*", 4, false), ("/", 4, false),
        ("&", 5, false),
    };

    private (string Sym, int Prec, bool RightAssoc)? PeekOp()
    {
        foreach (var op in Ops)
        {
            if (_pos + op.Sym.Length <= _src.Length && _src.AsSpan(_pos, op.Sym.Length).SequenceEqual(op.Sym))
                return op;
        }
        return null;
    }

    private string ParsePrimary()
    {
        SkipWs();
        if (_pos >= _src.Length) throw new InvalidOperationException("Unexpected end of expression.");
        var c = _src[_pos];
        if (c == '(')
        {
            _pos++;
            var inner = ParseExpr(0);
            SkipWs();
            Expect(')');
            return inner;
        }
        if (c == '-' || c == '+')
        {
            _pos++;
            var inner = ParsePrimary();
            return c == '-' ? $"(0 - {inner})" : inner;
        }
        if (c == '\'' || c == '"') return ParseString(c);
        if (char.IsDigit(c)) return ParseNumber();
        if (c == '$') return ParseVar();
        if (char.IsLetter(c) || c == '_') return ParseIdentOrCall();
        throw new InvalidOperationException($"Unexpected '{c}' at offset {_pos}.");
    }

    private string ParseString(char quote)
    {
        _pos++;
        var sb = new StringBuilder();
        while (_pos < _src.Length)
        {
            var c = _src[_pos];
            if (c == quote)
            {
                if (_pos + 1 < _src.Length && _src[_pos + 1] == quote) { sb.Append(quote); _pos += 2; continue; }
                _pos++;
                return $"Ex.S({Quote(sb.ToString())})";
            }
            sb.Append(c);
            _pos++;
        }
        throw new InvalidOperationException("Unterminated string literal.");
    }

    private string ParseNumber()
    {
        var start = _pos;
        var sawDot = false;
        while (_pos < _src.Length && (char.IsDigit(_src[_pos]) || (_src[_pos] == '.' && !sawDot)))
        {
            if (_src[_pos] == '.') sawDot = true;
            _pos++;
        }
        var num = _src.Substring(start, _pos - start);
        if (sawDot) return num.ToString(CultureInfo.InvariantCulture);
        return num;
    }

    private string ParseVar()
    {
        var start = _pos;
        _pos++; // skip $
        while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_')) _pos++;
        var name = _src.Substring(start, _pos - start);
        return name switch
        {
            "$VALUE" => "v",   // closures provide $VALUE as variable v
            "$LANG" => "lang",
            _ => $"/* unknown var {name} */ Ex.S(\"\")",
        };
    }

    private string ParseIdentOrCall()
    {
        var start = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_')) _pos++;
        var ident = _src.Substring(start, _pos - start);
        SkipWs();
        if (_pos < _src.Length && _src[_pos] == '(')
        {
            _pos++;
            var fn = ident.ToUpperInvariant();
            var args = new List<string>();
            // rawArgs[i] is the raw inriver id string when args[i] was read as a leading-id literal,
            // so EmitCall can rewrite the whole call to a strongly-typed form (lambda / generic) when
            // the id resolves through _ctx. Otherwise null and EmitCall keeps the string-id shape.
            var rawArgs = new List<string?>();
            // Number of leading args this function expects to be inriver string ids (not Expr<T>).
            // Those are parsed as raw strings, not Expr<string>, so they type-check against the
            // string-id Ex.* overloads and so EmitCall can map them to typed model references.
            var idArity = LeadingIdArgsFor(fn);
            SkipWs();
            if (_pos < _src.Length && _src[_pos] != ')')
            {
                var argIndex = 0;
                while (true)
                {
                    SkipWs();
                    var isIdArg = argIndex < idArity;
                    var isLiteralStringArg = IsLiteralStringArg(fn, argIndex);
                    if ((isIdArg || isLiteralStringArg) && _pos < _src.Length && (_src[_pos] == '\'' || _src[_pos] == '"'))
                    {
                        var rawId = ReadRawStringLiteral(_src[_pos]);
                        args.Add(Quote(rawId));
                        rawArgs.Add(isIdArg ? rawId : null);
                    }
                    else
                    {
                        // Either past the leading id args, or the id arg isn't a literal (e.g. a
                        // nested function call producing a dynamic id — unusual but legal). Fall
                        // back to generic expression parsing.
                        args.Add(ParseExpr(0));
                        rawArgs.Add(null);
                    }
                    argIndex++;
                    SkipWs();
                    if (_pos < _src.Length && _src[_pos] == ',') { _pos++; continue; }
                    break;
                }
            }
            SkipWs();
            Expect(')');
            return EmitCall(fn, args, rawArgs);
        }
        // Bare identifier — inriver expressions use bare TRUE/FALSE as boolean literals (commonly as
        // the catch-all condition of IFS). Emit them as typed Expr<bool> so they slot into bool-
        // accepting positions. Anything else falls back to a string literal.
        var upper = ident.ToUpperInvariant();
        if (upper == "TRUE") return "(Expr<bool>)true";
        if (upper == "FALSE") return "(Expr<bool>)false";
        _warnings.Add($"Bare identifier '{ident}' — emitted as string literal.");
        return $"Ex.S({Quote(ident)})";
    }

    /// <summary>How many leading args of <paramref name="fn"/> are inriver string ids (not Expr).</summary>
    private static int LeadingIdArgsFor(string fn) => fn switch
    {
        "FIELDVALUE" => 1,
        "FIELDCVLVALUE" => 1,
        "LINKEDENTITIES" => 1,
        "FIRSTLINKEDENTITY" => 1,
        "CVLVALUE" => 2,
        "LOCALESTRINGVALUE" => 1,
        _ => 0,
    };

    /// <summary>
    /// Positions where the typed <c>Ex.*</c> helper takes a bare <c>string</c> (regex pattern,
    /// replacement) rather than an <c>Expr&lt;string&gt;</c>. For these we read the source as a raw
    /// string literal so the emitted C# matches the helper signature.
    /// </summary>
    private static bool IsLiteralStringArg(string fn, int argIndex) => fn switch
    {
        "REGEXTEST" or "REGEXEXTRACT" => argIndex == 1,
        "REGEXREPLACE" => argIndex is 1 or 2,
        "LOCALESTRINGVALUE" => argIndex == 1, // language code
        "TEXTJOIN" => argIndex == 0,          // delimiter
        _ => false,
    };

    /// <summary>Consume a <c>'…'</c> or <c>"…"</c> literal and return the unquoted inner text.</summary>
    private string ReadRawStringLiteral(char quote)
    {
        _pos++; // skip opening quote
        var sb = new StringBuilder();
        while (_pos < _src.Length)
        {
            var c = _src[_pos];
            if (c == quote)
            {
                if (_pos + 1 < _src.Length && _src[_pos + 1] == quote) { sb.Append(quote); _pos += 2; continue; }
                _pos++;
                return sb.ToString();
            }
            sb.Append(c);
            _pos++;
        }
        throw new InvalidOperationException("Unterminated string literal.");
    }

    private string EmitCall(string fn, List<string> args, List<string?> rawArgs)
    {
        // Mapping inriver Expression Engine functions to our typed Ex.* methods.
        // When a leading id-arg resolves through _ctx, switch to the typed shape that points at the
        // scaffolded class/property directly (no `nameof` indirection, no string-id round-trip).
        string ArgsAll() => string.Join(", ", args);

        // Pre-resolve the typed shape for arg0 when applicable (used by FIELDVALUE/FIELDCVLVALUE).
        FieldRef? fieldRef0 = rawArgs.Count > 0 && rawArgs[0] is { } raw0Field
            && _ctx.Fields.TryGetValue(raw0Field, out var fref) ? fref : null;
        string? linkTypeClass0 = rawArgs.Count > 0 && rawArgs[0] is { } raw0Link
            && _ctx.LinkTypes.TryGetValue(raw0Link, out var lt) ? lt : null;
        string? cvlClass0 = rawArgs.Count > 0 && rawArgs[0] is { } raw0Cvl
            && _ctx.Cvls.TryGetValue(raw0Cvl, out var cvl) ? cvl : null;

        return fn switch
        {
            "CONCATENATE" => $"Ex.Concatenate({ArgsAll()})",
            "FIELDVALUE" when fieldRef0 is { } f && args.Count >= 1 =>
                args.Count == 1
                    ? $"Ex.FieldValue(({f.EntityClass} r) => r.{f.PropertyName})"
                    : $"Ex.FieldValue(({f.EntityClass} r) => r.{f.PropertyName}, {args[1]})",
            "FIELDVALUE" => args.Count >= 1 ? $"Ex.FieldValue<string>({args[0]})" : Fallback(),
            "FIELDCVLVALUE" when fieldRef0 is { } f && args.Count >= 1 =>
                args.Count == 1
                    ? $"Ex.FieldCvlValue(({f.EntityClass} r) => r.{f.PropertyName})"
                    : $"Ex.FieldCvlValue(({f.EntityClass} r) => r.{f.PropertyName}, {args[1]})",
            "FIELDCVLVALUE" => args.Count >= 1 ? $"Ex.FieldCvlValue({args[0]})" : Fallback(),
            "CVLVALUE" when cvlClass0 is { } c && args.Count == 2 => $"Ex.CvlValue<{c}>({args[1]})",
            "CVLVALUE" => args.Count == 2 ? $"Ex.CvlValue({args[0]}, {args[1]})" : Fallback(),
            "LINKEDENTITIES" when linkTypeClass0 is { } l && args.Count == 1 => $"Ex.LinkedEntities<{l}>()",
            "LINKEDENTITIES" => args.Count >= 1 ? $"Ex.LinkedEntities({args[0]})" : Fallback(),
            "FIRSTLINKEDENTITY" when linkTypeClass0 is { } l && args.Count == 1 => $"Ex.FirstLinkedEntity<{l}>()",
            "FIRSTLINKEDENTITY" => args.Count >= 1 ? $"Ex.FirstLinkedEntity({args[0]})" : Fallback(),
            "IF" => args.Count == 3 ? $"Ex.If({args[0]}, {args[1]}, {args[2]})" : Fallback(),
            "IFS" => EmitIfs(args),
            "AND" => $"Ex.And({ArgsAll()})",
            "OR" => $"Ex.Or({ArgsAll()})",
            "NOT" => args.Count == 1 ? $"Ex.Not({args[0]})" : Fallback(),
            "ISEMPTY" => args.Count == 1 ? $"Ex.IsEmpty({args[0]})" : Fallback(),
            "ISNUMBER" => args.Count == 1 ? $"Ex.IsNumber({args[0]})" : Fallback(),
            "ISERROR" => args.Count == 1 ? $"Ex.IsError({args[0]})" : Fallback(),
            "ROUND" => args.Count == 2 ? $"Ex.Round({args[0]}, {args[1]})" : Fallback(),
            "ROUNDUP" => args.Count == 2 ? $"Ex.RoundUp({args[0]}, {args[1]})" : Fallback(),
            "ROUNDDOWN" => args.Count == 2 ? $"Ex.RoundDown({args[0]}, {args[1]})" : Fallback(),
            "ABS" => args.Count == 1 ? $"Ex.Abs({args[0]})" : Fallback(),
            "SUM" => args.Count == 1 ? $"Ex.Sum({args[0]})" : Fallback(),
            "AVERAGE" => $"Ex.Average({ArgsAll()})",
            "MIN" => $"Ex.Min({ArgsAll()})",
            "MAX" => $"Ex.Max({ArgsAll()})",
            "PI" => "Ex.Pi",
            "REGEXTEST" => args.Count == 2 ? $"Ex.RegexTest({args[0]}, {args[1]})" : Fallback(),
            "REGEXEXTRACT" => args.Count == 2
                ? $"Ex.RegexExtract({args[0]}, {args[1]})"
                : args.Count == 3 ? $"Ex.RegexExtract({args[0]}, {args[1]}, {args[2]})" : Fallback(),
            "REGEXREPLACE" => args.Count == 3 ? $"Ex.RegexReplace({args[0]}, {args[1]}, {args[2]})" : Fallback(),
            "TRIM" => args.Count == 1 ? $"Ex.Trim({args[0]})" : Fallback(),
            "UPPER" => args.Count == 1 ? $"Ex.Upper({args[0]})" : Fallback(),
            "LOWER" => args.Count == 1 ? $"Ex.Lower({args[0]})" : Fallback(),
            "LEN" => args.Count == 1 ? $"Ex.Len({args[0]})" : Fallback(),
            "LEFT" => args.Count == 1 ? $"Ex.Left({args[0]})" : args.Count == 2 ? $"Ex.Left({args[0]}, {args[1]})" : Fallback(),
            "RIGHT" => args.Count == 1 ? $"Ex.Right({args[0]})" : args.Count == 2 ? $"Ex.Right({args[0]}, {args[1]})" : Fallback(),
            "TEXT" => args.Count == 1 ? $"Ex.Text({args[0]})" : Fallback(),
            "CHAR" => args.Count == 1 ? $"Ex.Char({args[0]})" : Fallback(),
            "LOCALESTRINGVALUE" when fieldRef0 is { } lf && args.Count >= 2 =>
                args.Count == 2
                    ? $"Ex.LocaleStringValue(({lf.EntityClass} r) => r.{lf.PropertyName}, {args[1]})"
                    : $"Ex.LocaleStringValue(({lf.EntityClass} r) => r.{lf.PropertyName}, {args[1]}, {args[2]})",
            "LOCALESTRINGVALUE" => args.Count is 2 or 3 ? $"Ex.LocaleStringValue({string.Join(", ", args)})" : Fallback(),
            "TEXTJOIN" => args.Count >= 2 ? EmitTextJoin(args) : Fallback(),
            "TRUE" => "(Expr<bool>)true",
            "FALSE" => "(Expr<bool>)false",
            _ => Fallback(),
        };

        string Fallback()
        {
            _warnings.Add($"Unknown function {fn} (arity {args.Count}) — emitted as Ex.Raw.");
            var raw = $"={fn}({string.Join(", ", args.Select(a => a))})";
            return $"Ex.Raw<string>({Quote(raw)})";
        }

        string EmitTextJoin(List<string> a)
        {
            // Ex.TextJoin(string delimiter, bool ignoreEmpty, params Expr[] inputs).
            // Strip the (Expr<bool>) cast our identifier parser emits for bare TRUE/FALSE so the
            // bool slot type-checks against the method signature.
            var ignore = a[1] switch
            {
                "(Expr<bool>)true" => "true",
                "(Expr<bool>)false" => "false",
                _ => a[1],
            };
            var rest = a.Count > 2 ? ", " + string.Join(", ", a.Skip(2)) : "";
            return $"Ex.TextJoin({a[0]}, {ignore}{rest})";
        }

        string EmitIfs(List<string> a)
        {
            // IFS(cond1, val1, cond2, val2, ..., [TRUE, default])
            if (a.Count % 2 != 0) return Fallback();
            var pairs = new List<string>();
            for (var i = 0; i < a.Count; i += 2)
                pairs.Add($"({a[i]}, {a[i + 1]})");
            return $"Ex.Ifs(new (Expr<bool>, Expr<string>)[]{{ {string.Join(", ", pairs)} }})";
        }
    }

    private void Expect(char c)
    {
        if (_pos >= _src.Length || _src[_pos] != c) throw new InvalidOperationException($"Expected '{c}' at offset {_pos}.");
        _pos++;
    }

    private void SkipWs()
    {
        while (_pos < _src.Length && (char.IsWhiteSpace(_src[_pos]) || _src[_pos] == '\n' || _src[_pos] == '\r')) _pos++;
        // Skip /* ... */ comments and // line comments
        if (_pos + 1 < _src.Length && _src[_pos] == '/' && _src[_pos + 1] == '*')
        {
            _pos += 2;
            while (_pos + 1 < _src.Length && !(_src[_pos] == '*' && _src[_pos + 1] == '/')) _pos++;
            if (_pos + 1 < _src.Length) _pos += 2;
            SkipWs();
        }
        if (_pos + 1 < _src.Length && _src[_pos] == '/' && _src[_pos + 1] == '/')
        {
            while (_pos < _src.Length && _src[_pos] != '\n') _pos++;
            SkipWs();
        }
    }

    private static string Quote(string s)
    {
        var escaped = string.Concat(s.Select(c => c switch
        {
            '"' or '\\' => "\\" + c,
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ => c.ToString(),
        }));
        return $"\"{escaped}\"";
    }

    private static string EscapeComment(string s) => s.Replace("*/", "*\\/");
}
