#region Copyright notice and license

/*The MIT License(MIT)
Copyright(c), Tobey Peters, https://github.com/tobeypeters

Permission is hereby granted, free of charge, to any person obtaining a copy of this software
and associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
#endregion
using ScintillaNET;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

public static class CSharpLexer
{
    private static List<char> NumberTypes = new List<char>
    {
        '0', '1', '2', '3', '4', '5', '6','7', '8', '9',
        'a', 'b', 'c', 'd', 'e', 'f', 'x',
        'A', 'B', 'C', 'D', 'E', 'F', '-', '.'
    };

    private static List<char> EscapeSequences = new List<char>
    {
        '\'', '"', '\\', '0', 'a', 'b', 'f',
        'n', 'r', 't', 'v'
    };

    private static List<string> Operators = new List<string>
    {
        "<<", ">>", "<=", ">=", "+=", "-=", "*=", "&=",
        "|=", "!=", "^=", "->", "??", "=>", "++", "--",
        "==", "&&", "||", "+", "-", "*", "&", "!", "|",
        "^", "~", "=", "<", ">"
    };

    private static List<char> OperatorStragglers = new List<char>
    {
        '*', '&', '?', '-', '!'
    };

    private static List<char> IdentifierMarkers = new List<char>
    {
        '<', '[', '.'
    };

    private static List<string> FullDocument = new List<string>
    {
        "*", "/", "{", "}"
    };

    //Few of these might need renamed
    public const int StyleDefault = 0,
                     StyleKeyword = 1,
                  StyleIdentifier = 2,
                      StyleNumber = 3,
                      StyleString = 4,
                     StyleComment = 5,
                   StyleProcedure = 6,
                  StyleContextual = 7,
                    StyleVerbatim = 8,
                StylePreprocessor = 9,
              StyleEscapeSequence = 10,
                    StyleOperator = 11,
                      StyleBraces = 12,
                       StyleError = 13,
                        StyleUser = 14,
          StyleProcedureContainer = 15,
          StyleContainerProcedure = 16,
             StyleMultiIdentifier = 17,
                StyleQuotedString = 18;

    private const int STATE_UNKNOWN = 0,
                   STATE_IDENTIFIER = 1,
                       STATE_NUMBER = 2,
                       STATE_STRING = 3,
            STATE_MULTILINE_COMMENT = 4,
                 STATE_PREPROCESSOR = 5,
                     STATE_OPERATOR = 6,
             STATE_MULTI_IDENTIFIER = 7;


    public static List<string> KEYWORDS, //Primary keywords
                    CONTEXTUAL_KEYWORDS, //Secondary keywords
                         MULTI_KEYWORDS, //Multi-string keywords
                          USER_KEYWORDS; //User-defined keywords

    private static bool IMPORTANT_KEY_DELETED = false;

    private static Dictionary<int, int> MULTI_DICT; //Description : KeyValuePair<Postion, Length>

    private static void RegSearchForWordsIn(string inSearchText, int inPositionOffset = 0)
    {
        MULTI_DICT = new Dictionary<int, int>();

        for (int i = 0; i < MULTI_KEYWORDS.Count; i++)
        {
            string Pattern = $@"\b{MULTI_KEYWORDS[i]}\b";

            Regex rx = new Regex(Pattern, RegexOptions.None);
            MatchCollection mc = rx.Matches(inSearchText);

            if (mc.Count > 0) { foreach (Match m in mc) { MULTI_DICT.Add((inPositionOffset + m.Index), m.Length); } }
        }
    }

    public static void SetKeyWords(string inKeywords = "", string inContextualKeywords = "", string inUserKeywords = "", string inMultiStringKeywords = "", bool AutoFillContextual = false)
    {
        IEnumerable<string> AssemblyTypes() => typeof(string).Assembly.GetTypes()
                                                                      .Where(t => t.IsPublic && t.IsVisible)
                                                                      .Select(t => new { t.Name, Length = t.Name.IndexOf('`') })
                                                                      .Select(x => x.Length == -1 ? x.Name : x.Name.Substring(0, x.Length))
                                                                      .Distinct();

        //Wasn't going to do it this way.  But, I guess, this is more "Flexible".
        CONTEXTUAL_KEYWORDS = new List<string>();

        MULTI_KEYWORDS = new List<string>();

        if (inKeywords != "") { KEYWORDS = new List<string>(inKeywords.Split(' ')); }
        if (inContextualKeywords != "") { CONTEXTUAL_KEYWORDS.AddRange(inContextualKeywords.Split(' ').ToList()); }
        if (inUserKeywords != "") { USER_KEYWORDS = new List<string>(inUserKeywords.Split(' ')); }

        if (AutoFillContextual) { CONTEXTUAL_KEYWORDS.AddRange(AssemblyTypes()); }

        if (inMultiStringKeywords != "") { MULTI_KEYWORDS = new List<string>(inMultiStringKeywords.Split(',').ToList()); }
    }

    public static void Init_Lexer(Scintilla inScintilla)
    {
        inScintilla.CharAdded += (s, ae) => { IMPORTANT_KEY_DELETED = FullDocument.Contains(ae.Char.ToString()); };

        //PLEASE NOTE I'M ALLOWING THIS TO BE CALLED MULTIPLE TIMES.  JUST IN CASE, IT NEEDS TO BE USED ON MULTIPLE SCINTILLA CONTROLS.
        inScintilla.Delete += (s, de) => { IMPORTANT_KEY_DELETED = (FullDocument.Contains(de.Text) || de.Text == @""""); };

        inScintilla.StyleNeeded += (s, se) =>
        {
            Style(inScintilla, inScintilla.GetEndStyled(), se.Position, IMPORTANT_KEY_DELETED);

            IMPORTANT_KEY_DELETED = false;
        };
    }

    public static void Style(Scintilla scintilla, int startPos, int endPos, bool fullDoc = false)
    {
        startPos = (fullDoc ? 0 : scintilla.Lines[scintilla.LineFromPosition(startPos)].Position);
        endPos = (fullDoc ? (scintilla.Lines[scintilla.Lines.Count].EndPosition - 1) : endPos);

        int style, length = 0, state = STATE_UNKNOWN;

        bool SINGLE_LINE_COMMENT,
              MULTI_LINE_COMMENT,
                VERBATIM = false,
             PARENTHESIS = false,
           QUOTED_STRING = false,
                         DBL_OPR;

        char c = '\0', d = '\0';

        void ClearState() { length = state = STATE_UNKNOWN; }

        void DefaultStyle() => scintilla.SetStyling(1, StyleDefault);

        int StyleUntilEndOfLine(int inPosition, int inStyle)
        {
            int len = (scintilla.Lines[scintilla.LineFromPosition(inPosition)].EndPosition - inPosition);

            scintilla.SetStyling(len, inStyle);

            return --len; //We return the length, cause we'll have to adjust the startPos.
        }

        bool ContainsUsingStatement(int inPosition) => (scintilla.GetTextRange(scintilla.Lines[scintilla.LineFromPosition(inPosition)].Position, 5)).Contains("using");

        if (MULTI_KEYWORDS.Count > 0) { RegSearchForWordsIn(scintilla.GetTextRange(startPos, (endPos - startPos)), startPos); }

        scintilla.StartStyling(startPos);
        {
            for (; startPos < endPos; startPos++)
            {                
                c = scintilla.Text[startPos];

                if ((state == STATE_UNKNOWN) && c == ' ') { DefaultStyle(); continue; } //Better than allowing it to set all the booleans and trickle down the if/else-if structure?

                d = (((startPos + 1) < scintilla.Text.Length) ? scintilla.Text[startPos + 1] : '\0'); //d = (char)scintilla.GetCharAt(startPos + 1);

                if (state == STATE_UNKNOWN)
                {
                    bool bFormattedVerbatim = ((c == '$') && (d == '@')),
                                 bFormatted = ((c == '$') && ((d == '"'))),
                               bNegativeNum = ((c == '-') && (char.IsDigit(d))),
                                  bFraction = ((c == '.') && (char.IsDigit(d))),
                                    bString = (c == '"'),
                              bQuotedString = (c == '\'');

                    VERBATIM = ((c == '@') && (d == '"'));

                    SINGLE_LINE_COMMENT = ((c == '/') && (d == '/'));
                    MULTI_LINE_COMMENT = ((c == '/') && (d == '*'));

                    //I always want braces to be highlighted 
                    if ((c == '{') || (c == '}'))
                    {
                        scintilla.SetStyling(1, ((scintilla.BraceMatch(startPos) > -1) ? StyleBraces : StyleError));
                    }
                    else if (char.IsLetter(c)) //Indentifier - Keywords, procedures, etc ...
                    {
                        state = (((MULTI_KEYWORDS.Count > 0) && MULTI_DICT.ContainsKey(startPos)) ? STATE_MULTI_IDENTIFIER : STATE_IDENTIFIER);
                    }
                    else if (bString || VERBATIM || bFormatted || bFormattedVerbatim || bQuotedString) //String
                    {
                        int len = ((VERBATIM || bFormatted || bFormattedVerbatim) ? ((bFormattedVerbatim) ? 3 : 2) : 1);

                        QUOTED_STRING = bQuotedString;

                        scintilla.SetStyling(len, (!VERBATIM ? (QUOTED_STRING ? StyleQuotedString : StyleString) : StyleVerbatim));

                        startPos += (len - 1);

                        state = STATE_STRING;
                    }
                    else if (char.IsDigit(c) || bNegativeNum || bFraction) //Number
                    {
                        state = STATE_NUMBER;
                    }
                    else if (SINGLE_LINE_COMMENT || MULTI_LINE_COMMENT) //Comment
                    {
                        if (SINGLE_LINE_COMMENT)
                        {
                            startPos += StyleUntilEndOfLine(startPos, StyleComment);
                        }
                        else
                        {
                            scintilla.SetStyling(2, StyleComment);

                            startPos += 2;

                            state = STATE_MULTILINE_COMMENT;
                        }
                    }
                    else if (c == '#') //Preprocessor
                    {
                        startPos += StyleUntilEndOfLine(startPos, StylePreprocessor); //continue;
                    }
                    else if (
                                (char.IsSymbol(c) || OperatorStragglers.Contains(c)) && (Operators.Contains($"{c}" +
                                ((DBL_OPR = (char.IsSymbol(d) || OperatorStragglers.Contains(d))) ? $"{d}" : "")))
                            ) //Operators
                    {
                        scintilla.SetStyling((DBL_OPR ? 2 : 1), StyleOperator);

                        startPos += (DBL_OPR ? 1 : 0);
                    }
                    else { DefaultStyle(); }

                    continue;
                }

                length++;

                switch (state)
                {
                    case STATE_IDENTIFIER:
                        string identifier = scintilla.GetWordFromPosition(startPos);

                        style = StyleIdentifier;

                        int s = startPos;

                        startPos += (identifier.Length - 2);
                        
                        d = (((startPos + 1) < scintilla.Text.Length) ? scintilla.Text[startPos + 1] : '\0'); //d = (char)scintilla.GetCharAt(startPos + 1);

                        bool OPEN_PAREN = (d == '(');

                        if (!OPEN_PAREN && KEYWORDS.Contains(identifier)) { style = StyleKeyword; } //Keywords
                        else if (!OPEN_PAREN && CONTEXTUAL_KEYWORDS.Contains(identifier)) { style = StyleContextual; } //Contextual Keywords
                        else if (!OPEN_PAREN && USER_KEYWORDS.Contains(identifier)) { style = StyleUser; } //User Keywords
                        else if (OPEN_PAREN) { style = StyleProcedure; } //Procedures
                        else if (IdentifierMarkers.Contains(d) && !ContainsUsingStatement(startPos)) { style = StyleProcedureContainer; } //Procedure Containers "classes?"
                        else if (((char)scintilla.GetCharAt(s - 2) == '.') && !ContainsUsingStatement(s)) { style = StyleContainerProcedure; } //Container "procedures"

                        ClearState();

                        scintilla.SetStyling(identifier.Length, style);

                        break;
                    case STATE_MULTI_IDENTIFIER:
                        int value;

                        MULTI_DICT.TryGetValue((startPos - 1), out value);

                        startPos += (value - 2);

                        ClearState();

                        scintilla.SetStyling(value, StyleMultiIdentifier);

                        break;

                    case STATE_NUMBER:
                        if (!NumberTypes.Contains(c))
                        {
                            scintilla.SetStyling(length, StyleNumber);

                            ClearState();

                            startPos--;
                        }

                        break;

                    case STATE_STRING:
                        style = (VERBATIM ? StyleVerbatim : (QUOTED_STRING ? StyleQuotedString : StyleString));

                        if (PARENTHESIS || ((c == '{') || (d == '}'))) //Formatted strings that are using braces
                        {
                            if (c == '{') { PARENTHESIS = true; }
                            if (c == '}') { PARENTHESIS = false; }
                        }
                        else if (QUOTED_STRING)
                        {
                            if (c == '\'') //End of our Quoted string?
                            {
                                QUOTED_STRING = false;

                                scintilla.SetStyling(length, style);
                                ClearState();
                            }
                            else if (c == '\\')
                            {
                                length++; startPos++;
                            }
                        }
                        else if (VERBATIM && ((c == '"') && (d == '"'))) //Skip over embedded quotation marks 
                        {
                            length++; startPos++;
                        }
                        else if (c == '"') //End of our string?
                        {
                            length = ((length < 1) ? 1 : length);

                            scintilla.SetStyling(length, style);

                            ClearState();
                        }
                        else
                        {
                            if (!QUOTED_STRING && (c == '\\') && EscapeSequences.Contains(d)) //Escape Sequences
                            {
                                length += ((d == '\\') ? 0 : -1);

                                scintilla.SetStyling(length, style);
                                {
                                    startPos++; length = 0;
                                }
                                scintilla.SetStyling(2, StyleEscapeSequence);
                            }
                        }

                        break;

                    case STATE_MULTILINE_COMMENT:
                        if ((c == '*') && (d == '/'))
                        {
                            length += 2;

                            scintilla.SetStyling(length, StyleComment);

                            ClearState();

                            startPos++;
                        }

                        break;
                }
            }
        }
    }
}