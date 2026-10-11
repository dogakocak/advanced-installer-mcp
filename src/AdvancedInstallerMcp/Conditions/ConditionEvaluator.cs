using System.Globalization;

namespace AdvancedInstallerMcp.Conditions;

// Evaluates a Windows Installer conditional statement against property values (Microsoft Learn, "Conditional
// Statement Syntax"), for ControlCondition rows (ADR 0006). Returns null when the answer depends on install-time state
// the package rows cannot tell (environment variables, feature and component states) or the text is not a valid
// condition; the caller then leaves the control as authored.
public static class ConditionEvaluator
{
    public static bool? Evaluate(string condition, IReadOnlyDictionary<string, string> properties)
    {
        try
        {
            var parser = new Parser(Tokenize(condition), properties);
            var result = parser.Expression();
            parser.ExpectEnd();
            return result;
        }
        catch (UnknownException)
        {
            return null;
        }
    }

    private sealed class UnknownException : Exception;

    private enum Kind
    {
        Property,
        String,
        Integer,
        Keyword,
        Operator,
        Open,
        Close,
    }

    private sealed record Token(Kind Kind, string Text);

    // A value in a comparison: a string, or an integer (literal, or a property whose value converts to one).
    // On its own an integer literal is true when not 0; a property or string when not empty (so "0" is true).
    private readonly record struct Value(string Text, int? Integer, bool IsLiteralInteger = false)
    {
        public bool IsTrue => IsLiteralInteger ? Integer != 0 : Text.Length > 0;
    }

    private static readonly string[] Operators = ["<>", "<=", ">=", "><", "<<", ">>", "=", "<", ">"];
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase) { "NOT", "AND", "OR", "XOR", "EQV", "IMP" };

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '(' || c == ')')
            {
                tokens.Add(new Token(c == '(' ? Kind.Open : Kind.Close, c.ToString()));
                i++;
            }
            else if (c == '"')
            {
                var close = text.IndexOf('"', i + 1);
                if (close < 0)
                {
                    throw new UnknownException();
                }

                tokens.Add(new Token(Kind.String, text[(i + 1)..close]));
                i = close + 1;
            }
            else if (c is '%' or '$' or '?' or '&' or '!')
            {
                // Environment variable, component or feature state: only known while installing.
                throw new UnknownException();
            }
            else if (char.IsDigit(c) || (c == '-' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                var start = i++;
                while (i < text.Length && char.IsDigit(text[i]))
                {
                    i++;
                }

                if (i < text.Length && (char.IsLetter(text[i]) || text[i] == '.'))
                {
                    throw new UnknownException(); // floating point is not supported
                }

                tokens.Add(new Token(Kind.Integer, text[start..i]));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.'))
                {
                    i++;
                }

                var word = text[start..i];
                tokens.Add(new Token(Keywords.Contains(word) ? Kind.Keyword : Kind.Property, word));
            }
            else
            {
                var ignoreCase = c == '~';
                var at = ignoreCase ? i + 1 : i;
                var op = Operators.FirstOrDefault(o => string.CompareOrdinal(text, at, o, 0, o.Length) == 0)
                    ?? throw new UnknownException();
                tokens.Add(new Token(Kind.Operator, (ignoreCase ? "~" : "") + op));
                i = at + op.Length;
            }
        }

        return tokens;
    }

    // Precedence from high to low: NOT, AND, OR, XOR, EQV, IMP.
    private sealed class Parser(List<Token> tokens, IReadOnlyDictionary<string, string> properties)
    {
        private int _position;

        private Token? Peek => _position < tokens.Count ? tokens[_position] : null;

        public void ExpectEnd()
        {
            if (Peek is not null)
            {
                throw new UnknownException();
            }
        }

        public bool Expression() => Binary("IMP", Eqv, (a, b) => !a || b);

        private bool Eqv() => Binary("EQV", Xor, (a, b) => a == b);

        private bool Xor() => Binary("XOR", Or, (a, b) => a ^ b);

        private bool Or() => Binary("OR", And, (a, b) => a || b);

        private bool And() => Binary("AND", Not, (a, b) => a && b);

        private bool Binary(string keyword, Func<bool> operand, Func<bool, bool, bool> combine)
        {
            var left = operand();
            while (IsKeyword(keyword))
            {
                _position++;
                left = combine(left, operand());
            }

            return left;
        }

        private bool Not()
        {
            if (IsKeyword("NOT"))
            {
                _position++;
                return !Not();
            }

            return Term();
        }

        private bool Term()
        {
            if (Peek?.Kind == Kind.Open)
            {
                _position++;
                var inner = Expression();
                if (Peek?.Kind != Kind.Close)
                {
                    throw new UnknownException();
                }

                _position++;
                return inner;
            }

            var left = ReadValue();
            if (Peek?.Kind != Kind.Operator)
            {
                return left.IsTrue;
            }

            var op = tokens[_position++].Text;
            return Compare(left, op, ReadValue());
        }

        private Value ReadValue()
        {
            var token = Peek ?? throw new UnknownException();
            _position++;
            return token.Kind switch
            {
                Kind.String => new Value(token.Text, null),
                Kind.Integer => new Value(token.Text, int.Parse(token.Text, CultureInfo.InvariantCulture), IsLiteralInteger: true),
                Kind.Property => PropertyValue(token.Text),
                _ => throw new UnknownException(),
            };
        }

        private Value PropertyValue(string name)
        {
            var text = properties.GetValueOrDefault(name, "");
            return new Value(text, int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : null);
        }

        private bool IsKeyword(string keyword) =>
            Peek is { Kind: Kind.Keyword } token && string.Equals(token.Text, keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Compare(Value left, string op, Value right)
    {
        var ignoreCase = op.StartsWith('~');
        op = op.TrimStart('~');

        if (left.Integer is { } a && right.Integer is { } b)
        {
            return op switch
            {
                "=" => a == b,
                "<>" => a != b,
                ">" => a > b,
                ">=" => a >= b,
                "<" => a < b,
                "<=" => a <= b,
                "><" => (a & b) != 0,
                "<<" => (int)((uint)a >> 16) == b,
                ">>" => (a & 0xFFFF) == b,
                _ => throw new UnknownException(),
            };
        }

        // An integer against a value that cannot be one: always false, except for <>.
        if (left.IsLiteralInteger || right.IsLiteralInteger)
        {
            return op == "<>";
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return op switch
        {
            "=" => string.Equals(left.Text, right.Text, comparison),
            "<>" => !string.Equals(left.Text, right.Text, comparison),
            ">" => string.Compare(left.Text, right.Text, comparison) > 0,
            ">=" => string.Compare(left.Text, right.Text, comparison) >= 0,
            "<" => string.Compare(left.Text, right.Text, comparison) < 0,
            "<=" => string.Compare(left.Text, right.Text, comparison) <= 0,
            "><" => left.Text.Contains(right.Text, comparison),
            "<<" => left.Text.StartsWith(right.Text, comparison),
            ">>" => left.Text.EndsWith(right.Text, comparison),
            _ => throw new UnknownException(),
        };
    }
}
