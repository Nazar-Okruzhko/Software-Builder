// SoftwareBuilder – Visual Python Programming Environment
// Single-file .NET 6.0 Windows Forms Application
// Build: dotnet new console -n SoftwareBuilder -f net6.0-windows
// Replace Program.cs with this file, then: dotnet run
// Assets: base\icons\*.png, base\fonts\*.ttf, Icon1.ico next to the executable (optional)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
 
namespace PyBlocksViewer
{
    // =========================================================================
    // Entry point
    // =========================================================================
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration_Initialize();
            Application.Run(new MainForm());
        }
 
        // Minimal stand-in for the WinForms-generated ApplicationConfiguration
        // initializer, kept explicit so this really is a single file.
        private static void ApplicationConfiguration_Initialize()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }
    }
 
    // =========================================================================
    // Parse tree: plain text nodes and bracketed input nodes
    // =========================================================================
 
    /// <summary>Base class for a fragment of a block's inline content.</summary>
    internal abstract class Node { }
 
    /// <summary>Literal label text drawn directly on the block's face.</summary>
    internal sealed class TextNode : Node
    {
        public string Text;
        public TextNode(string text) => Text = text;
    }
 
    /// <summary>
    /// A bracketed input: '(' round reporter/number slot, '[' square dropdown
    /// field, '&lt;' angle boolean slot. May itself contain nested Nodes,
    /// mirroring scratchblocks' own grammar (reporters can nest reporters).
    /// NOTE: this was scratchblocks-syntax machinery; the Python tokenizer
    /// uses PillNode below instead, but this stays available/dormant.
    /// </summary>
    internal sealed class BracketNode : Node
    {
        public char Open; // '(' , '[' , or '<'
        public List<Node> Children;
        public BracketNode(char open, List<Node> children)
        {
            Open = open;
            Children = children;
        }
    }
 
    internal enum PillKind
    {
        Literal,  // a string or number literal - drawn as a plain white input field
        Variable, // a bare identifier that isn't a keyword or a call name - drawn as a small colored "variable" block
    }
 
    /// <summary>
    /// A small rounded pill embedded inline in a line of code: either a
    /// literal (string/number - white input-field look) or a variable
    /// reference (colored rounded block, like a Scratch variable reporter).
    /// </summary>
    internal sealed class PillNode : Node
    {
        public string Text;
        public PillKind Kind;
        public PillNode(string text, PillKind kind)
        {
            Text = text;
            Kind = kind;
        }
    }
 
    // =========================================================================
    // Block shapes for a stack line
    // =========================================================================
    internal enum BlockShape
    {
        Hat,
        Command,
        Cap,
        CBlock, // may have any number of chained continuations (elif/else, except/finally, ...)
    }
 
    /// <summary>One block in a script stack (possibly containing nested stacks / "mouths").</summary>
    internal sealed class BlockNode
    {
        public List<Node> Header = new();
 
        // Chained continuation headers, e.g. for "if / elif / elif / else" or
        // "try / except / except / finally": ContinuationHeaders.Count == Mouths.Count - 1.
        public List<List<Node>> ContinuationHeaders = new();
 
        public string Category = "grey";
        public BlockShape Shape = BlockShape.Command;
        public Color? TextColorOverride; // used for comment lines (dark text instead of the usual white)
        public bool HatTop; // true for class/def: arched top ("starter block"), no puzzle notch expected from above
 
        // Raw (0-based) line indices into the source textbox this block came
        // from. SourceLine is this block's own header line; EndLine is the
        // LAST line anywhere in its subtree (its own line if it's a simple
        // command, otherwise the last line of its last mouth/continuation).
        // Used to translate a drag-drop target back into a real text edit.
        public int SourceLine = -1;
        public int EndLine = -1;
 
        // Mouths[0] is the body directly under Header; Mouths[k+1] is the body
        // under ContinuationHeaders[k].
        public List<List<BlockNode>> Mouths = new();
 
        // ---- layout cache, populated by Renderer.Measure ----
        public float W, H;
        public List<float> MouthW = new();
        public List<float> MouthH = new();
        public float HeaderH, FooterH;
        public List<float> ContinuationHeaderH = new();
 
        // Per-bar widths for a CBlock: BarW[0] is the header bar's own width
        // (sized to fit its own text AND Mouths[0]); BarW[k] for k>0 is
        // ContinuationHeaders[k-1]'s own bar width (sized to fit its own text
        // AND Mouths[k]). FooterW is the footer bar's width (sized to fit
        // Mouths[last] only, since the footer has no text of its own). W is
        // the max of all of these - the bounding width other blocks need to
        // know about for stacking - but each bar is drawn at its OWN width,
        // not stretched out to W, so the block's outline can step narrower
        // or wider between bars to hug whatever is actually inside it.
        public List<float> BarW = new();
        public float FooterW;
    }
 
    // =========================================================================
    // Parser: turns real, indented Python source into a block-stack tree.
    // Lines ending in ":" (ignoring trailing comments) open a nested "mouth";
    // elif/else/except/finally chain onto the same block as continuations.
    // =========================================================================
    internal static class ScriptParser
    {
        public static List<BlockNode> Parse(string source)
        {
            var lines = Tokenize(source);
            int i = 0;
            int rootIndent = lines.Count > 0 ? lines[0].indent : 0;
            var result = ParseBlock(lines, ref i, rootIndent);
 
            // A shebang ("#!/usr/bin/env python3") is the script's own
            // starter line - give it a hat, not the grey comment styling.
            if (result.Count > 0 && lines.Count > 0 && lines[0].text.TrimStart().StartsWith("#!"))
            {
                result[0].Category = "system";
                result[0].TextColorOverride = null;
                result[0].Shape = BlockShape.Hat;
            }
            return result;
        }
 
        private static List<(int raw, int indent, string text)> Tokenize(string source)
        {
            var result = new List<(int, int, string)>();
            var rawLines = (source ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            for (int rawIdx = 0; rawIdx < rawLines.Length; rawIdx++)
            {
                string raw = rawLines[rawIdx];
                if (raw.Trim().Length == 0) continue; // blank lines don't get a block
                string expanded = raw.Replace("\t", "    ");
                int indent = expanded.Length - expanded.TrimStart(' ').Length;
                result.Add((rawIdx, indent, expanded.TrimEnd()));
            }
            return result;
        }
 
        private static List<BlockNode> ParseBlock(List<(int raw, int indent, string text)> lines, ref int i, int blockIndent)
        {
            var result = new List<BlockNode>();
            while (i < lines.Count && lines[i].indent >= blockIndent)
            {
                if (lines[i].indent > blockIndent) blockIndent = lines[i].indent; // tolerate ragged indentation
 
                var (raw, indent, text) = lines[i];
                i++;
                string trimmed = text.TrimStart();
                var block = BuildLineNode(trimmed);
                block.SourceLine = raw;
                block.EndLine = raw;
 
                if (LineOpensBlock(trimmed))
                {
                    string codeOnly = StripComment(trimmed).TrimEnd();
                    block.HatTop = codeOnly.StartsWith("class ") || codeOnly.StartsWith("def ");
 
                    int childIndent = (i < lines.Count && lines[i].indent > indent) ? lines[i].indent : indent + 4;
                    block.Mouths.Add(ParseBlock(lines, ref i, childIndent));
                    block.Shape = BlockShape.CBlock;
 
                    while (i < lines.Count && lines[i].indent == indent && IsContinuationKeyword(lines[i].text.TrimStart()))
                    {
                        int contRaw = lines[i].raw;
                        string contText = lines[i].text.TrimStart();
                        i++;
                        block.ContinuationHeaders.Add(BuildHeaderNodes(contText));
                        block.EndLine = contRaw;
                        int contChildIndent = (i < lines.Count && lines[i].indent > indent) ? lines[i].indent : indent + 4;
                        block.Mouths.Add(ParseBlock(lines, ref i, contChildIndent));
                    }
 
                    // EndLine is the last raw line actually consumed anywhere
                    // in this block's subtree (last mouth's last line, or its
                    // own header/continuation line if every mouth is empty).
                    if (i > 0) block.EndLine = Math.Max(block.EndLine, lines[i - 1].raw);
                }
 
                result.Add(block);
            }
            return result;
        }
 
        private static BlockNode BuildLineNode(string trimmed)
        {
            var block = new BlockNode { Header = BuildHeaderNodes(trimmed) };
            string codePart = StripComment(trimmed).TrimEnd();
 
            if (trimmed.StartsWith("#"))
            {
                block.Category = "comment";
                block.TextColorOverride = Color.FromArgb(90, 90, 90);
            }
            else
            {
                block.Category = PyClassifier.Classify(codePart.Length > 0 ? codePart : trimmed);
            }
            block.Shape = BlockShape.Command; // upgraded to CBlock by the caller if it opens a suite
            return block;
        }
 
        /// <summary>
        /// Builds the inline node list for one line: comment-only lines stay
        /// as a single plain TextNode, everything else gets tokenized into
        /// plain code text / literal input pills / variable pills, with any
        /// trailing "# comment" re-appended as plain text afterwards.
        /// </summary>
        private static List<Node> BuildHeaderNodes(string trimmed)
        {
            if (trimmed.TrimStart().StartsWith("#"))
                return new List<Node> { new TextNode(trimmed) };
 
            string code = StripComment(trimmed);
            string rest = trimmed.Substring(code.Length); // whitespace + "#..." if any, else ""
 
            var nodes = TokenizeCodeLine(code.TrimEnd());
            string trailingWs = code.Substring(code.TrimEnd().Length); // whitespace eaten by TrimEnd, put back before the comment
            string comment = trailingWs + rest;
            if (comment.Length > 0) nodes.Add(new TextNode(comment));
            return nodes;
        }
 
        private static bool IsContinuationKeyword(string trimmed)
        {
            return trimmed.StartsWith("elif ") || trimmed.StartsWith("elif(")
                || trimmed == "else:" || trimmed.StartsWith("else:")
                || trimmed.StartsWith("except") || trimmed.StartsWith("finally");
        }
 
        private static bool LineOpensBlock(string trimmed)
        {
            if (trimmed.StartsWith("#")) return false;
            string code = StripComment(trimmed).TrimEnd();
            return code.EndsWith(":");
        }
 
        /// <summary>Strips a trailing '#' comment, ignoring '#' characters inside quotes.</summary>
        private static string StripComment(string s)
        {
            bool inSingle = false, inDouble = false;
            for (int idx = 0; idx < s.Length; idx++)
            {
                char c = s[idx];
                if (c == '\'' && !inDouble) inSingle = !inSingle;
                else if (c == '"' && !inSingle) inDouble = !inDouble;
                else if (c == '#' && !inSingle && !inDouble) return s.Substring(0, idx);
            }
            return s;
        }
 
        // ---- inline tokenizer: turns one line of Python code into plain
        // text / literal pills / variable pills ------------------------------
 
        private static readonly HashSet<string> PyKeywords = new(StringComparer.Ordinal)
        {
            "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class",
            "continue", "def", "del", "elif", "else", "except", "finally", "for", "from", "global",
            "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise",
            "return", "try", "while", "with", "yield", "self", "cls", "match", "case",
        };
 
        private static readonly Regex TokenPattern = new Regex(
            "(?<str>(?:[fFrRbB]{0,2})(?:\"\"\"(?:[^\\\\]|\\\\.)*?\"\"\"|'''(?:[^\\\\]|\\\\.)*?'''|\"(?:[^\"\\\\]|\\\\.)*\"|'(?:[^'\\\\]|\\\\.)*'))" +
            "|(?<num>(?<![\\w.])\\d[\\d_]*\\.?[\\d_]*(?:[eE][+-]?\\d+)?(?![\\w.]))" +
            "|(?<id>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);
 
        private static List<Node> TokenizeCodeLine(string code)
        {
            var nodes = new List<Node>();
            var plain = new StringBuilder();
 
            void FlushPlain()
            {
                if (plain.Length > 0) { nodes.Add(new TextNode(plain.ToString())); plain.Clear(); }
            }
 
            int pos = 0;
            foreach (Match m in TokenPattern.Matches(code))
            {
                if (m.Index > pos) plain.Append(code, pos, m.Index - pos);
 
                if (m.Groups["str"].Success || m.Groups["num"].Success)
                {
                    FlushPlain();
                    nodes.Add(new PillNode(m.Value, PillKind.Literal));
                }
                else // identifier
                {
                    string word = m.Value;
                    bool isKeyword = PyKeywords.Contains(word);
                    bool followedByCall = m.Index + m.Length < code.Length && code[m.Index + m.Length] == '(';
                    if (isKeyword || followedByCall)
                    {
                        plain.Append(word);
                    }
                    else
                    {
                        FlushPlain();
                        nodes.Add(new PillNode(word, PillKind.Variable));
                    }
                }
 
                pos = m.Index + m.Length;
            }
 
            if (pos < code.Length) plain.Append(code, pos, code.Length - pos);
            FlushPlain();
 
            if (nodes.Count == 0) nodes.Add(new TextNode(code)); // degenerate/empty line - keep something drawable
            return nodes;
        }
 
 
        // tokenize bracketed expressions again. ----
        public static string HeaderKeywordText(List<Node> nodes)
        {
            var sb = new StringBuilder();
            foreach (var n in nodes)
            {
                if (n is TextNode t) sb.Append(t.Text);
                else sb.Append(' ');
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim().ToLowerInvariant();
        }
 
        public static List<Node> ParseNodes(string s)
        {
            int i = 0;
            return ParseUntil(s, ref i, '\0');
        }
 
        private static List<Node> ParseUntil(string s, ref int i, char closeChar)
        {
            var nodes = new List<Node>();
            var sb = new StringBuilder();
 
            void Flush()
            {
                if (sb.Length > 0) { nodes.Add(new TextNode(sb.ToString())); sb.Clear(); }
            }
 
            while (i < s.Length)
            {
                char c = s[i];
                if (closeChar != '\0' && c == closeChar) { i++; Flush(); return nodes; }
 
                if (c == '(' || c == '[' || c == '<')
                {
                    Flush();
                    char open = c;
                    char close = open == '(' ? ')' : (open == '[' ? ']' : '>');
                    i++;
                    var children = ParseUntil(s, ref i, close);
                    nodes.Add(new BracketNode(open, children));
                    continue;
                }
 
                sb.Append(c);
                i++;
            }
            Flush();
            return nodes;
        }
    }
 
    // =========================================================================
    // Category classification + palette, driven by the LVL1-LVL12 Python
    // taxonomy: FLOW / VARIABLES / FUNCTIONS / OBJECTS / DATA / TEXT / MATH /
    // FILES / UI / TIME / SYSTEM / ADVANCED (colors match the "FINAL CATEGORY
    // TABLE" you provided; OBJECTS wasn't in that table so it gets its own
    // distinct color rather than being folded into FUNCTIONS).
    // =========================================================================
    internal static class PyClassifier
    {
        public static readonly Dictionary<string, Color> CategoryColors = new()
        {
            ["flow"] = ColorTranslator.FromHtml("#E1A91A"),      // Yellow
            ["variables"] = ColorTranslator.FromHtml("#4A6CD4"), // Blue
            ["functions"] = ColorTranslator.FromHtml("#8E44AD"), // Purple
            ["objects"] = ColorTranslator.FromHtml("#C2185B"),   // (not in table) deep pink/magenta
            ["data"] = ColorTranslator.FromHtml("#27AE60"),      // Green
            ["text"] = ColorTranslator.FromHtml("#E07B1A"),      // Orange
            ["math"] = ColorTranslator.FromHtml("#5B7C99"),      // Gray-blue
            ["files"] = ColorTranslator.FromHtml("#8D6748"),     // Brown
            ["ui"] = ColorTranslator.FromHtml("#17A2B8"),        // Cyan
            ["time"] = ColorTranslator.FromHtml("#009688"),      // Teal
            ["system"] = ColorTranslator.FromHtml("#555555"),    // Dark gray
            ["advanced"] = ColorTranslator.FromHtml("#4A235A"),  // Dark purple
            ["comment"] = ColorTranslator.FromHtml("#E8E8E8"),   // pale gray, dark text
            ["grey"] = ColorTranslator.FromHtml("#8B8B8B"),      // fallback / unrecognized
        };
 
        // Ordered by priority (most specific / least ambiguous first), NOT by
        // length - deliberately front-loads structural keywords, imports, and
        // dotted method-call patterns before the generic assignment fallback.
        // wordStart=true: must match at the very start of the (trimmed) line.
        // wordStart=false: matched anywhere in the line (dotted calls, literals).
        private static readonly (string pat, string cat, bool wordStart)[] Rules =
        {
            // ---- FLOW (LVL1: execution, conditions, loops, exceptions) ----
            ("pass", "flow", true),
            ("return", "flow", true),
            ("yield", "flow", true),
            ("if ", "flow", true), ("if(", "flow", true),
            ("elif", "flow", true),
            ("else", "flow", true),
            ("match ", "flow", true),
            ("case ", "flow", true),
            ("for ", "flow", true),
            ("while ", "flow", true),
            ("break", "flow", true),
            ("continue", "flow", true),
            ("try", "flow", true),
            ("except", "flow", true),
            ("finally", "flow", true),
            ("raise", "flow", true),
            ("with ", "flow", true),
            ("range(", "flow", false),
            ("enumerate(", "flow", false),
            ("zip(", "flow", false),
            ("reversed(", "flow", false),
 
            // ---- FUNCTIONS (LVL3) ----
            ("def ", "functions", true),
            ("lambda", "functions", false),
            ("global ", "functions", true),
            ("nonlocal ", "functions", true),
            ("*args", "functions", false),
            ("**kwargs", "functions", false),
 
            // ---- OBJECTS (LVL4 - not in the summary table, own color) ----
            ("class ", "objects", true),
            ("self.", "objects", false),
            ("__init__", "objects", false),
            ("super(", "objects", false),
 
            // ---- ADVANCED (LVL12) ----
            ("import ", "advanced", true),
            ("from ", "advanced", true),
            ("async ", "advanced", true),
            ("await ", "advanced", false),
            ("Optional[", "advanced", false),
            ("List[", "advanced", false),
            ("hasattr(", "advanced", false),
            ("gc.", "advanced", false),
 
            // ---- FILES (LVL8) - checked before TEXT so os.path.join wins over .join(  ----
            ("os.path.", "files", false),
            ("os.remove", "files", false),
            ("os.rename", "files", false),
            ("os.listdir", "files", false),
            ("open(", "files", false),
            (".readline(", "files", false),
            (".readbytes(", "files", false),
            (".writebytes(", "files", false),
            (".read(", "files", false),
            (".write(", "files", false),
            ("\"rb\"", "files", false), ("'rb'", "files", false),
            ("\"wb\"", "files", false), ("'wb'", "files", false),
 
            // ---- TIME (LVL10) ----
            ("time.sleep", "time", false),
            ("sleep(", "time", false),
            ("datetime.now", "time", false),
            (".now(", "time", false),
            ("timestamp(", "time", false),
            ("strftime(", "time", false),
 
            // ---- SYSTEM (LVL11) ----
            ("sys.exit(", "system", false),
            ("sys.argv", "system", false),
            ("sys.", "system", false),
            ("os.environ", "system", false),
            ("platform.", "system", false),
            ("clipboard", "system", false),
 
            // ---- UI (LVL9) ----
            ("tk.", "ui", false),
            ("Button(", "ui", false),
            ("Label(", "ui", false),
            ("Entry(", "ui", false),
            ("Checkbutton(", "ui", false),
            ("Scale(", "ui", false),
            ("mainloop(", "ui", false),
            (".grid(", "ui", false),
            (".pack(", "ui", false),
            ("bind(", "ui", false),
 
            // ---- DATA (LVL5) ----
            (".append(", "data", false),
            (".extend(", "data", false),
            (".insert(", "data", false),
            (".remove(", "data", false),
            (".pop(", "data", false),
            (".sort(", "data", false),
            (".reverse(", "data", false),
            (".keys(", "data", false),
            (".values(", "data", false),
            (".items(", "data", false),
            (".get(", "data", false),
            (".update(", "data", false),
            (".union(", "data", false),
            (".intersection(", "data", false),
            (".add(", "data", false),
 
            // ---- TEXT (LVL6) ----
            ("f\"", "text", false), ("f'", "text", false),
            ("print(", "text", false),
            (".upper(", "text", false),
            (".lower(", "text", false),
            (".strip(", "text", false),
            (".replace(", "text", false),
            (".split(", "text", false),
            (".join(", "text", false),
            (".find(", "text", false),
            (".index(", "text", false),
            (".startswith(", "text", false),
            (".endswith(", "text", false),
            (".format(", "text", false),
 
            // ---- MATH (LVL7) ----
            ("math.", "math", false),
            ("abs(", "math", false),
            ("round(", "math", false),
            ("min(", "math", false),
            ("max(", "math", false),
            ("sum(", "math", false),
            ("randint(", "math", false),
            ("random.random(", "math", false),
            ("choice(", "math", false),
            ("shuffle(", "math", false),
            ("sqrt(", "math", false),
 
            // ---- VARIABLES (LVL2) - checked last among positives, since
            //      True/False/None/int()/str() etc. are common inside other
            //      categories' lines too, and we want those more specific
            //      rules to win first. ----
            ("True", "variables", false),
            ("False", "variables", false),
            ("None", "variables", false),
            ("int(", "variables", false),
            ("float(", "variables", false),
            ("str(", "variables", false),
            ("bool(", "variables", false),
        };
 
        public static string Classify(string codeLine)
        {
            string trimmed = codeLine.TrimStart();
            if (trimmed.StartsWith("@")) return "functions"; // decorator
 
            foreach (var (pat, cat, wordStart) in Rules)
            {
                if (wordStart)
                {
                    if (Regex.IsMatch(trimmed, $@"^{Regex.Escape(pat.TrimEnd())}(\b|\()")) return cat;
                }
                else
                {
                    if (trimmed.Contains(pat)) return cat;
                }
            }
 
            // Fallback: anything that looks like an assignment is VARIABLES.
            if (trimmed.Contains("+=") || trimmed.Contains("-=") || trimmed.Contains("*=") || trimmed.Contains("/="))
                return "variables";
            if (Regex.IsMatch(trimmed, @"(?<![=!<>])=(?!=)"))
                return "variables";
 
            return "grey";
        }
 
        /// <summary>
        /// Not used by the Python parser (which never emits BracketNode), kept
        /// only so Renderer's dormant inline-reporter/boolean drawing code
        /// (left over from Scratch mode, harmless dead code for now) compiles.
        /// </summary>
        public static string? ClassifyInline(string _) => null;
    }
 
    // =========================================================================
    // Palette: the draggable "stencils" shown in the left panel, grouped by
    // the same LVL1-LVL12 taxonomy as the classifier above. Dragging one onto
    // the canvas inserts its Template text into the source at the drop point.
    // =========================================================================
    internal sealed class PaletteItem
    {
        public string Category;
        public string Label;      // short name shown on the stencil
        public string Template;   // the literal Python line(s) inserted (first line only if RequiresBody)
        public bool RequiresBody; // true for compound statements (if/for/def/...) - a "    pass" placeholder body is inserted under it
        public PaletteItem(string category, string label, string template, bool requiresBody = false)
        {
            Category = category;
            Label = label;
            Template = template;
            RequiresBody = requiresBody;
        }
    }
 
    internal static class PaletteCatalog
    {
        public static readonly (string key, string display)[] Categories =
        {
            ("flow", "Flow"), ("variables", "Variables"), ("functions", "Functions"), ("objects", "Objects"),
            ("data", "Data"), ("text", "Text"), ("math", "Math"), ("files", "Files"),
            ("ui", "UI"), ("time", "Time"), ("system", "System"), ("advanced", "Advanced"),
        };
 
        public static readonly List<PaletteItem> Items = new()
        {
            // ---- FLOW ----
            new("flow", "if", "if condition:", requiresBody: true),
            new("flow", "if / else", "if condition:", requiresBody: true), // else is added as a second drop; keep simple for now
            new("flow", "for", "for item in range(10):", requiresBody: true),
            new("flow", "while", "while condition:", requiresBody: true),
            new("flow", "try / except", "try:", requiresBody: true),
            new("flow", "break", "break"),
            new("flow", "continue", "continue"),
            new("flow", "return", "return value"),
            new("flow", "pass", "pass"),
 
            // ---- VARIABLES ----
            new("variables", "assign", "x = 0"),
            new("variables", "increment", "x += 1"),
            new("variables", "True", "True"),
            new("variables", "False", "False"),
            new("variables", "None", "None"),
 
            // ---- FUNCTIONS ----
            new("functions", "def", "def my_function():", requiresBody: true),
            new("functions", "lambda", "square = lambda x: x * x"),
            new("functions", "@decorator", "@staticmethod"),
 
            // ---- OBJECTS ----
            new("objects", "class", "class MyClass:", requiresBody: true),
            new("objects", "__init__", "def __init__(self):", requiresBody: true),
            new("objects", "self.attr", "self.value = 0"),
            new("objects", "super()", "super().__init__()"),
 
            // ---- DATA ----
            new("data", "list.append", "my_list.append(item)"),
            new("data", "list.pop", "my_list.pop()"),
            new("data", "dict[key]", "my_dict[key] = value"),
            new("data", "dict.get", "my_dict.get(key)"),
            new("data", "set.add", "my_set.add(item)"),
 
            // ---- TEXT ----
            new("text", "f-string", "text = f\"value: {x}\""),
            new("text", "split", "parts = text.split(\",\")"),
            new("text", "strip", "text = text.strip()"),
            new("text", "join", "text = \", \".join(parts)"),
 
            // ---- MATH ----
            new("math", "arithmetic", "result = a + b"),
            new("math", "abs", "abs(x)"),
            new("math", "round", "round(x, 2)"),
            new("math", "random", "random.randint(1, 10)"),
 
            // ---- FILES ----
            new("files", "open (with)", "with open(\"file.txt\") as f:", requiresBody: true),
            new("files", "write", "f.write(text)"),
            new("files", "read", "text = f.read()"),
            new("files", "os.path.exists", "os.path.exists(path)"),
 
            // ---- UI ----
            new("ui", "button", "button = Button()"),
            new("ui", "window.show", "window.show()"),
            new("ui", "on click", "def on_click():", requiresBody: true),
 
            // ---- TIME ----
            new("time", "sleep", "time.sleep(1)"),
            new("time", "now", "timestamp = datetime.now()"),
 
            // ---- SYSTEM ----
            new("system", "sys.exit", "sys.exit()"),
            new("system", "sys.argv", "args = sys.argv"),
 
            // ---- ADVANCED ----
            new("advanced", "import", "import module"),
            new("advanced", "from import", "from module import name"),
            new("advanced", "async def", "async def handler():", requiresBody: true),
            new("advanced", "await", "await task()"),
        };
 
        public static IEnumerable<PaletteItem> ForCategory(string category) => Items.Where(i => i.Category == category);
    }
 
    // =========================================================================
    // Renderer: pure GDI+ (System.Drawing) shapes matching the Scratch 2.0
    // puzzle-piece visual language. No SVG, no HTML, no browser control.
    // =========================================================================
    internal static class Renderer
    {
        // ----- layout constants (all in pixels @ 100% DPI) -----
        public const float RowH = 28f;
        public const float HatBulge = 11f;
        public const float FooterH = 14f;
        public const float MouthMin = 12f;
        public const float Indent = 18f;
        public const float LeftCutMargin = 6f;
        public const float RightMargin = 8f;
        public const float PadX = 10f;
        public const float PartGap = 6f;
        public const float Radius = 4f;
        public const float NotchX = 14f;
        public const float NotchW = 18f;
        public const float NotchH = 4f;
        public const float NotchSlant = 4f;
        public const float InlineRowH = 20f;
        public const float MinBlockW = 46f;
        public const float IconSize = 13f;
        public const float IconGap = 6f;
 
        public static readonly Font BlockFont = new(FontFamily.GenericSansSerif, 9.75f, FontStyle.Bold);
        public static readonly Font InlineFont = new(FontFamily.GenericSansSerif, 9.5f, FontStyle.Regular);
 
        // =====================================================================
        // PASS 1: measure (bottom-up), caches sizes on each BlockNode
        // =====================================================================
 
        /// <summary>Extra vertical breathing room placed above a "starter" block (Hat, or a class/def C-block) so separate scripts don't look welded together - skipped for the very first block in a stack.</summary>
        public const float StarterGap = 18f;
 
        private static bool IsStarter(BlockNode b) => b.Shape == BlockShape.Hat || (b.Shape == BlockShape.CBlock && b.HatTop);
 
        public static void MeasureStack(List<BlockNode> stack, Graphics g, out float width, out float height)
        {
            float w = 0, h = 0;
            for (int i = 0; i < stack.Count; i++)
            {
                var b = stack[i];
                Measure(b, g);
                if (i > 0 && IsStarter(b)) h += StarterGap;
                w = Math.Max(w, b.W);
                h += b.H;
            }
            width = w;
            height = h;
        }
 
        private static float HeaderRowWidth(List<Node> nodes, Graphics g)
            => Math.Max(MeasureInlineRow(nodes, g) + PadX * 2, MinBlockW);
 
        private static void Measure(BlockNode b, Graphics g)
        {
            float headerW = HeaderRowWidth(b.Header, g);
 
            switch (b.Shape)
            {
                case BlockShape.Hat:
                    b.HeaderH = RowH + HatBulge;
                    b.W = headerW;
                    b.H = b.HeaderH;
                    return;
 
                case BlockShape.Command:
                case BlockShape.Cap:
                    b.HeaderH = RowH;
                    b.W = headerW;
                    b.H = RowH;
                    return;
 
                case BlockShape.CBlock:
                    b.HeaderH = RowH + (b.HatTop ? HatBulge : 0f);
                    b.FooterH = FooterH;
                    float totalH = b.HeaderH;
                    float overallMaxW = headerW;
 
                    b.MouthW.Clear();
                    b.MouthH.Clear();
                    b.ContinuationHeaderH.Clear();
                    b.BarW.Clear();
 
                    for (int m = 0; m < b.Mouths.Count; m++)
                    {
                        MeasureStack(b.Mouths[m], g, out float mw, out float mh);
                        if (b.Mouths[m].Count == 0) mh = MouthMin;
                        b.MouthW.Add(mw);
                        b.MouthH.Add(mh);
                        totalH += mh;
 
                        // The bar directly above THIS mouth (header for m==0,
                        // otherwise the continuation before it) is sized to
                        // fit its own text AND this specific mouth - not the
                        // widest mouth anywhere in the block.
                        List<Node> ownText = (m == 0) ? b.Header : b.ContinuationHeaders[m - 1];
                        float barW = Math.Max(HeaderRowWidth(ownText, g), Indent + mw + RightMargin);
                        b.BarW.Add(barW);
                        overallMaxW = Math.Max(overallMaxW, barW);
 
                        if (m < b.ContinuationHeaders.Count)
                        {
                            b.ContinuationHeaderH.Add(RowH);
                            totalH += RowH;
                        }
                    }
 
                    // Footer has no text of its own - it just needs to fit
                    // under whatever the LAST mouth needs.
                    float lastMouthW = b.MouthW.Count > 0 ? b.MouthW[^1] : 0f;
                    b.FooterW = Math.Max(MinBlockW, Indent + lastMouthW + RightMargin);
                    overallMaxW = Math.Max(overallMaxW, b.FooterW);
 
                    totalH += b.FooterH;
                    b.W = overallMaxW; // bounding width other blocks stack against
                    b.H = totalH;
                    return;
            }
        }
 
        private static float MeasureInlineRow(List<Node> nodes, Graphics g)
        {
            float x = 0;
            bool first = true;
            foreach (var n in nodes)
            {
                if (!first) x += PartGap;
                first = false;
                x += MeasureNode(n, g);
            }
            return x;
        }
 
        private const float PillPadH = 7f; // horizontal padding inside a literal/variable pill (each side)
 
        private static float MeasureNode(Node n, Graphics g)
        {
            if (n is TextNode t)
            {
                string trimmed = t.Text.Trim();
                if (trimmed.Length == 0) return Math.Max(4f, g.MeasureString(" ", BlockFont).Width * 0.4f);
                return g.MeasureString(trimmed, BlockFont).Width;
            }
            if (n is PillNode pn)
            {
                return g.MeasureString(pn.Text, InlineFont).Width + PillPadH * 2f;
            }
            var bn = (BracketNode)n;
            float innerW = MeasureInlineRow(bn.Children, g);
            if (bn.Open == '[')
            {
                // dropdown pill: text + small arrow
                return innerW + 22f;
            }
            if (bn.Open == '<')
            {
                return Math.Max(30f, innerW + 22f); // hexagon needs extra room for the pointed ends
            }
            // '(' round reporter/number slot
            return Math.Max(22f, innerW + 16f);
        }
 
        // =====================================================================
        // PASS 2: draw, using the cached sizes from Measure()
        // =====================================================================
        public static void DrawStack(List<BlockNode> stack, Graphics g, float x, float y)
        {
            float curY = y;
            for (int i = 0; i < stack.Count; i++)
            {
                var b = stack[i];
                if (i > 0 && IsStarter(b)) curY += StarterGap;
                DrawBlock(b, g, x, curY);
                curY += b.H;
            }
        }
 
        private static void DrawBlock(BlockNode b, Graphics g, float x, float y)
        {
            var fill = PyClassifier.CategoryColors.TryGetValue(b.Category, out var c) ? c : PyClassifier.CategoryColors["grey"];
            Color? textColor = b.TextColorOverride;
            float textX = x + PadX;
 
            switch (b.Shape)
            {
                case BlockShape.Hat:
                    // DrawSimpleFace/BuildBarPath handle the HatBulge offset
                    // internally now (archTop case) - pass the raw top y.
                    DrawSimpleFace(g, x, y, b.W, RowH, fill, topNotch: false, bottomTab: true, hatTop: true);
                    DrawInlineRow(b.Header, g, textX, y + HatBulge, RowH, true, textColor);
                    break;
 
                case BlockShape.Command:
                    DrawSimpleFace(g, x, y, b.W, RowH, fill, topNotch: true, bottomTab: true, hatTop: false);
                    DrawInlineRow(b.Header, g, textX, y, RowH, true, textColor);
                    break;
 
                case BlockShape.Cap:
                    DrawSimpleFace(g, x, y, b.W, RowH, fill, topNotch: true, bottomTab: false, hatTop: false);
                    DrawInlineRow(b.Header, g, textX, y, RowH, true, textColor);
                    break;
 
                case BlockShape.CBlock:
                    DrawCBlock(b, g, x, y, fill);
                    break;
            }
        }
 
        private static void DrawCBlock(BlockNode b, Graphics g, float x, float y, Color fill)
        {
            Color? textColor = b.TextColorOverride;
            float textX = x + PadX;
 
            // ONE continuous path for the whole "C" - header bar (its own
            // width), the narrow wall through every mouth, each continuation
            // bar (its own width), and the footer bar, all stepped together
            // into a single outline. This is what makes the shape read as
            // one connected piece instead of a grid of separately-bordered
            // rectangles (that was the "weird squares" bug from drawing each
            // bar independently).
            using (var outline = BuildCBlockOutlinePath(b, x, y))
            {
                FillWithGradientAndBorder(g, outline, fill);
            }
 
            DrawInlineRow(b.Header, g, textX, y + (b.HatTop ? HatBulge : 0f), RowH, true, textColor);
 
            float cursorY = y + b.HeaderH;
            for (int m = 0; m < b.Mouths.Count; m++)
            {
                float mh = b.MouthH[m];
                DrawStack(b.Mouths[m], g, x + Indent, cursorY);
                cursorY += mh;
 
                // continuation divider bar (elif / else / except / finally), if any
                if (m < b.ContinuationHeaders.Count)
                {
                    float ch = b.ContinuationHeaderH[m];
                    DrawInlineRow(b.ContinuationHeaders[m], g, textX, cursorY, ch, true, textColor);
                    cursorY += ch;
                }
            }
            // footer occupies the remaining b.FooterH automatically (it's just
            // the tail of the outer colored silhouette below the last mouth).
        }
 
        /// <summary>
        /// Traces the actual "C" cross-section of a C-block as one path: the
        /// header bar is sized to fit Mouths[0] (not the widest mouth
        /// anywhere in the block), each elif/else/except/finally bar is sized
        /// to fit the mouth right after it, and the footer bar is sized to
        /// fit the last mouth - so the silhouette steps narrower or wider to
        /// hug what's actually inside it, joined by a narrow left-wall column
        /// through each mouth. There is deliberately no geometry to the right
        /// of the wall during a mouth row, so that area is genuinely open
        /// (whatever is drawn underneath shows through). Closed with the
        /// usual notch on top (or an arch, for a class/def "starter" block)
        /// and a tab on the bottom.
        /// </summary>
        private static GraphicsPath BuildCBlockOutlinePath(BlockNode b, float x, float y)
        {
            float r = Radius;
            float d = r * 2f;
            float wallRight = x + Indent;
 
            float headerRight = x + b.BarW[0];
            bool fitsHeader = b.BarW[0] > NotchX + NotchW + 4;
 
            var p = new GraphicsPath();
            p.StartFigure();
 
            if (b.HatTop)
            {
                p.AddArc(new RectangleF(x, y, b.BarW[0], HatBulge * 2f), 180, 180); // peaks at y, lands at y+HatBulge
            }
            else
            {
                p.AddArc(x, y, d, d, 180, 90); // top-left corner
 
                if (fitsHeader)
                {
                    p.AddLine(x + r, y, x + NotchX, y);
                    p.AddLine(x + NotchX, y, x + NotchX + NotchSlant, y + NotchH);
                    p.AddLine(x + NotchX + NotchSlant, y + NotchH, x + NotchX + NotchW - NotchSlant, y + NotchH);
                    p.AddLine(x + NotchX + NotchW - NotchSlant, y + NotchH, x + NotchX + NotchW, y);
                }
 
                p.AddArc(headerRight - d, y, d, d, 270, 90); // top-right corner of the header bar
            }
 
            float curY = y + b.HeaderH;
            float curRight = headerRight;
 
            for (int m = 0; m < b.Mouths.Count; m++)
            {
                float mouthBottom = curY + b.MouthH[m];
                p.AddLine(curRight, curY, wallRight, curY);         // step inward: top of the mouth
                p.AddLine(wallRight, curY, wallRight, mouthBottom); // down the narrow inner wall
                curY = mouthBottom;
 
                if (m < b.ContinuationHeaders.Count)
                {
                    float contRight = x + b.BarW[m + 1];
                    float contBottom = curY + b.ContinuationHeaderH[m];
                    p.AddLine(wallRight, curY, contRight, curY);       // step back out: this continuation's own width
                    p.AddLine(contRight, curY, contRight, contBottom); // down through the continuation bar
                    curY = contBottom;
                    curRight = contRight;
                }
            }
 
            float footerRight = x + b.FooterW;
            float footerBottom = curY + b.FooterH;
            bool fitsFooter = b.FooterW > NotchX + NotchW + 4;
 
            p.AddLine(wallRight, curY, footerRight, curY);
            p.AddLine(footerRight, curY, footerRight, footerBottom);
 
            p.AddArc(footerRight - d, footerBottom - d, d, d, 0, 90); // bottom-right corner
 
            if (fitsFooter)
            {
                p.AddLine(footerRight - r, footerBottom, x + NotchX + NotchW, footerBottom);
                p.AddLine(x + NotchX + NotchW, footerBottom, x + NotchX + NotchW - NotchSlant, footerBottom + NotchH);
                p.AddLine(x + NotchX + NotchW - NotchSlant, footerBottom + NotchH, x + NotchX + NotchSlant, footerBottom + NotchH);
                p.AddLine(x + NotchX + NotchSlant, footerBottom + NotchH, x + NotchX, footerBottom);
            }
 
            p.AddArc(x, footerBottom - d, d, d, 90, 90); // bottom-left corner
            p.CloseFigure(); // implicit straight left edge, full height, back to the start
            return p;
        }
 
        /// <summary>
        /// NOTE: icons are intentionally not drawn on blocks anymore - kept
        /// here (and DrawCategoryIcon below) only so a future category-legend
        /// / filter-button UI can reuse the exact same glyphs.
        /// </summary>
        private static void DrawIcon(Graphics g, string category, float x, float rowY, float rowH, Color? textColor)
        {
            Color iconColor = textColor ?? Color.White;
            float iy = rowY + (rowH - IconSize) / 2f;
            DrawCategoryIcon(g, category, x, iy, IconSize, iconColor);
        }
 
        /// <summary>
        /// Small hand-drawn vector glyphs for each LVL1-LVL12 category (no
        /// emoji-font dependency, so these render identically everywhere):
        /// FLOW=branch, VARIABLES=box, FUNCTIONS=lightning, OBJECTS=diamond,
        /// DATA=stacked bars, TEXT=speech bubble, MATH=sigma, FILES=folder,
        /// UI=window, TIME=clock, SYSTEM=gear, ADVANCED=overlapping circles.
        /// </summary>
        private static void DrawCategoryIcon(Graphics g, string category, float x, float y, float size, Color color)
        {
            using var pen = new Pen(color, 1.3f);
            using var brush = new SolidBrush(color);
 
            switch (category)
            {
                case "flow":
                    g.DrawLine(pen, x + size * 0.5f, y, x + size * 0.5f, y + size * 0.42f);
                    g.DrawLine(pen, x + size * 0.5f, y + size * 0.42f, x + size * 0.12f, y + size);
                    g.DrawLine(pen, x + size * 0.5f, y + size * 0.42f, x + size * 0.88f, y + size);
                    break;
 
                case "variables":
                    g.DrawRectangle(pen, x + size * 0.12f, y + size * 0.12f, size * 0.76f, size * 0.76f);
                    break;
 
                case "functions":
                    g.FillPolygon(brush, new[]
                    {
                        new PointF(x + size * 0.58f, y),
                        new PointF(x + size * 0.12f, y + size * 0.6f),
                        new PointF(x + size * 0.46f, y + size * 0.6f),
                        new PointF(x + size * 0.38f, y + size),
                        new PointF(x + size * 0.88f, y + size * 0.38f),
                        new PointF(x + size * 0.5f, y + size * 0.38f),
                    });
                    break;
 
                case "objects":
                    g.DrawPolygon(pen, new[]
                    {
                        new PointF(x + size * 0.5f, y),
                        new PointF(x + size, y + size * 0.5f),
                        new PointF(x + size * 0.5f, y + size),
                        new PointF(x, y + size * 0.5f),
                    });
                    break;
 
                case "data":
                    for (int k = 0; k < 3; k++)
                    {
                        float by = y + k * (size * 0.4f);
                        g.DrawLine(pen, x, by + size * 0.1f, x + size, by + size * 0.1f);
                    }
                    break;
 
                case "text":
                    g.DrawEllipse(pen, x, y, size, size * 0.72f);
                    g.FillPolygon(brush, new[]
                    {
                        new PointF(x + size * 0.22f, y + size * 0.6f),
                        new PointF(x + size * 0.08f, y + size),
                        new PointF(x + size * 0.42f, y + size * 0.66f),
                    });
                    break;
 
                case "math":
                    using (var f = new Font(FontFamily.GenericSansSerif, size * 0.85f, FontStyle.Bold))
                        g.DrawString("\u03A3", f, brush, x - size * 0.08f, y - size * 0.18f);
                    break;
 
                case "files":
                    g.DrawLine(pen, x, y + size * 0.28f, x + size * 0.4f, y + size * 0.28f);
                    g.DrawLine(pen, x + size * 0.4f, y + size * 0.28f, x + size * 0.5f, y + size * 0.14f);
                    g.DrawRectangle(pen, x, y + size * 0.28f, size, size * 0.6f);
                    break;
 
                case "ui":
                    g.DrawRectangle(pen, x, y, size, size * 0.8f);
                    g.DrawLine(pen, x, y + size * 0.24f, x + size, y + size * 0.24f);
                    break;
 
                case "time":
                    g.DrawEllipse(pen, x, y, size, size);
                    g.DrawLine(pen, x + size * 0.5f, y + size * 0.5f, x + size * 0.5f, y + size * 0.2f);
                    g.DrawLine(pen, x + size * 0.5f, y + size * 0.5f, x + size * 0.76f, y + size * 0.58f);
                    break;
 
                case "system":
                    g.DrawPolygon(pen, HexPoints(x + size * 0.5f, y + size * 0.5f, size * 0.5f));
                    g.DrawEllipse(pen, x + size * 0.3f, y + size * 0.3f, size * 0.4f, size * 0.4f);
                    break;
 
                case "advanced":
                    g.DrawEllipse(pen, x, y + size * 0.14f, size * 0.58f, size * 0.72f);
                    g.DrawEllipse(pen, x + size * 0.42f, y + size * 0.14f, size * 0.58f, size * 0.72f);
                    break;
 
                default:
                    break; // "comment" / "grey": no icon, just keeps everything left-aligned
            }
        }
 
        private static PointF[] HexPoints(float cx, float cy, float r)
        {
            var pts = new PointF[6];
            for (int k = 0; k < 6; k++)
            {
                double ang = Math.PI / 3 * k - Math.PI / 6;
                pts[k] = new PointF(cx + r * (float)Math.Cos(ang), cy + r * (float)Math.Sin(ang));
            }
            return pts;
        }
 
        /// <summary>
        /// Draws a simple (non-C) block face: fills + bevels the shape.
        /// (x, y, w, h) is always the BODY rect - for a hat block that means the
        /// rectangular part below the arch; the arch is drawn using the HatBulge
        /// constant above (x, y), so callers should pass y already shifted down
        /// by HatBulge for hat blocks (see the Hat case in DrawBlock).
        /// </summary>
        private static void DrawSimpleFace(Graphics g, float x, float y, float w, float h, Color fill,
            bool topNotch, bool bottomTab, bool hatTop)
        {
            float rTop = hatTop ? 0f : Radius;
            using var path = BuildBarPath(x, y, w, h, topNotch, bottomTab, hatTop, rTop, Radius);
            FillWithGradientAndBorder(g, path, fill);
        }
 
        /// <summary>
        /// Traces one block "bar" as a single path: rounded (or square, if
        /// rTop/rBottom is 0) corners, an optional notch cut into the top
        /// edge, an optional tab protruding from the bottom edge, or - for a
        /// class/def "starter" bar - an arch across the top instead of a
        /// notch. (x, y) is always the very top-left of the bar's own
        /// bounding box; when archTop is true the flat body sits HatBulge
        /// below y (the arch peaks at y itself), exactly like the standalone
        /// Hat shape used to.
        /// </summary>
        private static GraphicsPath BuildBarPath(float x, float y, float w, float h, bool topNotch, bool bottomTab, bool archTop, float rTop, float rBottom)
        {
            var p = new GraphicsPath();
            bool fits = w > NotchX + NotchW + 4;
            float dTop = rTop * 2f;
            float dBottom = rBottom * 2f;
            float topY = archTop ? y + HatBulge : y;
            float bottomY = topY + h;
 
            p.StartFigure();
 
            if (archTop)
            {
                p.AddArc(new RectangleF(x, y, w, HatBulge * 2f), 180, 180); // peaks at y, lands at topY on both ends
            }
            else
            {
                if (rTop > 0.01f) p.AddArc(x, topY, dTop, dTop, 180, 90);
                float topLeftX = rTop > 0.01f ? x + rTop : x;
                float topRightX = rTop > 0.01f ? x + w - rTop : x + w;
 
                if (topNotch && fits)
                {
                    p.AddLine(topLeftX, topY, x + NotchX, topY);
                    p.AddLine(x + NotchX, topY, x + NotchX + NotchSlant, topY + NotchH);
                    p.AddLine(x + NotchX + NotchSlant, topY + NotchH, x + NotchX + NotchW - NotchSlant, topY + NotchH);
                    p.AddLine(x + NotchX + NotchW - NotchSlant, topY + NotchH, x + NotchX + NotchW, topY);
                }
                else if (rTop <= 0.01f)
                {
                    p.AddLine(topLeftX, topY, topRightX, topY);
                }
                // else: rTop>0 and no notch - the two corner arcs auto-connect the flat edge between them
 
                if (rTop > 0.01f) p.AddArc(x + w - dTop, topY, dTop, dTop, 270, 90);
            }
 
            if (rBottom > 0.01f) p.AddArc(x + w - dBottom, bottomY - dBottom, dBottom, dBottom, 0, 90);
 
            float botRightX = rBottom > 0.01f ? x + w - rBottom : x + w;
            float botLeftX = rBottom > 0.01f ? x + rBottom : x;
 
            if (bottomTab && fits)
            {
                p.AddLine(botRightX, bottomY, x + NotchX + NotchW, bottomY);
                p.AddLine(x + NotchX + NotchW, bottomY, x + NotchX + NotchW - NotchSlant, bottomY + NotchH);
                p.AddLine(x + NotchX + NotchW - NotchSlant, bottomY + NotchH, x + NotchX + NotchSlant, bottomY + NotchH);
                p.AddLine(x + NotchX + NotchSlant, bottomY + NotchH, x + NotchX, bottomY);
            }
            else if (rBottom <= 0.01f)
            {
                p.AddLine(botRightX, bottomY, botLeftX, bottomY);
            }
 
            if (rBottom > 0.01f) p.AddArc(x, bottomY - dBottom, dBottom, dBottom, 90, 90);
 
            p.CloseFigure(); // implicit straight left edge, full height, back to the start
            return p;
        }
 
        /// <summary>
        /// Fills a shape with a gentle diagonal gradient (lighter top-left,
        /// darker bottom-right - reads as a raised, glossy surface) and
        /// strokes ONE uniform border around its exact silhouette. This
        /// intentionally does NOT try to do a separate two-tone light/dark
        /// rim on top of that: every earlier attempt at that (clipped bands
        /// on a plain rect, clipped bands on the full C outline, then
        /// independent per-bar rim lines) ran into some version of "the
        /// border looks different depending on which edge/corner you look
        /// at" because it always involved either two regions that could
        /// overlap, or a fixed set of explicit lines that don't line up with
        /// the real notch/tab/arch geometry. A single fill + a single stroke
        /// of the SAME path cannot have that problem by construction: there
        /// is exactly one border, drawn once, at one width, everywhere.
        /// </summary>
        private static void FillWithGradientAndBorder(Graphics g, GraphicsPath path, Color fill)
        {
            var bounds = path.GetBounds();
            bounds.Inflate(1f, 1f);
            if (bounds.Width < 1f) bounds.Width = 1f;
            if (bounds.Height < 1f) bounds.Height = 1f;
 
            Color lightFill = Lighten(fill, 0.18f);
            Color darkFill = Darken(fill, 0.22f);
            using (var gradientBrush = new LinearGradientBrush(bounds, lightFill, darkFill, LinearGradientMode.ForwardDiagonal))
                g.FillPath(gradientBrush, path);
 
            using (var borderPen = new Pen(Darken(fill, 0.42f), 1f))
                g.DrawPath(borderPen, path);
        }
 
        // ---- inline content (label text, reporters, booleans, dropdowns) ----
 
        private static float DrawInlineRow(List<Node> nodes, Graphics g, float x, float y, float rowH, bool draw, Color? textColor = null)
        {
            float cursor = x;
            bool first = true;
            foreach (var n in nodes)
            {
                if (!first) cursor += PartGap;
                first = false;
                cursor += DrawNode(n, g, cursor, y, rowH, draw, textColor);
            }
            return cursor - x;
        }
 
        private static float DrawNode(Node n, Graphics g, float x, float y, float rowH, bool draw, Color? textColor = null)
        {
            if (n is TextNode t)
            {
                string trimmed = t.Text.Trim();
                float w = MeasureNode(n, g);
                if (draw && trimmed.Length > 0)
                {
                    var size = g.MeasureString(trimmed, BlockFont);
                    float ty = y + (rowH - size.Height) / 2f;
                    using var textBrush = new SolidBrush(textColor ?? Color.White);
                    g.DrawString(trimmed, BlockFont, textBrush, x, ty);
                }
                return w;
            }
 
            if (n is PillNode pn)
            {
                var size = g.MeasureString(pn.Text, InlineFont);
                float pillW = size.Width + PillPadH * 2f;
                float pillH = InlineRowH;
                float py0 = y + (rowH - pillH) / 2f;
 
                if (draw)
                {
                    if (pn.Kind == PillKind.Variable)
                    {
                        var vc = PyClassifier.CategoryColors["variables"];
                        using var path = BuildBarPath(x, py0, pillW, pillH, topNotch: false, bottomTab: false, archTop: false, rTop: pillH / 2f, rBottom: pillH / 2f);
                        FillWithGradientAndBorder(g, path, vc);
                        using var tb = new SolidBrush(Color.White);
                        g.DrawString(pn.Text, InlineFont, tb, x + PillPadH, py0 + (pillH - size.Height) / 2f);
                    }
                    else
                    {
                        using var path = RoundedRectPath(x, py0, pillW, pillH, pillH / 2f);
                        using var lb = new SolidBrush(Color.White);
                        using var lp = new Pen(Color.FromArgb(140, 140, 140), 1f);
                        g.FillPath(lb, path);
                        g.DrawPath(lp, path);
                        using var tb = new SolidBrush(Color.Black);
                        g.DrawString(pn.Text, InlineFont, tb, x + PillPadH, py0 + (pillH - size.Height) / 2f);
                    }
                }
                return pillW;
            }
 
            var bn = (BracketNode)n;
            float innerH = InlineRowH;
            float innerW = MeasureInlineRow(bn.Children, g);
            float y0 = y + (rowH - innerH) / 2f;
 
            if (bn.Open == '[')
            {
                float pillW = innerW + 22f;
                if (draw)
                {
                    using var path = RoundedRectPath(x, y0, pillW, innerH, innerH / 2f);
                    using var pillBrush = new SolidBrush(Color.White);
                    using var pillPen = new Pen(Color.FromArgb(120, 120, 120), 1f);
                    g.FillPath(pillBrush, path);
                    g.DrawPath(pillPen, path);
                    DrawInlineRow(bn.Children, g, x + 8f, y0, innerH, true);
                    DrawDropdownArrow(g, x + pillW - 14f, y0 + innerH / 2f);
                    RestoreTextColorForDropdown(g, bn, x + 8f, y0, innerH);
                }
                return pillW;
            }
 
            if (bn.Open == '<')
            {
                float hexW = Math.Max(30f, innerW + 22f);
                string keyword = ScriptParser.HeaderKeywordText(bn.Children);
                string? cat = PyClassifier.ClassifyInline(keyword);
                if (draw)
                {
                    using var path = HexagonPath(x, y0, hexW, innerH);
                    if (cat != null)
                    {
                        var hc = PyClassifier.CategoryColors[cat];
                        using var hb = new SolidBrush(hc);
                        using var hp = new Pen(Darken(hc, 0.25f), 1f);
                        g.FillPath(hb, path);
                        g.DrawPath(hp, path);
                        DrawInlineRowColored(bn.Children, g, x + 10f, y0, innerH, Color.White);
                    }
                    else
                    {
                        using var hb = new SolidBrush(Color.White);
                        using var hp = new Pen(Color.FromArgb(120, 120, 120), 1f);
                        g.FillPath(hb, path);
                        g.DrawPath(hp, path);
                        DrawInlineRowColored(bn.Children, g, x + 10f, y0, innerH, Color.Black);
                    }
                }
                return hexW;
            }
 
            // '(' round reporter / number slot
            float ovalW = Math.Max(22f, innerW + 16f);
            {
                string keyword = ScriptParser.HeaderKeywordText(bn.Children);
                string? cat = PyClassifier.ClassifyInline(keyword);
                if (draw)
                {
                    using var path = RoundedRectPath(x, y0, ovalW, innerH, innerH / 2f);
                    if (cat != null)
                    {
                        var hc = PyClassifier.CategoryColors[cat];
                        using var hb = new SolidBrush(hc);
                        using var hp = new Pen(Darken(hc, 0.25f), 1f);
                        g.FillPath(hb, path);
                        g.DrawPath(hp, path);
                        DrawInlineRowColored(bn.Children, g, x + 8f, y0, innerH, Color.White);
                    }
                    else
                    {
                        using var hb = new SolidBrush(Color.White);
                        using var hp = new Pen(Color.FromArgb(150, 150, 150), 1f);
                        g.FillPath(hb, path);
                        g.DrawPath(hp, path);
                        DrawInlineRowColored(bn.Children, g, x + 8f, y0, innerH, Color.Black);
                    }
                }
                return ovalW;
            }
        }
 
        private static void RestoreTextColorForDropdown(Graphics g, BracketNode bn, float x, float y0, float innerH)
        {
            // Dropdown pill text is drawn black (drawn again here on top in
            // black since DrawInlineRow above used the default white color
            // for TextNode children - dropdown captions must read on white).
            using var blackBrush = new SolidBrush(Color.Black);
            float cx = x;
            bool first = true;
            foreach (var c in bn.Children)
            {
                if (!first) cx += PartGap;
                first = false;
                if (c is TextNode t)
                {
                    string trimmed = t.Text.Trim();
                    if (trimmed.Length > 0)
                    {
                        var size = g.MeasureString(trimmed, InlineFont);
                        float ty = y0 + (innerH - size.Height) / 2f;
                        // paint over the white text drawn by DrawInlineRow
                        using var whiteOver = new SolidBrush(Color.White);
                        g.DrawString(trimmed, InlineFont, whiteOver, cx, ty);
                        g.DrawString(trimmed, InlineFont, blackBrush, cx, ty);
                    }
                    cx += MeasureNode(c, g);
                }
                else
                {
                    cx += MeasureNode(c, g);
                }
            }
        }
 
        private static void DrawInlineRowColored(List<Node> nodes, Graphics g, float x, float y, float rowH, Color textColor)
        {
            float cursor = x;
            bool first = true;
            foreach (var n in nodes)
            {
                if (!first) cursor += PartGap;
                first = false;
                if (n is TextNode t)
                {
                    string trimmed = t.Text.Trim();
                    if (trimmed.Length > 0)
                    {
                        var size = g.MeasureString(trimmed, InlineFont);
                        float ty = y + (rowH - size.Height) / 2f;
                        using var b = new SolidBrush(textColor);
                        g.DrawString(trimmed, InlineFont, b, cursor, ty);
                    }
                    cursor += MeasureNode(n, g);
                }
                else
                {
                    cursor += DrawNode(n, g, cursor, y, rowH, true);
                }
            }
        }
 
        private static void DrawDropdownArrow(Graphics g, float cx, float cy)
        {
            using var b = new SolidBrush(Color.FromArgb(90, 90, 90));
            var pts = new[]
            {
                new PointF(cx - 4f, cy - 2f),
                new PointF(cx + 4f, cy - 2f),
                new PointF(cx, cy + 3f),
            };
            g.FillPolygon(b, pts);
        }
 
        // ---- small standalone shape helpers still used by pills/dropdowns/hexagons ----
 
        private static GraphicsPath RoundedRectPath(float x, float y, float w, float h, float r)
        {
            r = Math.Min(r, Math.Min(w, h) / 2f);
            var p = new GraphicsPath();
            if (r <= 0.01f)
            {
                p.AddRectangle(new RectangleF(x, y, w, h));
                return p;
            }
            float d = r * 2f;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
 
        private static GraphicsPath HexagonPath(float x, float y, float w, float h)
        {
            float cut = Math.Min(h / 2f * 0.75f, w / 2f - 2f);
            var p = new GraphicsPath();
            p.AddPolygon(new[]
            {
                new PointF(x + cut, y),
                new PointF(x + w - cut, y),
                new PointF(x + w, y + h / 2f),
                new PointF(x + w - cut, y + h),
                new PointF(x + cut, y + h),
                new PointF(x, y + h / 2f),
            });
            return p;
        }
 
        private static Color Darken(Color c, float amount)
        {
            int r = (int)(c.R * (1f - amount));
            int gg = (int)(c.G * (1f - amount));
            int b = (int)(c.B * (1f - amount));
            return Color.FromArgb(c.A, Math.Max(0, r), Math.Max(0, gg), Math.Max(0, b));
        }
 
        private static Color Lighten(Color c, float amount)
        {
            int r = c.R + (int)((255 - c.R) * amount);
            int gg = c.G + (int)((255 - c.G) * amount);
            int b = c.B + (int)((255 - c.B) * amount);
            return Color.FromArgb(c.A, Math.Min(255, r), Math.Min(255, gg), Math.Min(255, b));
        }
    }
 
    // =========================================================================
    // Canvas control: paints the parsed script with GDI+, supports scrolling
    // =========================================================================
    internal sealed class BlockCanvas : Panel
    {
        public List<BlockNode> Script = new();
        private const float MarginX = 20f, MarginY = 20f;
 
        public BlockCanvas()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScroll = true;
            BackColor = Color.White;
        }
 
        public void SetScript(List<BlockNode> script)
        {
            Script = script;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                Renderer.MeasureStack(Script, g, out float w, out float h);
                AutoScrollMinSize = new Size((int)Math.Ceiling(w + MarginX * 2), (int)Math.Ceiling(h + MarginY * 2));
            }
            Invalidate();
        }
 
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.TranslateTransform(AutoScrollPosition.X + MarginX, AutoScrollPosition.Y + MarginY);
            Renderer.DrawStack(Script, g, 0, 0);
        }
 
        /// <summary>Renders the whole script to a right-sized bitmap for PNG export.</summary>
        public Bitmap RenderToBitmap()
        {
            using var measureBmp = new Bitmap(1, 1);
            using var measureG = Graphics.FromImage(measureBmp);
            Renderer.MeasureStack(Script, measureG, out float w, out float h);
 
            int width = (int)Math.Ceiling(w + MarginX * 2);
            int height = (int)Math.Ceiling(h + MarginY * 2);
            width = Math.Max(width, 1);
            height = Math.Max(height, 1);
 
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);
                g.TranslateTransform(MarginX, MarginY);
                Renderer.DrawStack(Script, g, 0, 0);
            }
            return bmp;
        }
    }
 
    // =========================================================================
    // Main window
    // =========================================================================
    internal sealed class MainForm : Form
    {
        private readonly TextBox _input;
        private readonly BlockCanvas _canvas;
        private readonly System.Windows.Forms.Timer _debounce;
        private readonly Label _status;
 
        private const string SampleScript =
@"#!/usr/bin/env python3
# Demo script for the Python block visualizer
import math
import os
 
class ShapeCalculator:
    # Computes area and perimeter for simple shapes.
 
    def __init__(self, name):
        self.name = name
        self.history = []
 
    def area_of_circle(self, radius):
        area = math.pi * radius ** 2
        self.history.append(area)
        return round(area, 2)
 
def load_scores(path):
    if not os.path.exists(path):
        return []
    scores = []
    with open(path) as f:
        for line in f.readlines():
            line = line.strip()
            if line.isdigit():
                scores.append(int(line))
            elif line == """":
                continue
            else:
                print(f""Skipping bad line: {line}"")
    return scores
 
def summarize(scores):
    total = sum(scores)
    average = total / len(scores) if scores else 0
    try:
        highest = max(scores)
    except ValueError:
        highest = None
    finally:
        print(""Done summarizing"")
    return total, average, highest
 
for i in range(5):
    print(i)
 
scores = load_scores(""scores.txt"")
total, average, highest = summarize(scores)
print(f""Total: {total}, Average: {average:.2f}, Highest: {highest}"")";
 
        public MainForm()
        {
            Text = "Python Block Viewer (native WinForms / GDI+, no web engine)";
            Width = 1180;
            Height = 760;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font(FontFamily.GenericSansSerif, 9f);
 
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 420,
                FixedPanel = FixedPanel.Panel1,
            };
            Controls.Add(split);
 
            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            split.Panel1.Controls.Add(leftPanel);
 
            var label = new Label { Text = "Python source (real code, indentation-based):", Dock = DockStyle.Top, Height = 22 };
            leftPanel.Controls.Add(label);
 
            _input = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericMonospace, 10f),
                ScrollBars = ScrollBars.Both,
                AcceptsTab = true,
                Text = SampleScript,
            };
            leftPanel.Controls.Add(_input);
            _input.BringToFront();
            label.SendToBack();
 
            var buttonBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.LeftToRight };
            var renderBtn = new Button { Text = "Render", Width = 90 };
            var exportBtn = new Button { Text = "Export PNG...", Width = 110 };
            var sampleBtn = new Button { Text = "Reset Sample", Width = 110 };
            buttonBar.Controls.Add(renderBtn);
            buttonBar.Controls.Add(exportBtn);
            buttonBar.Controls.Add(sampleBtn);
            leftPanel.Controls.Add(buttonBar);
            buttonBar.BringToFront();
 
            _status = new Label { Dock = DockStyle.Bottom, Height = 20, Text = "", ForeColor = Color.DimGray };
            leftPanel.Controls.Add(_status);
            _status.BringToFront();
 
            var rightPanel = new Panel { Dock = DockStyle.Fill };
            split.Panel2.Controls.Add(rightPanel);
            _canvas = new BlockCanvas { Dock = DockStyle.Fill };
            rightPanel.Controls.Add(_canvas);
 
            _debounce = new System.Windows.Forms.Timer { Interval = 250 };
            _debounce.Tick += (s, e) => { _debounce.Stop(); DoRender(); };
 
            _input.TextChanged += (s, e) => { _debounce.Stop(); _debounce.Start(); };
            renderBtn.Click += (s, e) => DoRender();
            exportBtn.Click += (s, e) => DoExport();
            sampleBtn.Click += (s, e) => { _input.Text = SampleScript; DoRender(); };
 
            Load += (s, e) => DoRender();
        }
 
        private void DoRender()
        {
            try
            {
                var script = ScriptParser.Parse(_input.Text);
                _canvas.SetScript(script);
                _status.Text = $"{script.Count} top-level block(s) parsed.";
            }
            catch (Exception ex)
            {
                _status.Text = "Parse error: " + ex.Message;
            }
        }
 
        private void DoExport()
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "PNG image (*.png)|*.png",
                FileName = "scratchblocks.png",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
 
            using var bmp = _canvas.RenderToBitmap();
            bmp.Save(dlg.FileName, ImageFormat.Png);
            _status.Text = "Saved: " + dlg.FileName;
        }
    }
}
 
