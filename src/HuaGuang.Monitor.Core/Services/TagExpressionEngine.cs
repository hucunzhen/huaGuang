using System.Globalization;
using System.Text;

namespace HuaGuang.Monitor.Services;

/// <summary>受限表达式：数字、+ - * / ( )、引用 [点位名称]。</summary>
public static class TagExpressionEngine
{
    public static IReadOnlyList<string> ExtractReferences(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return [];
        }

        var list = new List<string>();
        var index = 0;
        while (index < expression.Length)
        {
            if (expression[index] != '[')
            {
                index++;
                continue;
            }

            index++;
            var start = index;
            while (index < expression.Length && expression[index] != ']')
            {
                index++;
            }

            if (index >= expression.Length)
            {
                break;
            }

            var name = expression[start..index].Trim();
            if (name.Length > 0)
            {
                list.Add(name);
            }

            index++;
        }

        return list;
    }

    public static bool TryEvaluate(
        string expression,
        IReadOnlyDictionary<string, object?> values,
        out double result,
        out string error)
    {
        result = 0;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "表达式为空。";
            return false;
        }

        try
        {
            var tokens = Tokenize(expression);
            var parser = new Parser(tokens, values);
            result = parser.ParseExpression();
            if (parser.HasRemaining)
            {
                error = "表达式含有无法解析的内容。";
                return false;
            }

            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                error = "计算结果无效（NaN/Infinity）。";
                return false;
            }

            return true;
        }
        catch (TagExpressionException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryCoerceToDouble(object? value, out double number, out string error)
    {
        number = 0;
        error = string.Empty;
        if (value is null)
        {
            error = "值为空。";
            return false;
        }

        switch (value)
        {
            case bool b:
                number = b ? 1 : 0;
                return true;
            case byte v:
                number = v;
                return true;
            case sbyte v:
                number = v;
                return true;
            case short v:
                number = v;
                return true;
            case ushort v:
                number = v;
                return true;
            case int v:
                number = v;
                return true;
            case uint v:
                number = v;
                return true;
            case long v:
                number = v;
                return true;
            case ulong v:
                number = v;
                return true;
            case float v:
                number = v;
                return true;
            case double v:
                number = v;
                return true;
            case decimal v:
                number = (double)v;
                return true;
            case string text:
                if (bool.TryParse(text, out var boolText))
                {
                    number = boolText ? 1 : 0;
                    return true;
                }

                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                    || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number))
                {
                    return true;
                }

                error = $"无法将文本「{text}」转为数值。";
                return false;
            default:
                error = $"不支持的数据类型 {value.GetType().Name}。";
                return false;
        }
    }

    static List<Token> Tokenize(string expression)
    {
        var tokens = new List<Token>();
        var index = 0;
        while (index < expression.Length)
        {
            var ch = expression[index];
            if (char.IsWhiteSpace(ch))
            {
                index++;
                continue;
            }

            if (ch is '+' or '-' or '*' or '/' or '(' or ')')
            {
                tokens.Add(new Token(TokenKind.Operator, ch.ToString()));
                index++;
                continue;
            }

            if (ch == '[')
            {
                index++;
                var start = index;
                while (index < expression.Length && expression[index] != ']')
                {
                    index++;
                }

                if (index >= expression.Length)
                {
                    throw new TagExpressionException("缺少 ]，点位引用格式应为 [名称]。");
                }

                var name = expression[start..index].Trim();
                if (name.Length == 0)
                {
                    throw new TagExpressionException("点位引用 [ ] 内不能为空。");
                }

                tokens.Add(new Token(TokenKind.Reference, name));
                index++;
                continue;
            }

            if (char.IsDigit(ch) || ch == '.')
            {
                var start = index;
                index++;
                while (index < expression.Length && (char.IsDigit(expression[index]) || expression[index] == '.'))
                {
                    index++;
                }

                var text = expression[start..index];
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    throw new TagExpressionException($"无效数字「{text}」。");
                }

                tokens.Add(new Token(TokenKind.Number, text));
                continue;
            }

            throw new TagExpressionException($"无法识别的字符「{ch}」。");
        }

        return tokens;
    }

    enum TokenKind
    {
        Number,
        Reference,
        Operator
    }

    readonly record struct Token(TokenKind Kind, string Text);

    sealed class TagExpressionException(string message) : Exception(message);

    sealed class Parser(List<Token> tokens, IReadOnlyDictionary<string, object?> values)
    {
        int _index;

        public bool HasRemaining => _index < tokens.Count;

        public double ParseExpression()
        {
            var value = ParseTerm();
            while (TryPeekOperator(out var op) && op is '+' or '-')
            {
                TryConsumeOperator(out _);
                var right = ParseTerm();
                value = op == '+' ? value + right : value - right;
            }

            return value;
        }

        double ParseTerm()
        {
            var value = ParseUnary();
            while (TryPeekOperator(out var op) && op is '*' or '/')
            {
                TryConsumeOperator(out _);
                var right = ParseUnary();
                if (op == '/')
                {
                    if (Math.Abs(right) < double.Epsilon)
                    {
                        throw new TagExpressionException("除数为零。");
                    }

                    value /= right;
                }
                else
                {
                    value *= right;
                }
            }

            return value;
        }

        double ParseUnary()
        {
            if (TryPeekOperator(out var op) && op == '-')
            {
                TryConsumeOperator(out _);
                return -ParseUnary();
            }

            return ParsePrimary();
        }

        double ParsePrimary()
        {
            if (_index >= tokens.Count)
            {
                throw new TagExpressionException("表达式不完整。");
            }

            var token = tokens[_index++];
            switch (token.Kind)
            {
                case TokenKind.Number:
                    return double.Parse(token.Text, CultureInfo.InvariantCulture);
                case TokenKind.Reference:
                    if (!values.TryGetValue(token.Text, out var raw))
                    {
                        throw new TagExpressionException($"未找到点位「{token.Text}」的当前值。");
                    }

                    if (!TryCoerceToDouble(raw, out var number, out var error))
                    {
                        throw new TagExpressionException($"点位「{token.Text}」：{error}");
                    }

                    return number;
                case TokenKind.Operator when token.Text == "(":
                {
                    var inner = ParseExpression();
                    if (!TryConsumeOperator(out var close) || close != ')')
                    {
                        throw new TagExpressionException("缺少 )。");
                    }

                    return inner;
                }
                default:
                    throw new TagExpressionException("表达式语法错误。");
            }
        }

        bool TryPeekOperator(out char op)
        {
            op = default;
            if (_index >= tokens.Count || tokens[_index].Kind != TokenKind.Operator)
            {
                return false;
            }

            op = tokens[_index].Text[0];
            return true;
        }

        bool TryConsumeOperator(out char op)
        {
            if (!TryPeekOperator(out op))
            {
                return false;
            }

            _index++;
            return true;
        }
    }
}
