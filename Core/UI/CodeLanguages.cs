using System;
using System.Collections.Generic;
using POrigami = global::Prowl.OrigamiUI;

namespace V12.Core.UI
{
    /// <summary>
    /// Small line-based languages for the Origami code editor: C#, Lua and plain
    /// text. Lexing is per line with a block-comment carry state, which is what the
    /// editor requires for virtualised highlighting and background folding.
    /// </summary>
    public static class V12CodeLanguages
    {
        public static readonly POrigami.CodeLanguage Plain = new POrigami.CodeLanguage();

        public static readonly POrigami.CodeLanguage CSharp;
        public static readonly POrigami.CodeLanguage Lua;

        static V12CodeLanguages()
        {
            // Assigned here so the keyword sets (declared below) are initialised first.
            CSharp = new LexedLanguage(
                "C#", new SimpleLexer(CSharpKeywords, CSharpControlKeywords, "//", "/*", "*/", preprocessor: true));
            Lua = new LexedLanguage(
                "Lua", new SimpleLexer(LuaKeywords, LuaControlKeywords, "--", "--[[", "]]", preprocessor: false));
        }

        /// <summary>Language id used by <see cref="V12.UI.ICodeEditor"/>.</summary>
        public static string IdForExtension(string? extension) => (extension ?? "").ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".lua" => "lua",
            _ => "plain",
        };

        public static POrigami.CodeLanguage ForId(string? id) => (id ?? "").ToLowerInvariant() switch
        {
            "csharp" or "cs" => CSharp,
            "lua" => Lua,
            _ => Plain,
        };

        private sealed class LexedLanguage : POrigami.CodeLanguage
        {
            private readonly string _name;
            private readonly POrigami.ICodeLexer _lexer;
            private readonly string _lineComment;
            private readonly string _blockStart;
            private readonly string _blockEnd;

            public LexedLanguage(string name, SimpleLexer lexer)
            {
                _name = name;
                _lexer = lexer;
                _lineComment = lexer.LineComment;
                _blockStart = lexer.BlockCommentStart;
                _blockEnd = lexer.BlockCommentEnd;
            }

            public override string Name => _name;
            public override POrigami.ICodeLexer? Lexer => _lexer;
            public override string? LineComment => _lineComment;
            public override string? BlockCommentStart => _blockStart;
            public override string? BlockCommentEnd => _blockEnd;
        }

        private sealed class SimpleLexer : POrigami.ICodeLexer
        {
            private const int StateNormal = 0;
            private const int StateBlockComment = 1;

            private enum Class { Identifier, Keyword, Control, Number, String, Char, Comment, Operator, Punctuation }

            private readonly HashSet<string> _keywords;
            private readonly HashSet<string> _control;
            private readonly bool _preprocessor;

            public SimpleLexer(HashSet<string> keywords, HashSet<string> control,
                string lineComment, string blockStart, string blockEnd, bool preprocessor)
            {
                _keywords = keywords;
                _control = control;
                _preprocessor = preprocessor;
                LineComment = lineComment;
                BlockCommentStart = blockStart;
                BlockCommentEnd = blockEnd;
            }

            public string LineComment { get; }
            public string BlockCommentStart { get; }
            public string BlockCommentEnd { get; }

            public int Tokenize(string line, int state, List<POrigami.CodeToken>? tokens)
            {
                int n = line.Length;
                int i = 0;

                if (state == StateBlockComment)
                {
                    int end = line.IndexOf(BlockCommentEnd, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        Emit(tokens, 0, n, Class.Comment);
                        return StateBlockComment;
                    }
                    Emit(tokens, 0, end + BlockCommentEnd.Length, Class.Comment);
                    i = end + BlockCommentEnd.Length;
                }

                int firstNonSpace = 0;
                while (firstNonSpace < n && char.IsWhiteSpace(line[firstNonSpace])) firstNonSpace++;

                while (i < n)
                {
                    char c = line[i];

                    if (char.IsWhiteSpace(c)) { i++; continue; }

                    if (_preprocessor && i == firstNonSpace && c == '#')
                    {
                        Emit(tokens, i, n - i, Class.Comment);
                        return StateNormal;
                    }

                    if (MatchAt(line, i, LineComment))
                    {
                        Emit(tokens, i, n - i, Class.Comment);
                        return StateNormal;
                    }

                    if (MatchAt(line, i, BlockCommentStart))
                    {
                        int end = line.IndexOf(BlockCommentEnd, i + BlockCommentStart.Length, StringComparison.Ordinal);
                        if (end < 0)
                        {
                            Emit(tokens, i, n - i, Class.Comment);
                            return StateBlockComment;
                        }
                        int stop = end + BlockCommentEnd.Length;
                        Emit(tokens, i, stop - i, Class.Comment);
                        i = stop;
                        continue;
                    }

                    if (c == '"' || c == '\'')
                    {
                        int start = i;
                        bool closed = false;
                        i++;
                        while (i < n)
                        {
                            char d = line[i];
                            if (d == '\\' && i + 1 < n) { i += 2; continue; }
                            i++;
                            if (d == c) { closed = true; break; }
                        }
                        Emit(tokens, start, i - start, c == '"' ? Class.String : Class.Char);
                        if (!closed) return StateNormal;
                        continue;
                    }

                    if (char.IsDigit(c))
                    {
                        int start = i;
                        while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '.' || line[i] == '_'))
                            i++;
                        Emit(tokens, start, i - start, Class.Number);
                        continue;
                    }

                    if (char.IsLetter(c) || c == '_')
                    {
                        int start = i;
                        while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '_' || line[i] == '@'))
                            i++;
                        string word = line.Substring(start, i - start);
                        Class kind = _control.Contains(word) ? Class.Control
                                   : _keywords.Contains(word) ? Class.Keyword
                                   : Class.Identifier;
                        Emit(tokens, start, i - start, kind);
                        continue;
                    }

                    // Multi-character operators first.
                    int opLen = OperatorLength(line, i);
                    Emit(tokens, i, opLen,
                        opLen > 1 ? Class.Operator : IsPunctuation(c) ? Class.Punctuation : Class.Operator);
                    i += opLen;
                }

                return StateNormal;
            }

            private static bool MatchAt(string line, int index, string token)
                => token.Length > 0 && index + token.Length <= line.Length
                   && string.CompareOrdinal(line, index, token, 0, token.Length) == 0;

            private static bool IsPunctuation(char c)
                => c is '(' or ')' or '[' or ']' or '{' or '}' or ',' or ';' or '.' or ':';

            private static int OperatorLength(string line, int i)
            {
                if (i + 1 < line.Length)
                {
                    string two = line.Substring(i, 2);
                    switch (two)
                    {
                        case "=>": case "==": case "!=": case "<=": case ">=":
                        case "&&": case "||": case "++": case "--": case "+=":
                        case "-=": case "*=": case "/=": case "%=": case "->":
                        case "::": case "??": case "<<": case ">>": case "?.":
                            return 2;
                    }
                }
                return 1;
            }

            private static void Emit(List<POrigami.CodeToken>? tokens, int start, int length, Class kind)
            {
                if (tokens == null || length <= 0) return;
                tokens.Add(new POrigami.CodeToken(start, length, ToKind(kind)));
            }

            private static POrigami.CodeTokenKind ToKind(Class kind) => kind switch
            {
                Class.Keyword => POrigami.CodeTokenKind.Keyword,
                Class.Control => POrigami.CodeTokenKind.ControlKeyword,
                Class.Number => POrigami.CodeTokenKind.Number,
                Class.String => POrigami.CodeTokenKind.String,
                Class.Char => POrigami.CodeTokenKind.Char,
                Class.Comment => POrigami.CodeTokenKind.Comment,
                Class.Operator => POrigami.CodeTokenKind.Operator,
                Class.Punctuation => POrigami.CodeTokenKind.Punctuation,
                _ => POrigami.CodeTokenKind.Identifier,
            };
        }

        private static readonly HashSet<string> CSharpControlKeywords = new(StringComparer.Ordinal)
        {
            "if", "else", "for", "foreach", "while", "do", "switch", "case", "default",
            "break", "continue", "return", "goto", "throw", "try", "catch", "finally",
            "using", "lock", "yield", "await", "checked", "unchecked", "when",
        };

        private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "byte", "char", "class", "const", "decimal",
            "delegate", "double", "enum", "event", "explicit", "extern", "false", "fixed",
            "float", "implicit", "in", "int", "interface", "internal", "is", "long",
            "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "record", "ref", "sbyte",
            "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
            "this", "true", "typeof", "uint", "ulong", "unsafe", "ushort", "var",
            "virtual", "void", "volatile", "async", "global", "init", "required",
        };

        private static readonly HashSet<string> LuaControlKeywords = new(StringComparer.Ordinal)
        {
            "if", "then", "else", "elseif", "end", "for", "while", "do", "repeat",
            "until", "return", "break", "goto",
        };

        private static readonly HashSet<string> LuaKeywords = new(StringComparer.Ordinal)
        {
            "and", "or", "not", "nil", "true", "false", "function", "local", "in",
        };
    }
}
