// PyBlocksViewer (formerly ScratchBlocksViewer)
// -----------------------------------------------------------------------------
// A single-file .NET 6 WinForms tool that renders real Python source code as
// colored, puzzle-piece "blocks" - the same visual language Scratch/
// scratchblocks uses - to make scripts easier to scan at a glance. Everything
// is drawn natively with GDI+ (System.Drawing); there is no WebView, no HTML,
// no embedded browser.
//
// WHAT CHANGED FROM THE SCRATCH VERSION:
//   - The parser now reads real, indentation-based Python (if/elif/else,
//     for/while, try/except/finally, def/class, ...) instead of
//     scratchblocks' "end"-terminated plain-text DSL.
//   - The category palette + icons come from the LVL1-LVL12 taxonomy you
//     provided (FLOW/VARIABLES/FUNCTIONS/OBJECTS/DATA/TEXT/MATH/FILES/UI/
//     TIME/SYSTEM/ADVANCED), each with its own color and a small hand-drawn
//     vector icon (no emoji-font dependency, so it renders identically on
//     any machine).
//   - Each Python line is classified by keyword/pattern and drawn as one
//     block; lines ending in ":" become C-blocks with a nested "mouth"
//     for their indented body, and chain any elif/else/except/finally
//     continuations onto the same block.
//
// HONESTY NOTE: this is a heuristic, line-by-line classifier - not a real
// Python AST parser. It doesn't track multi-line statements (triple-quoted
// strings, brackets spanning lines, backslash continuations), and keyword
// matching is pattern-based (e.g. ".append(" -> DATA) so it can occasionally
// mis-tag an unusual line. It's meant to make ordinary scripts easier to
// skim, not to be a source of semantic truth about the code.
//
// Build (on Windows, with the .NET 6 SDK installed):
//   dotnet run
// -----------------------------------------------------------------------------
 
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
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
            AppAssets.LoadFont();
            Renderer.ApplyCustomFont(AppAssets.UiFamily);
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
        public string SubCategory; // e.g. "Execution", "Conditions" under FLOW - rendered as a divider bar in the palette
        public string Label;       // short name shown on the stencil
        public string Template;    // the literal Python line(s) inserted (first line only if RequiresBody)
        public bool RequiresBody;  // true for compound statements (if/for/def/...) - a "    pass" placeholder body is inserted under it
        public PaletteItem(string category, string subCategory, string label, string template, bool requiresBody = false)
        {
            Category = category;
            SubCategory = subCategory;
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
 
        // Mirrors the LVL1-LVL12 taxonomy exactly: category -> sub-category ->
        // items, in the given order (sub-category order/grouping in the
        // palette comes from this list's order, via GroupBy).
        public static readonly List<PaletteItem> Items = new()
        {
            // ==================== LVL 1: FLOW ====================
            new("flow", "Execution", "pass", "pass"),
            new("flow", "Execution", "return", "return value"),
            new("flow", "Execution", "yield", "yield value"),
 
            new("flow", "Conditions", "if", "if condition:", requiresBody: true),
            new("flow", "Conditions", "elif", "elif condition:", requiresBody: true),
            new("flow", "Conditions", "else", "else:", requiresBody: true),
            new("flow", "Conditions", "match", "match value:", requiresBody: true),
 
            new("flow", "Loops", "for", "for item in range(10):", requiresBody: true),
            new("flow", "Loops", "while", "while condition:", requiresBody: true),
            new("flow", "Loops", "break", "break"),
            new("flow", "Loops", "continue", "continue"),
 
            new("flow", "Iteration Helpers", "range()", "range(10)"),
            new("flow", "Iteration Helpers", "enumerate()", "enumerate(items)"),
            new("flow", "Iteration Helpers", "zip()", "zip(a, b)"),
            new("flow", "Iteration Helpers", "reversed()", "reversed(items)"),
 
            new("flow", "Exceptions Flow", "try", "try:", requiresBody: true),
            new("flow", "Exceptions Flow", "except", "except Exception:", requiresBody: true),
            new("flow", "Exceptions Flow", "finally", "finally:", requiresBody: true),
            new("flow", "Exceptions Flow", "raise", "raise Exception(\"error\")"),
 
            // ==================== LVL 2: VARIABLES ====================
            new("variables", "Assignment", "=", "x = value"),
            new("variables", "Assignment", "+=", "x += 1"),
            new("variables", "Assignment", "-=", "x -= 1"),
            new("variables", "Assignment", "*=", "x *= 2"),
            new("variables", "Assignment", "/=", "x /= 2"),
 
            new("variables", "Types", "int", "x: int = 0"),
            new("variables", "Types", "float", "x: float = 0.0"),
            new("variables", "Types", "str", "x: str = \"\""),
            new("variables", "Types", "bool", "x: bool = True"),
            new("variables", "Types", "list", "x: list = []"),
            new("variables", "Types", "dict", "x: dict = {}"),
 
            new("variables", "Constants", "True", "True"),
            new("variables", "Constants", "False", "False"),
            new("variables", "Constants", "None", "None"),
 
            new("variables", "Conversion", "int()", "int(value)"),
            new("variables", "Conversion", "float()", "float(value)"),
            new("variables", "Conversion", "str()", "str(value)"),
            new("variables", "Conversion", "bool()", "bool(value)"),
 
            // ==================== LVL 3: FUNCTIONS ====================
            new("functions", "Definition", "def", "def my_function():", requiresBody: true),
            new("functions", "Definition", "lambda", "square = lambda x: x * x"),
 
            new("functions", "Return", "return", "return value"),
 
            new("functions", "Parameters", "args (*args)", "def my_function(*args):", requiresBody: true),
            new("functions", "Parameters", "kwargs (**kwargs)", "def my_function(**kwargs):", requiresBody: true),
            new("functions", "Parameters", "default values", "def my_function(x=0):", requiresBody: true),
 
            new("functions", "Scope", "global", "global x"),
            new("functions", "Scope", "nonlocal", "nonlocal x"),
 
            new("functions", "Decorators", "@property", "@property"),
            new("functions", "Decorators", "@staticmethod", "@staticmethod"),
            new("functions", "Decorators", "@classmethod", "@classmethod"),
 
            // ==================== LVL 4: OBJECTS ====================
            new("objects", "Classes", "class", "class MyClass:", requiresBody: true),
            new("objects", "Classes", "self", "self.value = 0"),
            new("objects", "Classes", "__init__", "def __init__(self):", requiresBody: true),
 
            new("objects", "Attributes", "getattr", "getattr(obj, \"name\")"),
            new("objects", "Attributes", "setattr", "setattr(obj, \"name\", value)"),
 
            new("objects", "Methods", "instance method", "def method(self):", requiresBody: true),
            new("objects", "Methods", "class method", "def method(cls):", requiresBody: true),
            new("objects", "Methods", "static method", "def method():", requiresBody: true),
 
            new("objects", "Inheritance", "super()", "super().__init__()"),
            new("objects", "Inheritance", "override", "def method(self):", requiresBody: true),
 
            // ==================== LVL 5: DATA ====================
            new("data", "Lists", "append()", "my_list.append(item)"),
            new("data", "Lists", "extend()", "my_list.extend(items)"),
            new("data", "Lists", "insert()", "my_list.insert(0, item)"),
            new("data", "Lists", "remove()", "my_list.remove(item)"),
            new("data", "Lists", "pop()", "my_list.pop()"),
            new("data", "Lists", "sort()", "my_list.sort()"),
            new("data", "Lists", "reverse()", "my_list.reverse()"),
 
            new("data", "Dictionaries", "keys()", "my_dict.keys()"),
            new("data", "Dictionaries", "values()", "my_dict.values()"),
            new("data", "Dictionaries", "items()", "my_dict.items()"),
            new("data", "Dictionaries", "get()", "my_dict.get(key)"),
            new("data", "Dictionaries", "update()", "my_dict.update(other)"),
            new("data", "Dictionaries", "pop()", "my_dict.pop(key)"),
 
            new("data", "Sets", "add()", "my_set.add(item)"),
            new("data", "Sets", "remove()", "my_set.remove(item)"),
            new("data", "Sets", "union()", "my_set.union(other)"),
            new("data", "Sets", "intersection()", "my_set.intersection(other)"),
 
            new("data", "Tuples", "indexing", "value = my_tuple[0]"),
            new("data", "Tuples", "unpacking", "a, b = my_tuple"),
 
            // ==================== LVL 6: TEXT ====================
            new("text", "Creation", "str()", "text = str(value)"),
            new("text", "Creation", "f-string", "text = f\"value: {x}\""),
 
            new("text", "Manipulation", "upper()", "text.upper()"),
            new("text", "Manipulation", "lower()", "text.lower()"),
            new("text", "Manipulation", "strip()", "text.strip()"),
            new("text", "Manipulation", "replace()", "text.replace(\"a\", \"b\")"),
            new("text", "Manipulation", "split()", "text.split(\",\")"),
            new("text", "Manipulation", "join()", "\", \".join(parts)"),
 
            new("text", "Search", "find()", "text.find(\"sub\")"),
            new("text", "Search", "index()", "text.index(\"sub\")"),
            new("text", "Search", "startswith()", "text.startswith(\"a\")"),
            new("text", "Search", "endswith()", "text.endswith(\"z\")"),
            new("text", "Search", "in", "if \"a\" in text:", requiresBody: true),
 
            new("text", "Formatting", "format()", "text.format(x)"),
            new("text", "Formatting", "f-string", "text = f\"{x:.2f}\""),
 
            // ==================== LVL 7: MATH ====================
            new("math", "Arithmetic", "+", "result = a + b"),
            new("math", "Arithmetic", "-", "result = a - b"),
            new("math", "Arithmetic", "*", "result = a * b"),
            new("math", "Arithmetic", "/", "result = a / b"),
            new("math", "Arithmetic", "//", "result = a // b"),
            new("math", "Arithmetic", "%", "result = a % b"),
            new("math", "Arithmetic", "**", "result = a ** b"),
 
            new("math", "Built-in Math", "abs()", "abs(x)"),
            new("math", "Built-in Math", "round()", "round(x, 2)"),
            new("math", "Built-in Math", "min()", "min(a, b)"),
            new("math", "Built-in Math", "max()", "max(a, b)"),
            new("math", "Built-in Math", "sum()", "sum(values)"),
 
            new("math", "Random", "random()", "random.random()"),
            new("math", "Random", "randint()", "random.randint(1, 10)"),
            new("math", "Random", "choice()", "random.choice(items)"),
            new("math", "Random", "shuffle()", "random.shuffle(items)"),
 
            new("math", "Advanced", "sin()", "math.sin(x)"),
            new("math", "Advanced", "cos()", "math.cos(x)"),
            new("math", "Advanced", "tan()", "math.tan(x)"),
            new("math", "Advanced", "sqrt()", "math.sqrt(x)"),
 
            // ==================== LVL 8: FILES ====================
            new("files", "Text Files", "open()", "f = open(\"file.txt\")"),
            new("files", "Text Files", "read()", "text = f.read()"),
            new("files", "Text Files", "readline()", "line = f.readline()"),
            new("files", "Text Files", "write()", "f.write(text)"),
            new("files", "Text Files", "append()", "f = open(\"file.txt\", \"a\")"),
 
            new("files", "Binary Files", "rb", "f = open(\"file.bin\", \"rb\")"),
            new("files", "Binary Files", "wb", "f = open(\"file.bin\", \"wb\")"),
            new("files", "Binary Files", "readbytes()", "data = f.read()"),
            new("files", "Binary Files", "writebytes()", "f.write(data)"),
 
            new("files", "File System", "os.path.exists", "os.path.exists(path)"),
            new("files", "File System", "os.remove", "os.remove(path)"),
            new("files", "File System", "os.rename", "os.rename(old, new_name)"),
            new("files", "File System", "os.listdir", "os.listdir(path)"),
 
            new("files", "Paths", "join()", "os.path.join(a, b)"),
            new("files", "Paths", "split()", "os.path.split(path)"),
            new("files", "Paths", "basename()", "os.path.basename(path)"),
 
            // ==================== LVL 9: UI ====================
            new("ui", "Window", "create window", "window = Window()"),
            new("ui", "Window", "show", "window.show()"),
            new("ui", "Window", "hide", "window.hide()"),
 
            new("ui", "Controls", "button", "button = Button()"),
            new("ui", "Controls", "label", "label = Label()"),
            new("ui", "Controls", "textbox", "textbox = TextBox()"),
            new("ui", "Controls", "checkbox", "checkbox = Checkbox()"),
            new("ui", "Controls", "slider", "slider = Slider()"),
 
            new("ui", "Layout", "grid", "layout = Grid()"),
            new("ui", "Layout", "vertical", "layout = VBox()"),
            new("ui", "Layout", "horizontal", "layout = HBox()"),
 
            new("ui", "Events", "click", "def on_click():", requiresBody: true),
            new("ui", "Events", "hover", "def on_hover():", requiresBody: true),
            new("ui", "Events", "change", "def on_change():", requiresBody: true),
 
            // ==================== LVL 10: TIME ====================
            new("time", "Current", "now()", "timestamp = datetime.now()"),
            new("time", "Current", "timestamp()", "t = time.time()"),
 
            new("time", "Sleep", "sleep()", "time.sleep(1)"),
 
            new("time", "Formatting", "strftime()", "text = now.strftime(\"%Y-%m-%d\")"),
            new("time", "Formatting", "parse", "date = datetime.strptime(text, \"%Y-%m-%d\")"),
 
            // ==================== LVL 11: SYSTEM ====================
            new("system", "OS", "platform", "sys.platform"),
            new("system", "OS", "environment", "os.environ"),
 
            new("system", "Process", "exit()", "sys.exit()"),
            new("system", "Process", "argv", "args = sys.argv"),
 
            new("system", "Clipboard", "copy", "clipboard.copy(text)"),
            new("system", "Clipboard", "paste", "text = clipboard.paste()"),
 
            // ==================== LVL 12: ADVANCED ====================
            new("advanced", "Imports", "import", "import module"),
            new("advanced", "Imports", "from", "from module import name"),
 
            new("advanced", "Async", "async", "async def handler():", requiresBody: true),
            new("advanced", "Async", "await", "await task()"),
 
            new("advanced", "Generators", "yield", "yield value"),
 
            new("advanced", "Typing", "type hints", "x: int = 0"),
            new("advanced", "Typing", "Optional", "x: Optional[int] = None"),
            new("advanced", "Typing", "List[T]", "x: List[int] = []"),
 
            new("advanced", "Reflection", "getattr", "getattr(obj, \"name\")"),
            new("advanced", "Reflection", "setattr", "setattr(obj, \"name\", value)"),
            new("advanced", "Reflection", "hasattr", "hasattr(obj, \"name\")"),
 
            new("advanced", "Memory / Internals", "gc", "gc.collect()"),
            new("advanced", "Memory / Internals", "sys", "sys.exit()"),
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
        public const float NotchH = 5f;
        public const float NotchSlant = 4f;
        public const float InlineRowH = 20f;
        public const float MinBlockW = 46f;
        public const float IconSize = 13f;
        public const float IconGap = 6f;
 
        public static Font BlockFont = new(FontFamily.GenericSansSerif, 9.75f, FontStyle.Bold);
        public static Font InlineFont = new(FontFamily.GenericSansSerif, 9.5f, FontStyle.Regular);
 
        /// <summary>Called once at startup (after AppAssets.LoadFont) to switch block text over to the custom UI font, if one was found.</summary>
        public static void ApplyCustomFont(FontFamily family)
        {
            BlockFont = new Font(family, 9.75f, FontStyle.Bold);
            InlineFont = new Font(family, 9.5f, FontStyle.Regular);
        }
 
        // =====================================================================
        // PASS 1: measure (bottom-up), caches sizes on each BlockNode
        // =====================================================================
 
        /// <summary>Extra vertical breathing room placed above a "starter" block (Hat, or a class/def C-block) so separate scripts don't look welded together - skipped for the very first block in a stack.</summary>
        public const float StarterGap = 18f;
 
        internal static bool IsStarter(BlockNode b) => b.Shape == BlockShape.Hat || (b.Shape == BlockShape.CBlock && b.HatTop);
 
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
        /// Adds a small rounded fillet at an axis-aligned 90-degree turn: the
        /// path arrives at 'corner' heading in direction (dirInX,dirInY) and
        /// should leave heading in direction (dirOutX,dirOutY). Caller must
        /// have already drawn the path up to exactly (corner - dirIn*r)
        /// first (see StepCorner). Returns the point (corner + dirOut*r)
        /// where the next straight segment continues from.
        /// </summary>
        private static PointF AddFilletCorner(GraphicsPath p, PointF corner, float dirInX, float dirInY, float dirOutX, float dirOutY, float r)
        {
            var p2 = new PointF(corner.X + dirOutX * r, corner.Y + dirOutY * r);
            var center = new PointF(corner.X - dirInX * r + dirOutX * r, corner.Y - dirInY * r + dirOutY * r);
 
            float startAngle = AngleOfDir(-dirOutX, -dirOutY);
            float endAngle = AngleOfDir(dirInX, dirInY);
            float sweep = endAngle - startAngle;
            if (sweep > 180f) sweep -= 360f;
            if (sweep < -180f) sweep += 360f;
            if (Math.Abs(sweep) < 45f || Math.Abs(sweep) > 135f) sweep = sweep >= 0 ? 90f : -90f; // guard: should always be +-90 for our shapes
 
            p.AddArc(center.X - r, center.Y - r, r * 2f, r * 2f, startAngle, sweep);
            return p2;
        }
 
        private static float AngleOfDir(float dx, float dy)
        {
            if (dy > 0.5f) return 90f;
            if (dy < -0.5f) return 270f;
            if (dx > 0.5f) return 0f;
            return 180f;
        }
 
        /// <summary>Draws a straight line up to r-before 'corner', then fillets the turn. Returns the new cursor position.</summary>
        private static PointF StepCorner(GraphicsPath p, PointF cursor, PointF corner, float dirInX, float dirInY, float dirOutX, float dirOutY, float r)
        {
            var p1 = new PointF(corner.X - dirInX * r, corner.Y - dirInY * r);
            p.AddLine(cursor, p1);
            return AddFilletCorner(p, corner, dirInX, dirInY, dirOutX, dirOutY, r);
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
        /// and a tab on the bottom. Every bar-to-bar step is a small rounded
        /// fillet rather than a sharp right angle, so top/middle/bottom bars
        /// all read as consistently "squircle" rather than boxy.
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
            PointF cursor;
 
            if (b.HatTop)
            {
                p.AddArc(new RectangleF(x, y, b.BarW[0], HatBulge * 2f), 180, 180);
                cursor = new PointF(headerRight, y + HatBulge);
            }
            else
            {
                p.AddArc(x, y, d, d, 180, 90);
                cursor = new PointF(x + r, y);
 
                if (fitsHeader)
                {
                    p.AddLine(cursor, new PointF(x + NotchX, y));
                    p.AddLine(new PointF(x + NotchX, y), new PointF(x + NotchX + NotchSlant, y + NotchH));
                    p.AddLine(new PointF(x + NotchX + NotchSlant, y + NotchH), new PointF(x + NotchX + NotchW - NotchSlant, y + NotchH));
                    p.AddLine(new PointF(x + NotchX + NotchW - NotchSlant, y + NotchH), new PointF(x + NotchX + NotchW, y));
                    cursor = new PointF(x + NotchX + NotchW, y);
                }
 
                p.AddArc(headerRight - d, y, d, d, 270, 90);
                cursor = new PointF(headerRight, y + r);
            }
 
            float curY = y + b.HeaderH;
            float curRight = headerRight;
 
            for (int m = 0; m < b.Mouths.Count; m++)
            {
                float mouthBottom = curY + b.MouthH[m];
                float sr = Math.Max(0f, Math.Min(5f, Math.Min(b.MouthH[m], Indent) * 0.4f));
 
                cursor = StepCorner(p, cursor, new PointF(curRight, curY), 0f, 1f, -1f, 0f, sr);   // down -> left
                cursor = StepCorner(p, cursor, new PointF(wallRight, curY), -1f, 0f, 0f, 1f, sr);  // left -> down
                p.AddLine(cursor, new PointF(wallRight, mouthBottom));
                cursor = new PointF(wallRight, mouthBottom);
                curY = mouthBottom;
 
                if (m < b.ContinuationHeaders.Count)
                {
                    float contRight = x + b.BarW[m + 1];
                    float contBottom = curY + b.ContinuationHeaderH[m];
                    float sr2 = Math.Max(0f, Math.Min(5f, Math.Min(b.ContinuationHeaderH[m], Indent) * 0.4f));
 
                    cursor = StepCorner(p, cursor, new PointF(wallRight, curY), 0f, 1f, 1f, 0f, sr2);   // down -> right
                    cursor = StepCorner(p, cursor, new PointF(contRight, curY), 1f, 0f, 0f, 1f, sr2);   // right -> down
                    p.AddLine(cursor, new PointF(contRight, contBottom));
                    cursor = new PointF(contRight, contBottom);
                    curY = contBottom;
                    curRight = contRight;
                }
            }
 
            float footerRight = x + b.FooterW;
            float footerBottom = curY + b.FooterH;
            bool fitsFooter = b.FooterW > NotchX + NotchW + 4;
            float srF = Math.Max(0f, Math.Min(5f, Math.Min(b.FooterH, Indent) * 0.4f));
 
            cursor = StepCorner(p, cursor, new PointF(wallRight, curY), 0f, 1f, 1f, 0f, srF);   // down -> right (wall into footer)
            cursor = StepCorner(p, cursor, new PointF(footerRight, curY), 1f, 0f, 0f, 1f, srF);  // right -> down
            p.AddLine(cursor, new PointF(footerRight, footerBottom));
 
            p.AddArc(footerRight - d, footerBottom - d, d, d, 0, 90); // bottom-right corner
 
            if (fitsFooter)
            {
                p.AddLine(new PointF(footerRight - r, footerBottom), new PointF(x + NotchX + NotchW, footerBottom));
                p.AddLine(new PointF(x + NotchX + NotchW, footerBottom), new PointF(x + NotchX + NotchW - NotchSlant, footerBottom + NotchH));
                p.AddLine(new PointF(x + NotchX + NotchW - NotchSlant, footerBottom + NotchH), new PointF(x + NotchX + NotchSlant, footerBottom + NotchH));
                p.AddLine(new PointF(x + NotchX + NotchSlant, footerBottom + NotchH), new PointF(x + NotchX, footerBottom));
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
        internal static void DrawCategoryIcon(Graphics g, string category, float x, float y, float size, Color color)
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
 
        internal static GraphicsPath RoundedRectPath(float x, float y, float w, float h, float r)
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
 
        internal static Color Darken(Color c, float amount)
        {
            int r = (int)(c.R * (1f - amount));
            int gg = (int)(c.G * (1f - amount));
            int b = (int)(c.B * (1f - amount));
            return Color.FromArgb(c.A, Math.Max(0, r), Math.Max(0, gg), Math.Max(0, b));
        }
 
        internal static Color Lighten(Color c, float amount)
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
    /// <summary>One place a dragged palette block can land.</summary>
    internal readonly struct DropSlot
    {
        public readonly float Y, PreviewX, PreviewW;
        public readonly int Line;       // raw source line this slot is anchored to
        public readonly bool After;     // insert after Line (true) or before it (false)
        public readonly int IndentSpaces;
        public DropSlot(float y, float previewX, float previewW, int line, bool after, int indentSpaces)
        {
            Y = y; PreviewX = previewX; PreviewW = previewW; Line = line; After = after; IndentSpaces = indentSpaces;
        }
    }
 
    /// <summary>
    /// What's actually being dragged: text lines to insert (relative-indented,
    /// first line at column 0), and - only when moving an EXISTING chain of
    /// blocks already on the canvas - the raw source line range to remove
    /// from its old spot. A fresh drag from the palette leaves both Remove*
    /// fields null, so the drop handler just inserts without deleting
    /// anything first.
    /// </summary>
    internal sealed class DragPayload
    {
        public List<string> Lines = new();
        public int? RemoveFromLine;
        public int? RemoveToLineInclusive;
 
        /// <summary>
        /// Computes the resulting source lines if this payload were inserted
        /// at the given drop slot: removes its own original lines first (if
        /// it's a move, not a fresh palette insert) and adjusts the
        /// insertion index for that shift, then inserts the (re-indented)
        /// payload lines. Returns null for a no-op (dropped back inside its
        /// own original span). Shared by the live drag preview and the real
        /// commit-on-drop so they can never disagree with each other.
        /// </summary>
        public List<string>? ApplyTo(string[] currentLines, DropSlot slot)
        {
            var lines = new List<string>(currentLines);
            int insertAt = slot.After ? slot.Line + 1 : slot.Line;
 
            if (RemoveFromLine.HasValue && RemoveToLineInclusive.HasValue)
            {
                int rf = RemoveFromLine.Value, rt = RemoveToLineInclusive.Value;
                if (insertAt > rf && insertAt <= rt + 1) return null; // dropped back inside its own span
 
                if (rf >= 0 && rf < lines.Count)
                {
                    int count = Math.Min(rt - rf + 1, lines.Count - rf);
                    lines.RemoveRange(rf, count);
                    if (insertAt > rt) insertAt -= count;
                }
            }
 
            insertAt = Math.Max(0, Math.Min(insertAt, lines.Count));
            string indent = new string(' ', Math.Max(0, slot.IndentSpaces));
            for (int k = 0; k < Lines.Count; k++)
                lines.Insert(insertAt + k, indent + Lines[k]);
 
            return lines;
        }
    }
 
    internal sealed class BlockCanvas : Panel
    {
        public List<BlockNode> Script = new();
        private List<BlockNode> _committedScript = new();
        private string[] _committedRawLines = Array.Empty<string>();
        private const float MarginX = 20f, MarginY = 20f;
 
        /// <summary>Fired on a successful drop: the dragged payload, and where to insert it.</summary>
        public event Action<DragPayload, DropSlot>? BlockDropped;
 
        private DropSlot? _preview;
        private DragPayload? _dragPreviewPayload;
 
        public BlockCanvas()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScroll = true;
            BackColor = Color.White;
            AllowDrop = true;
        }
 
        public void SetScript(List<BlockNode> script, string[] rawLines)
        {
            _committedScript = script;
            _committedRawLines = rawLines;
            ApplyDisplayScript(script);
        }
 
        private void ApplyDisplayScript(List<BlockNode> script)
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
 
            // While dragging, Script has ALREADY been swapped (see OnDragOver)
            // for the real would-be result of the drop - the rest of the
            // script genuinely reflows to make room, live, rather than just
            // showing a static overlay on top of the unchanged original.
            Renderer.DrawStack(Script, g, 0, 0);
 
            if (_preview.HasValue)
            {
                var d = _preview.Value;
                using var pen = new Pen(Color.FromArgb(230, 40, 160, 40), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(pen, d.PreviewX, d.Y, d.PreviewX + d.PreviewW, d.Y);
                using var dot = new SolidBrush(Color.FromArgb(230, 40, 160, 40));
                g.FillEllipse(dot, d.PreviewX - 4f, d.Y - 4f, 8f, 8f);
            }
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
 
        // ---- drag & drop -----------------------------------------------------
 
        private PointF ToContentPoint(int screenX, int screenY)
        {
            var p = PointToClient(new Point(screenX, screenY));
            return new PointF(p.X - (AutoScrollPosition.X + MarginX), p.Y - (AutoScrollPosition.Y + MarginY));
        }
 
        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            e.Effect = (e.Data?.GetDataPresent(typeof(DragPayload)) ?? false) ? DragDropEffects.Copy : DragDropEffects.None;
        }
 
        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            if (e.Data == null || !e.Data.GetDataPresent(typeof(DragPayload)))
            {
                e.Effect = DragDropEffects.None;
                _preview = null;
                _dragPreviewPayload = null;
                ApplyDisplayScript(_committedScript);
                return;
            }
            e.Effect = DragDropEffects.Copy;
            var pt = ToContentPoint(e.X, e.Y);
            _preview = ComputeDropSlot(pt.X, pt.Y);
            _dragPreviewPayload = e.Data.GetData(typeof(DragPayload)) as DragPayload;
 
            // Live reflow: actually build and show what the script would look
            // like if dropped HERE right now, rather than a static overlay on
            // top of the unchanged original - the rest of the blocks visibly
            // shift to make room, same as the real drop will produce.
            if (_dragPreviewPayload != null && _preview.HasValue)
            {
                var previewLines = _dragPreviewPayload.ApplyTo(_committedRawLines, _preview.Value);
                if (previewLines != null)
                {
                    try { ApplyDisplayScript(ScriptParser.Parse(string.Join("\n", previewLines))); }
                    catch { ApplyDisplayScript(_committedScript); }
                }
                else
                {
                    ApplyDisplayScript(_committedScript);
                }
            }
            else
            {
                ApplyDisplayScript(_committedScript);
            }
        }
 
        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            _preview = null;
            _dragPreviewPayload = null;
            ApplyDisplayScript(_committedScript);
        }
 
        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            _preview = null;
            _dragPreviewPayload = null;
            if (e.Data == null || !e.Data.GetDataPresent(typeof(DragPayload))) { ApplyDisplayScript(_committedScript); return; }
            if (e.Data.GetData(typeof(DragPayload)) is not DragPayload payload) { ApplyDisplayScript(_committedScript); return; }
 
            var pt = ToContentPoint(e.X, e.Y);
            var slot = ComputeDropSlot(pt.X, pt.Y);
            if (slot.HasValue) BlockDropped?.Invoke(payload, slot.Value);
            // BlockDropped's handler re-parses the real source and calls
            // SetScript, which updates _committedScript - no need to revert
            // here, that call already supersedes the live-preview display.
        }
 
        // ---- picking up an EXISTING block (and everything chained below it) ----
 
        private Point _mouseDownScreenPt;
        private bool _armedForChainDrag;
        private (List<BlockNode> stack, int index)? _armedChain;
 
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) { _armedForChainDrag = false; return; }
            float cx = e.X - (AutoScrollPosition.X + MarginX);
            float cy = e.Y - (AutoScrollPosition.Y + MarginY);
            _armedChain = HitTestChain(Script, 0f, 0, cx, cy);
            _mouseDownScreenPt = Cursor.Position;
            _armedForChainDrag = _armedChain.HasValue;
        }
 
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_armedForChainDrag || e.Button != MouseButtons.Left || _armedChain == null) return;
            var cur = Cursor.Position;
            if (Math.Abs(cur.X - _mouseDownScreenPt.X) < 5 && Math.Abs(cur.Y - _mouseDownScreenPt.Y) < 5) return;
            _armedForChainDrag = false;
 
            var (stack, index) = _armedChain.Value;
            var chain = stack.Skip(index).ToList();
            if (chain.Count == 0 || _committedRawLines.Length == 0) return;
 
            int rf = chain[0].SourceLine;
            int rt = chain[^1].EndLine;
            if (rf < 0 || rt < rf || rt >= _committedRawLines.Length) return;
 
            int baseIndent = CountLeadingSpaces(_committedRawLines[rf]);
            var relLines = new List<string>();
            for (int ln = rf; ln <= rt; ln++)
            {
                string raw = _committedRawLines[ln].Replace("\t", "    ");
                int lead = CountLeadingSpaces(raw);
                int keep = Math.Max(0, lead - baseIndent);
                relLines.Add(new string(' ', keep) + raw.TrimStart(' '));
            }
 
            var payload = new DragPayload { Lines = relLines, RemoveFromLine = rf, RemoveToLineInclusive = rt };
            DoDragDrop(payload, DragDropEffects.Move);
        }
 
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _armedForChainDrag = false;
            _armedChain = null;
        }
 
        private static int CountLeadingSpaces(string s)
        {
            int n = 0;
            while (n < s.Length && s[n] == ' ') n++;
            return n;
        }
 
        /// <summary>
        /// Finds which block (and which stack it lives in) the cursor is
        /// over, preferring the deepest nested match - so clicking a block
        /// inside a class body picks up that inner block, not the class
        /// itself. Returns the CONTAINING stack and the clicked block's index
        /// in it, so the caller can grab that block plus everything chained
        /// after it in the same stack (matching how picking up a block in
        /// Scratch takes the rest of the stack below it along).
        /// </summary>
        private static (List<BlockNode> stack, int index)? HitTestChain(List<BlockNode> stack, float startY, int depth, float cx, float cy)
        {
            float curY = startY;
            for (int i = 0; i < stack.Count; i++)
            {
                var b = stack[i];
                if (i > 0 && Renderer.IsStarter(b)) curY += Renderer.StarterGap;
                float top = curY, bottom = curY + b.H;
 
                if (cy >= top && cy < bottom)
                {
                    if (b.Shape == BlockShape.CBlock)
                    {
                        float mouthY = curY + b.HeaderH;
                        for (int m = 0; m < b.Mouths.Count; m++)
                        {
                            var childHit = HitTestChain(b.Mouths[m], mouthY, depth + 1, cx, cy);
                            if (childHit.HasValue) return childHit;
                            mouthY += b.MouthH[m];
                            if (m < b.ContinuationHeaders.Count) mouthY += b.ContinuationHeaderH[m];
                        }
                    }
                    return (stack, i);
                }
                curY += b.H;
            }
            return null;
        }
 
        /// <summary>
        /// Finds the nearest place to insert a dropped block: walks the
        /// already-measured tree (same traversal DrawStack uses, so the
        /// preview lines up with what's actually on screen) collecting one
        /// candidate per block boundary, then picks whichever is closest to
        /// the cursor - preferring the deepest nesting level the cursor's X
        /// position plausibly reaches into (a rough "am I indented enough to
        /// be inside this body" check, not exact hit-testing of the mouth's
        /// true silhouette).
        /// </summary>
        private DropSlot? ComputeDropSlot(float cx, float cy)
        {
            var candidates = new List<DropSlot>();
            CollectDropSlots(_committedScript, 0f, 0, candidates);
            if (candidates.Count == 0) return null;
 
            const float indentTolerance = 10f;
            var reachable = candidates.Where(c => cx >= c.PreviewX - indentTolerance - Renderer.Indent).ToList();
            var pool = reachable.Count > 0 ? reachable : candidates;
 
            DropSlot best = pool[0];
            float bestDist = Math.Abs(cy - best.Y);
            foreach (var c in pool)
            {
                float dist = Math.Abs(cy - c.Y);
                if (dist < bestDist) { bestDist = dist; best = c; }
            }
            return best;
        }
 
        private static void CollectDropSlots(List<BlockNode> stack, float startY, int depth, List<DropSlot> outList)
        {
            float previewX = depth * Renderer.Indent;
            float curY = startY;
 
            for (int i = 0; i < stack.Count; i++)
            {
                var b = stack[i];
                if (i > 0 && Renderer.IsStarter(b)) curY += Renderer.StarterGap;
 
                outList.Add(new DropSlot(curY, previewX, Math.Max(b.W, 60f), b.SourceLine, after: false, indentSpaces: depth * 4));
 
                if (b.Shape == BlockShape.CBlock)
                {
                    float mouthY = curY + b.HeaderH;
                    for (int m = 0; m < b.Mouths.Count; m++)
                    {
                        if (b.Mouths[m].Count == 0)
                        {
                            outList.Add(new DropSlot(mouthY + 6f, (depth + 1) * Renderer.Indent, 60f, b.SourceLine, after: true, indentSpaces: (depth + 1) * 4));
                        }
                        else
                        {
                            CollectDropSlots(b.Mouths[m], mouthY, depth + 1, outList);
                            var lastChild = b.Mouths[m][^1];
                            outList.Add(new DropSlot(mouthY + b.MouthH[m], (depth + 1) * Renderer.Indent, 60f, lastChild.EndLine, after: true, indentSpaces: (depth + 1) * 4));
                        }
                        mouthY += b.MouthH[m];
                        if (m < b.ContinuationHeaders.Count) mouthY += b.ContinuationHeaderH[m];
                    }
                }
 
                curY += b.H;
            }
 
            // trailing slot: append after everything in this stack
            outList.Add(new DropSlot(curY, previewX, 60f,
                stack.Count > 0 ? stack[^1].EndLine : -1, after: true, indentSpaces: depth * 4));
        }
    }
 
    // =========================================================================
    // Asset loading: an optional "base/" folder next to the .exe can supply a
    // custom UI font (base/fonts/Lucida Grande.ttf), category icons
    // (base/icons/<category>.png, e.g. flow.png, variables.png, ...), and UI
    // sounds (base/sounds/<name>.wav). Everything here is written to degrade
    // gracefully - none of these files exist yet, so every lookup falls back
    // to what the app already does (GenericSansSerif, the hand-drawn vector
    // icons, silence) until the folder is actually populated.
    // =========================================================================
    internal static class AppAssets
    {
        private static readonly string BaseDir = Path.Combine(AppContext.BaseDirectory, "base");
        private static readonly PrivateFontCollection FontCollection = new();
        private static readonly Dictionary<string, Image?> IconCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, SoundPlayer?> SoundCache = new(StringComparer.OrdinalIgnoreCase);
 
        public static FontFamily? CustomFamily { get; private set; }
 
        /// <summary>Call once at startup. Silently no-ops if base/fonts/Lucida Grande.ttf isn't there.</summary>
        public static void LoadFont()
        {
            try
            {
                string path = Path.Combine(BaseDir, "fonts", "Lucida Grande.ttf");
                if (File.Exists(path))
                {
                    FontCollection.AddFontFile(path);
                    if (FontCollection.Families.Length > 0) CustomFamily = FontCollection.Families[0];
                }
            }
            catch
            {
                CustomFamily = null; // a corrupt/locked font file should never crash startup
            }
        }
 
        /// <summary>The app's UI font family - the custom one if base/fonts supplied it, otherwise the same generic sans-serif used before.</summary>
        private static readonly FontFamily ArialOrFallback = TryGetFamily("Arial") ?? FontFamily.GenericSansSerif;
 
        private static FontFamily? TryGetFamily(string name)
        {
            try
            {
                var f = new FontFamily(name);
                return f;
            }
            catch
            {
                return null;
            }
        }
 
        public static FontFamily UiFamily => CustomFamily ?? ArialOrFallback;
 
        public static Font UiFont(float size, FontStyle style = FontStyle.Regular) => new(UiFamily, size, style);
 
        /// <summary>Loads base/icons/&lt;category&gt;.png, caching the result (including the "not found" miss). Returns null if it isn't there, so callers fall back to the vector icon.</summary>
        public static Image? Icon(string category)
        {
            if (IconCache.TryGetValue(category, out var cached)) return cached;
            Image? img = null;
            try
            {
                string path = Path.Combine(BaseDir, "icons", category + ".png");
                if (File.Exists(path)) img = Image.FromFile(path);
            }
            catch
            {
                img = null; // a malformed PNG should never crash the palette
            }
            IconCache[category] = img;
            return img;
        }
 
        /// <summary>Plays base/sounds/&lt;name&gt;.wav if present; silently does nothing otherwise (including if the file is missing, locked, or not actually a WAV).</summary>
        public static void PlaySound(string name)
        {
            try
            {
                if (!SoundCache.TryGetValue(name, out var player))
                {
                    string path = Path.Combine(BaseDir, "sounds", name + ".wav");
                    player = File.Exists(path) ? new SoundPlayer(path) : null;
                    SoundCache[name] = player;
                }
                player?.Play();
            }
            catch
            {
                // never let a bad sound file interrupt an edit
            }
        }
    }
 
    // =========================================================================
    // Palette UI: a left-side panel of draggable block "stencils", grouped
    // into the same categories as the classifier, styled after the reference
    // (Stencyl-style) block palette - category buttons up top, a search box,
    // and a scrollable list of blocks below. Each stencil is a REAL rendered
    // block preview (built by feeding its template through the same parser
    // and renderer the canvas uses), not a separate mockup drawing, so the
    // palette and the canvas always look identical.
    // =========================================================================
 
    /// <summary>One draggable block preview in the palette.</summary>
    internal sealed class StencilTile : Panel
    {
        public readonly PaletteItem Item;
        private readonly List<BlockNode> _previewScript;
        private Point _mouseDownPt;
        private bool _armed;
 
        public StencilTile(PaletteItem item)
        {
            Item = item;
            string src = item.Template + (item.RequiresBody ? "\n    pass" : "");
            _previewScript = ScriptParser.Parse(src);
 
            // Measured ONCE here, at construction, so the tile is sized to the
            // block's real full-scale dimensions - no shrink-to-fit scaling,
            // and no dependency on a container width that might not have
            // settled yet (that dependency was the actual bug behind tiles
            // looking tiny and then growing after a few category clicks).
            float naturalW, naturalH;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
                Renderer.MeasureStack(_previewScript, g, out naturalW, out naturalH);
 
            Width = (int)Math.Ceiling(naturalW) + 16;
            Height = (int)Math.Ceiling(naturalH) + 12;
            Margin = new Padding(6, 4, 6, 4);
            Cursor = Cursors.Hand;
            BackColor = Color.White;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }
 
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var border = new Pen(Color.FromArgb(225, 225, 225), 1f);
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
 
            try
            {
                Renderer.DrawStack(_previewScript, g, 8, 6); // always full scale - no shrinking
            }
            catch
            {
                using var f = new SolidBrush(Color.Gray);
                g.DrawString(Item.Label, SystemFonts.DefaultFont, f, 6, 6);
            }
        }
 
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _mouseDownPt = e.Location;
            _armed = e.Button == MouseButtons.Left;
        }
 
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_armed || e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - _mouseDownPt.X) < 4 && Math.Abs(e.Y - _mouseDownPt.Y) < 4) return;
            _armed = false;
            var payload = new DragPayload { Lines = new List<string> { Item.Template } };
            if (Item.RequiresBody) payload.Lines.Add("    pass");
            DoDragDrop(payload, DragDropEffects.Copy);
        }
 
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _armed = false;
        }
    }
 
    /// <summary>
    /// A category button styled after the reference screenshot: a rounded
    /// ("squircle") tile with a diagonal gradient fill in the category's
    /// color, an icon (loaded from base/icons/&lt;category&gt;.png if present,
    /// otherwise the same hand-drawn vector glyph used elsewhere), and a
    /// label underneath. A thicker ring marks the currently-selected category.
    /// </summary>
    internal sealed class CategoryButton : Control
    {
        public readonly string CategoryKey;
        private readonly string _display;
        private readonly Color _color;
        public bool Selected;
 
        public CategoryButton(string key, string display, Color color)
        {
            CategoryKey = key;
            _display = display;
            _color = color;
            Width = 92;
            Height = 58;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
 
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
 
            var rect = new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f);
            using var path = Renderer.RoundedRectPath(rect.X, rect.Y, rect.Width, rect.Height, 12f);
 
            Color light = Renderer.Lighten(_color, 0.22f);
            Color dark = Renderer.Darken(_color, 0.20f);
            using (var grad = new LinearGradientBrush(rect, light, dark, LinearGradientMode.ForwardDiagonal))
                g.FillPath(grad, path);
 
            using (var pen = new Pen(Renderer.Darken(_color, 0.42f), Selected ? 2.5f : 1f))
                g.DrawPath(pen, path);
 
            const float iconSize = 20f;
            float iconX = (Width - iconSize) / 2f, iconY = 6f;
            var img = AppAssets.Icon(CategoryKey);
            if (img != null)
                g.DrawImage(img, new RectangleF(iconX, iconY, iconSize, iconSize));
            else
                Renderer.DrawCategoryIcon(g, CategoryKey, iconX, iconY, iconSize, Color.White);
 
            using var textBrush = new SolidBrush(Color.White);
            using var font = AppAssets.UiFont(7.75f, FontStyle.Bold);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter };
            g.DrawString(_display, font, textBrush, new RectangleF(2, iconY + iconSize + 3, Width - 4, Height - iconY - iconSize - 5), sf);
        }
    }
 
    internal sealed class PalettePanel : Panel
    {
        private readonly FlowLayoutPanel _stencilFlow;
        private readonly TextBox _search;
        private readonly Dictionary<string, CategoryButton> _categoryButtons = new();
        private string _currentCategory = "flow";
 
        public PalettePanel()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(248, 248, 248);
 
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(root);
 
            var title = new Label
            {
                Text = "Python Blocks",
                Dock = DockStyle.Top,
                Height = 28,
                Font = AppAssets.UiFont(10.5f, FontStyle.Bold),
                Padding = new Padding(8, 6, 0, 0),
            };
            root.Controls.Add(title, 0, 0);
 
            var searchWrap = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(8, 0, 8, 4) };
            _search = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Search blocks..." };
            _search.TextChanged += (s, e) => RefreshList();
            searchWrap.Controls.Add(_search);
            root.Controls.Add(searchWrap, 0, 1);
 
            var catFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(4),
            };
            foreach (var (key, display) in PaletteCatalog.Categories)
            {
                var color = PyClassifier.CategoryColors.TryGetValue(key, out var c) ? c : Color.Gray;
                var btn = new CategoryButton(key, display, color) { Margin = new Padding(3) };
                string keyCopy = key;
                btn.Click += (s, e) => { _currentCategory = keyCopy; _search.Text = ""; UpdateCategoryHighlight(); RefreshList(); };
                _categoryButtons[key] = btn;
                catFlow.Controls.Add(btn);
            }
            root.Controls.Add(catFlow, 0, 2);
 
            var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(0) };
            _stencilFlow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            scrollHost.Controls.Add(_stencilFlow);
            root.Controls.Add(scrollHost, 0, 3);
 
            UpdateCategoryHighlight();
            RefreshList();
        }
 
        private void UpdateCategoryHighlight()
        {
            foreach (var (key, btn) in _categoryButtons)
            {
                btn.Selected = key == _currentCategory;
                btn.Invalidate();
            }
        }
 
        private void RefreshList()
        {
            _stencilFlow.SuspendLayout();
            foreach (Control c in _stencilFlow.Controls) c.Dispose();
            _stencilFlow.Controls.Clear();
 
            string q = _search.Text.Trim();
            IEnumerable<PaletteItem> items = q.Length > 0
                ? PaletteCatalog.Items.Where(i =>
                    i.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    i.Template.Contains(q, StringComparison.OrdinalIgnoreCase))
                : PaletteCatalog.ForCategory(_currentCategory);
 
            foreach (var item in items)
                _stencilFlow.Controls.Add(new StencilTile(item));
 
            _stencilFlow.ResumeLayout();
        }
    }
 
    // =========================================================================
    // A RichTextBox with IDLE-style syntax highlighting. Full-document
    // re-highlight on every (debounced) edit - simple, and fast enough for
    // scripts this app's scale. WM_SETREDRAW suppresses the flicker/caret-
    // jump that repeated SelectionColor changes would otherwise cause.
    // =========================================================================
    internal sealed class PythonSyntaxBox : RichTextBox
    {
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, bool wParam, int lParam);
        private const int WM_SETREDRAW = 11;
 
        private static readonly string[] Keywords =
        {
            "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class",
            "continue", "def", "del", "elif", "else", "except", "finally", "for", "from", "global",
            "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise",
            "return", "try", "while", "with", "yield",
        };
        private static readonly Regex KeywordPattern = new(@"\b(" + string.Join("|", Keywords) + @")\b", RegexOptions.Compiled);
        private static readonly Regex DefNamePattern = new(@"\b(?:def|class)\s+(\w+)", RegexOptions.Compiled);
        private static readonly Regex StringPattern = new(
            "(?:[fFrRbB]{0,2})(?:\"\"\"(?:[^\\\\]|\\\\.)*?\"\"\"|'''(?:[^\\\\]|\\\\.)*?'''|\"(?:[^\"\\\\]|\\\\.)*\"|'(?:[^'\\\\]|\\\\.)*')",
            RegexOptions.Compiled);
        private static readonly Regex CommentPattern = new("#.*$", RegexOptions.Compiled | RegexOptions.Multiline);
 
        // Classic IDLE default theme colors.
        private static readonly Color KeywordColor = Color.FromArgb(255, 119, 0);
        private static readonly Color StringColor = Color.FromArgb(0, 128, 0);
        private static readonly Color CommentColor = Color.FromArgb(200, 0, 0);
        private static readonly Color DefNameColor = Color.FromArgb(0, 0, 205);
 
        public void ApplyHighlighting()
        {
            if (!IsHandleCreated) return;
            int selStart = SelectionStart, selLen = SelectionLength;
            SendMessage(Handle, WM_SETREDRAW, false, 0);
            try
            {
                string text = Text;
                SelectAll();
                SelectionColor = Color.Black;
 
                foreach (Match m in KeywordPattern.Matches(text)) Colorize(m.Index, m.Length, KeywordColor);
                foreach (Match m in DefNamePattern.Matches(text))
                {
                    var g = m.Groups[1];
                    Colorize(g.Index, g.Length, DefNameColor);
                }
                foreach (Match m in StringPattern.Matches(text)) Colorize(m.Index, m.Length, StringColor);
                foreach (Match m in CommentPattern.Matches(text)) Colorize(m.Index, m.Length, CommentColor);
            }
            finally
            {
                SelectionStart = Math.Min(selStart, TextLength);
                SelectionLength = Math.Min(Math.Max(0, selLen), Math.Max(0, TextLength - SelectionStart));
                SelectionColor = Color.Black;
                SendMessage(Handle, WM_SETREDRAW, true, 0);
                Invalidate();
            }
        }
 
        private void Colorize(int index, int length, Color color)
        {
            if (index < 0 || length <= 0 || index >= TextLength) return;
            length = Math.Min(length, TextLength - index);
            Select(index, length);
            SelectionColor = color;
        }
    }
 
    // =========================================================================
    // Main window
    // =========================================================================
    internal sealed class MainForm : Form
    {
        private readonly PythonSyntaxBox _input;
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
            Text = "Python Block Editor (native WinForms / GDI+, no web engine)";
            Width = 1360;
            Height = 820;
            StartPosition = FormStartPosition.CenterScreen;
            Font = AppAssets.UiFont(9f);
 
            var outerSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 400,
                FixedPanel = FixedPanel.Panel1,
            };
            Controls.Add(outerSplit);
 
            var palette = new PalettePanel();
            outerSplit.Panel1.Controls.Add(palette);
 
            var rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical, // splitter is vertical -> panels sit side by side (Canvas | Source)
                FixedPanel = FixedPanel.Panel2,
            };
            outerSplit.Panel2.Controls.Add(rightSplit);
            // Panel2 (Python Code) is fixed at 300px; SplitterDistance has to be
            // set from the container's actual width, which isn't known until
            // the form has laid out, so this is finalized in the Load handler.
 
            // ---- middle: canvas (drop target) + toolbar ----
            var canvasHost = new Panel { Dock = DockStyle.Fill };
            rightSplit.Panel1.Controls.Add(canvasHost);
 
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4) };
            canvasHost.Controls.Add(toolbar);
 
            var renderBtn = new Button { Text = "Render", Width = 80 };
            var exportBtn = new Button { Text = "Export PNG...", Width = 110 };
            var sampleBtn = new Button { Text = "Reset Sample", Width = 110 };
            var toggleSourceBtn = new Button { Text = "Hide Source", Width = 100 };
            toolbar.Controls.Add(renderBtn);
            toolbar.Controls.Add(exportBtn);
            toolbar.Controls.Add(sampleBtn);
            toolbar.Controls.Add(toggleSourceBtn);
 
            _canvas = new BlockCanvas { Dock = DockStyle.Fill };
            canvasHost.Controls.Add(_canvas);
            _canvas.BringToFront();
            toolbar.SendToBack();
 
            // ---- right: Python Code panel - directly editable, and where
            // dropped-block text edits land - collapsible via the toolbar button ----
            var sourceHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            rightSplit.Panel2.Controls.Add(sourceHost);
 
            var sourceLabel = new Label { Text = "Python Code (edit directly, or drag blocks from the left onto the canvas):", Dock = DockStyle.Top, Height = 20 };
            sourceHost.Controls.Add(sourceLabel);
 
            _input = new PythonSyntaxBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericMonospace, 9.5f),
                ScrollBars = RichTextBoxScrollBars.Both,
                AcceptsTab = true,
                Text = SampleScript,
                WordWrap = false,
            };
            sourceHost.Controls.Add(_input);
            _input.BringToFront();
            sourceLabel.SendToBack();
 
            _status = new Label { Dock = DockStyle.Bottom, Height = 20, Text = "", ForeColor = Color.DimGray };
            sourceHost.Controls.Add(_status);
            _status.BringToFront();
 
            _debounce = new System.Windows.Forms.Timer { Interval = 250 };
            _debounce.Tick += (s, e) => { _debounce.Stop(); DoRender(); _input.ApplyHighlighting(); };
 
            _input.TextChanged += (s, e) => { _debounce.Stop(); _debounce.Start(); };
            renderBtn.Click += (s, e) => DoRender();
            exportBtn.Click += (s, e) => DoExport();
            sampleBtn.Click += (s, e) => { _input.Text = SampleScript; DoRender(); };
            toggleSourceBtn.Click += (s, e) =>
            {
                rightSplit.Panel2Collapsed = !rightSplit.Panel2Collapsed;
                toggleSourceBtn.Text = rightSplit.Panel2Collapsed ? "Show Source" : "Hide Source";
            };
            _canvas.BlockDropped += HandleBlockDropped;
 
            Load += (s, e) =>
            {
                rightSplit.SplitterDistance = Math.Max(150, rightSplit.Width - 300);
                DoRender();
                _input.ApplyHighlighting();
            };
        }
 
        /// <summary>
        /// A block (or an existing chain of blocks picked up off the canvas)
        /// dropped somewhere is translated into a real text edit at the
        /// target line/indent, then the whole thing is reparsed - the source
        /// textbox stays the single source of truth, so drag-drop editing and
        /// direct text editing never fight each other or need separate sync
        /// logic. When the payload came from an existing chain (RemoveFromLine
        /// is set), its old lines are deleted first and the insertion index is
        /// adjusted for the shift; dropping it back inside its own original
        /// span is treated as a no-op rather than corrupting the source.
        /// </summary>
        private void HandleBlockDropped(DragPayload payload, DropSlot slot)
        {
            var resultLines = payload.ApplyTo(_input.Lines, slot);
            if (resultLines == null) return; // dropped back inside its own original span - no-op
 
            _input.Lines = resultLines.ToArray();
            AppAssets.PlaySound("drop");
            DoRender();
        }
 
        private void DoRender()
        {
            try
            {
                var rawLines = _input.Lines;
                var script = ScriptParser.Parse(_input.Text);
                _canvas.SetScript(script, rawLines);
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
 
