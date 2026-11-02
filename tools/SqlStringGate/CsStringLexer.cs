using System.Text;

namespace SqlStringGate;

internal sealed record StringGroup(string Text, int[] Map);

internal sealed record LexError(string Kind, int Line);

internal sealed class CsStringLexer
{
    public const char HoleMark = '￼';

    private static readonly char[] ExtraLineBreaks = { '\u0085', '\u2028', '\u2029' };

    private sealed class Piece
    {
        public int Start;
        public int End;
        public readonly List<char> Chars = new();
        public readonly List<int> Map = new();

        public void Add(char c, int pos)
        {
            Chars.Add(c);
            Map.Add(pos);
        }

        public void RemoveLast()
        {
            Chars.RemoveAt(Chars.Count - 1);
            Map.RemoveAt(Map.Count - 1);
        }
    }

    private sealed class LexFailure : Exception
    {
        public LexFailure(string kind, int pos)
        {
            Kind = kind;
            Pos = pos;
        }

        public string Kind { get; }
        public int Pos { get; }
    }

    private readonly string _s;
    private readonly int _n;
    private readonly List<int> _lineStarts = new() { 0 };
    private readonly List<List<Piece>> _scopes = new();
    private List<Piece> _scope;

    public CsStringLexer(string normalizedSource)
    {
        _s = normalizedSource;
        _n = _s.Length;
        for (int i = 0; i < _n; i++)
        {
            if (_s[i] == '\n')
            {
                _lineStarts.Add(i + 1);
            }
        }

        _scope = NewScope();
    }

    public int LineOf(int pos)
    {
        int index = _lineStarts.BinarySearch(pos);
        return index >= 0 ? index + 1 : ~index;
    }

    public List<StringGroup> Run(out LexError? error)
    {
        error = null;
        int extraBreak = _s.IndexOfAny(ExtraLineBreaks);
        if (extraBreak >= 0)
        {
            error = new LexError("break", LineOf(extraBreak));
            return new List<StringGroup>();
        }

        try
        {
            Code(0, 0);
        }
        catch (LexFailure failure)
        {
            error = new LexError(failure.Kind, LineOf(failure.Pos));
            return new List<StringGroup>();
        }

        return Fold();
    }

    private List<Piece> NewScope()
    {
        var scope = new List<Piece>();
        _scopes.Add(scope);
        return scope;
    }

    private char Peek(int i) => i < _n ? _s[i] : '\0';

    private int LineEnd(int i)
    {
        int j = _s.IndexOf('\n', i);
        return j < 0 ? _n : j;
    }

    private int Code(int i, int closes)
    {
        int depth = 0;
        bool lineStart = true;
        while (i < _n)
        {
            char c = _s[i];
            if (c == '\n')
            {
                lineStart = true;
                i++;
                continue;
            }

            if (c is ' ' or '\t')
            {
                i++;
                continue;
            }

            if (c == '#' && lineStart && closes == 0)
            {
                i = LineEnd(i);
                continue;
            }

            lineStart = false;
            if (c == '/' && Peek(i + 1) == '/')
            {
                i = LineEnd(i);
                continue;
            }

            if (c == '/' && Peek(i + 1) == '*')
            {
                int end = _s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    throw new LexFailure("comment", i);
                }

                i = end + 2;
                continue;
            }

            if (c == '\'')
            {
                i = CharLiteral(i);
                continue;
            }

            if (TryStringStart(i, out int quote, out int dollars, out bool verbatim))
            {
                i = StringLiteral(i, quote, dollars, verbatim);
                continue;
            }

            if (closes > 0)
            {
                if (c is '(' or '[' or '{')
                {
                    depth++;
                }
                else if (c is ')' or ']')
                {
                    depth--;
                }
                else if (c == '}')
                {
                    if (depth == 0)
                    {
                        return i;
                    }

                    depth--;
                }
                else if (c == ':' && depth == 0)
                {
                    if (Peek(i + 1) == ':')
                    {
                        i += 2;
                        continue;
                    }

                    int end = _s.IndexOf('}', i);
                    return end < 0 ? _n : end;
                }
            }

            i++;
        }

        return i;
    }

    private int CharLiteral(int i)
    {
        int j = i + 1;
        if (Peek(j) == '\\')
        {
            j += 2;
            while (j < _n && _s[j] != '\'' && j - i < 12)
            {
                j++;
            }

            if (Peek(j) != '\'')
            {
                throw new LexFailure("char", i);
            }

            return j + 1;
        }

        if (j < _n && Peek(j + 1) == '\'')
        {
            return j + 2;
        }

        throw new LexFailure("char", i);
    }

    private bool TryStringStart(int i, out int quote, out int dollars, out bool verbatim)
    {
        int j = i;
        dollars = 0;
        verbatim = false;
        if (Peek(j) == '@')
        {
            verbatim = true;
            j++;
            while (Peek(j) == '$')
            {
                dollars++;
                j++;
            }
        }
        else
        {
            while (Peek(j) == '$')
            {
                dollars++;
                j++;
            }

            if (Peek(j) == '@')
            {
                verbatim = true;
                j++;
            }
        }

        quote = j;
        return Peek(j) == '"';
    }

    private int StringLiteral(int i, int quote, int dollars, bool verbatim)
    {
        var piece = new Piece { Start = i };
        _scope.Add(piece);
        if (verbatim)
        {
            return Verbatim(piece, quote, dollars);
        }

        int quotes = 0;
        while (Peek(quote + quotes) == '"')
        {
            quotes++;
        }

        if (quotes >= 3)
        {
            return Raw(piece, quote, quotes, dollars);
        }

        return Regular(piece, quote, dollars);
    }

    private int Close(Piece piece, int end)
    {
        if (Peek(end) is 'u' or 'U' && Peek(end + 1) == '8')
        {
            end += 2;
        }

        piece.End = end;
        return end;
    }

    private int Regular(Piece piece, int quote, int dollars)
    {
        int j = quote + 1;
        while (j < _n)
        {
            char c = _s[j];
            if (c == '"')
            {
                return Close(piece, j + 1);
            }

            if (c == '\n')
            {
                throw new LexFailure("string", piece.Start);
            }

            if (c == '\\')
            {
                j = Escape(piece, j);
                continue;
            }

            if (dollars > 0 && Brace(piece, ref j))
            {
                continue;
            }

            piece.Add(c, j);
            j++;
        }

        throw new LexFailure("string", piece.Start);
    }

    private int Verbatim(Piece piece, int quote, int dollars)
    {
        int j = quote + 1;
        while (j < _n)
        {
            char c = _s[j];
            if (c == '"')
            {
                if (Peek(j + 1) != '"')
                {
                    return Close(piece, j + 1);
                }

                piece.Add('"', j);
                j += 2;
                continue;
            }

            if (dollars > 0 && Brace(piece, ref j))
            {
                continue;
            }

            piece.Add(c, j);
            j++;
        }

        throw new LexFailure("string", piece.Start);
    }

    private bool Brace(Piece piece, ref int j)
    {
        int at = j;
        char c = _s[at];
        if (c == '{')
        {
            if (Peek(at + 1) == '{')
            {
                piece.Add('{', at);
                j = at + 2;
                return true;
            }

            piece.Add(HoleMark, at);
            j = Hole(at + 1, 1, at);
            return true;
        }

        if (c == '}')
        {
            if (Peek(at + 1) != '}')
            {
                throw new LexFailure("brace", at);
            }

            piece.Add('}', at);
            j = at + 2;
            return true;
        }

        return false;
    }

    private int Raw(Piece piece, int quote, int quotes, int dollars)
    {
        int j = quote + quotes;
        int k = j;
        while (Peek(k) is ' ' or '\t')
        {
            k++;
        }

        bool multiLine = Peek(k) == '\n';
        if (multiLine)
        {
            j = k + 1;
        }

        while (j < _n)
        {
            char c = _s[j];
            if (c == '"')
            {
                int run = 0;
                while (Peek(j + run) == '"')
                {
                    run++;
                }

                if (run >= quotes)
                {
                    if (multiLine)
                    {
                        TrimRawIndent(piece);
                    }

                    return Close(piece, j + run);
                }

                for (int r = 0; r < run; r++)
                {
                    piece.Add('"', j + r);
                }

                j += run;
                continue;
            }

            if (dollars > 0 && c == '{')
            {
                int run = 0;
                while (Peek(j + run) == '{')
                {
                    run++;
                }

                int literal = run >= dollars ? run - dollars : run;
                for (int r = 0; r < literal; r++)
                {
                    piece.Add('{', j + r);
                }

                if (run >= dollars)
                {
                    piece.Add(HoleMark, j);
                    j = Hole(j + run, dollars, j);
                }
                else
                {
                    j += run;
                }

                continue;
            }

            piece.Add(c, j);
            j++;
        }

        throw new LexFailure("string", piece.Start);
    }

    private static void TrimRawIndent(Piece piece)
    {
        int indent = 0;
        while (piece.Chars.Count > 0 && piece.Chars[^1] is ' ' or '\t')
        {
            piece.RemoveLast();
            indent++;
        }

        if (piece.Chars.Count > 0 && piece.Chars[^1] == '\n')
        {
            piece.RemoveLast();
        }

        if (indent == 0)
        {
            return;
        }

        var chars = new List<char>(piece.Chars);
        var map = new List<int>(piece.Map);
        piece.Chars.Clear();
        piece.Map.Clear();
        int skip = indent;
        for (int i = 0; i < chars.Count; i++)
        {
            char c = chars[i];
            if (skip > 0 && c is ' ' or '\t')
            {
                skip--;
                continue;
            }

            skip = c == '\n' ? indent : 0;
            piece.Add(c, map[i]);
        }
    }

    private int Hole(int start, int closes, int open)
    {
        List<Piece> outer = _scope;
        _scope = NewScope();
        int end = Code(start, closes);
        _scope = outer;
        int count = 0;
        while (count < closes && Peek(end) == '}')
        {
            end++;
            count++;
        }

        if (count < closes)
        {
            throw new LexFailure("hole", open);
        }

        return end;
    }

    private int Escape(Piece piece, int j)
    {
        char e = Peek(j + 1);
        char simple = e switch
        {
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            '0' => '\0',
            'a' => '\a',
            'b' => '\b',
            'f' => '\f',
            'v' => '\v',
            '\\' or '"' or '\'' => e,
            _ => '￿',
        };
        if (simple != '￿')
        {
            piece.Add(simple, j);
            return j + 2;
        }

        (int min, int max) = e switch
        {
            'u' => (4, 4),
            'U' => (8, 8),
            'x' => (1, 4),
            _ => throw new LexFailure("escape", j),
        };
        int k = j + 2;
        long value = 0;
        while (k - (j + 2) < max && Uri.IsHexDigit(Peek(k)))
        {
            value = (value * 16) + Uri.FromHex(_s[k]);
            k++;
        }

        if (k - (j + 2) < min || value > 0x10FFFF)
        {
            throw new LexFailure("escape", j);
        }

        if (value <= 0xFFFF)
        {
            piece.Add((char)value, j);
        }
        else
        {
            foreach (char c in char.ConvertFromUtf32((int)value))
            {
                piece.Add(c, j);
            }
        }

        return k;
    }

    private bool PlusOnly(int from, int to)
    {
        int plus = 0;
        int i = from;
        while (i < to)
        {
            char c = _s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '/' && Peek(i + 1) == '/')
            {
                i = LineEnd(i);
            }
            else if (c == '/' && Peek(i + 1) == '*')
            {
                i = _s.IndexOf("*/", i + 2, StringComparison.Ordinal) + 2;
            }
            else if (c == '+')
            {
                plus++;
                i++;
            }
            else
            {
                return false;
            }
        }

        return plus == 1;
    }

    private List<StringGroup> Fold()
    {
        var groups = new List<StringGroup>();
        foreach (List<Piece> scope in _scopes)
        {
            int i = 0;
            while (i < scope.Count)
            {
                var text = new StringBuilder();
                var map = new List<int>();
                int j = i;
                while (true)
                {
                    foreach (char c in scope[j].Chars)
                    {
                        text.Append(c);
                    }

                    map.AddRange(scope[j].Map);
                    if (j + 1 >= scope.Count || !PlusOnly(scope[j].End, scope[j + 1].Start))
                    {
                        break;
                    }

                    j++;
                }

                groups.Add(new StringGroup(text.ToString(), map.ToArray()));
                i = j + 1;
            }
        }

        return groups;
    }
}
