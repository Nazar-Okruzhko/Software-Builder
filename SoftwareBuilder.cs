// SoftwareBuilder – Visual Python Programming Environment
// Single-file .NET 6.0 Windows Forms Application
// Build: dotnet new console -n SoftwareBuilder -f net6.0-windows
// Replace Program.cs with this file, then: dotnet run
// Assets: base\icons\*.png, base\fonts\*.ttf, Icon1.ico next to the executable (optional)

#nullable disable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception.ToString(), "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show(e.ExceptionObject.ToString(), "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Critical Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

enum BlockShape { Hat, Stack, CBlock, Reporter, Boolean }
enum BlockCategory { Flow, Variables, Functions, Objects, Data, Text, Math, Files, UI, Time, System, Advanced }

class CategoryInfo
{
    public BlockCategory Category;
    public string Name;
    public Color Color;
    public string IconName;
    public List<SubCategory> SubCategories = new();
    public CategoryInfo(BlockCategory cat, string name, Color color, string iconName)
    { Category = cat; Name = name; Color = color; IconName = iconName; }
}

class SubCategory
{
    public string Name;
    public List<BlockDefinition> Blocks = new();
    public SubCategory(string name) => Name = name;
}

class BlockDefinition
{
    public string Label;
    public BlockShape Shape;
    public BlockCategory Category;
    public string SubCategory;
    public Color Color;
    public string PythonTemplate;
    public string[] DefaultArgs;
    public bool IsCBlock;

    public BlockDefinition(string label, BlockShape shape, BlockCategory cat, string subCat,
        Color color, string pyTemplate, bool isCBlock = false, string[] defaultArgs = null)
    {
        Label = label; Shape = shape; Category = cat; SubCategory = subCat;
        Color = color; PythonTemplate = pyTemplate; IsCBlock = isCBlock;
        DefaultArgs = defaultArgs ?? Array.Empty<string>();
    }
}

// ── Font Loader ─────────────
static class FontLoader
{
    private static PrivateFontCollection privateFonts = new();
    private static FontFamily defaultFamily;

    static FontLoader()
    {
        try
        {
            string fontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "base", "fonts");
            if (Directory.Exists(fontsDir))
                foreach (string file in Directory.GetFiles(fontsDir, "*.ttf"))
                    privateFonts.AddFontFile(file);
        }
        catch { }
        defaultFamily = privateFonts.Families.Length > 0 ? privateFonts.Families[0] : FontFamily.GenericSansSerif;
    }

    public static Font GetFont(float size, FontStyle style = FontStyle.Regular)
    {
        try { return new Font(defaultFamily, size, style, GraphicsUnit.Point); }
        catch { return new Font(FontFamily.GenericSansSerif, size, style); }
    }
}

// ── Exact Scratch 2.0 Block Paths (SVG → GDI+) ─
static class ScratchBlockPath
{
    public static GraphicsPath HatPath(int w, int h)
    {
        var path = new GraphicsPath();
        if (w <= 0 || h <= 0) return path;
        path.AddArc(0, 12, 80, 80, 180, 90);
        path.AddArc(w - 80, 10, 80, 80, 270, 90);
        path.AddLine(w, 13, w, h - 3);
        path.AddLine(w, h - 3, w - 3, h);
        path.AddLine(w - 3, h, 27, h); path.AddLine(27, h, 24, h + 3);
        path.AddLine(24, h + 3, 16, h + 3); path.AddLine(16, h + 3, 13, h);
        path.AddLine(13, h, 3, h); path.AddLine(3, h, 0, h - 3);
        path.AddLine(0, h - 3, 0, 13);
        path.CloseFigure();
        return path;
    }
    public static GraphicsPath StackPath(int w, int h) => StackOrCBlockPath(w, h, false, 0);
    public static GraphicsPath CBlockPath(int w, int h, int armTop) => StackOrCBlockPath(w, h, true, armTop);
    private static GraphicsPath StackOrCBlockPath(int w, int h, bool isCBlock, int armTop)
    {
        var p = new GraphicsPath();
        if (w <= 0 || h <= 0) return p;
        p.AddLine(w / 2 - 12, 0, w / 2, -3); p.AddLine(w / 2, -3, w / 2 + 12, 0);
        p.AddLine(w / 2 + 12, 0, w - 3, 0); p.AddLine(w - 3, 0, w, 3);
        if (isCBlock)
        {
            p.AddLine(w, 3, w, armTop - 2); p.AddLine(w, armTop - 2, w - 3, armTop);
            p.AddLine(w - 15, armTop, w - 15, h - 3); p.AddLine(w - 15, h - 3, w - 27, h - 3);
            p.AddLine(w - 27, h - 3, w - 24, h); p.AddLine(w - 24, h, w - 16, h);
            p.AddLine(w - 16, h, w - 13, h - 3); p.AddLine(w - 13, h - 3, 0, h - 3);
            p.AddLine(0, h - 3, 0, 3); p.AddLine(0, 3, 15, 3); p.AddLine(15, 3, 15, armTop);
            p.AddLine(15, armTop, 0, armTop - 2);
        }
        else
        {
            p.AddLine(w, 3, w, h - 3); p.AddLine(w, h - 3, w - 3, h);
            p.AddLine(w - 3, h, 27, h); p.AddLine(27, h, 24, h + 3);
            p.AddLine(24, h + 3, 16, h + 3); p.AddLine(16, h + 3, 13, h);
            p.AddLine(13, h, 3, h); p.AddLine(3, h, 0, h - 3); p.AddLine(0, h - 3, 0, 3);
        }
        p.CloseFigure();
        return p;
    }
    public static GraphicsPath ReporterPath(int w, int h)
    {
        var p = new GraphicsPath();
        if (w <= 0 || h <= 0) return p;
        int r = h / 2;
        p.AddArc(0, 0, r * 2, r * 2, 90, 180);
        p.AddArc(w - r * 2, 0, r * 2, r * 2, 270, 180);
        p.CloseFigure();
        return p;
    }
    public static GraphicsPath BooleanPath(int w, int h)
    {
        var p = new GraphicsPath();
        if (w <= 0 || h <= 0) return p;
        int r = h / 2;
        p.AddArc(0, 0, r * 2, r * 2, 90, 180); p.AddLine(r, 0, w - r, 0);
        p.AddArc(w - r * 2, 0, r * 2, r * 2, 270, 90); p.AddLine(w, r, w, r);
        p.AddArc(w - r * 2, h - r * 2, r * 2, r * 2, 0, 90); p.AddLine(w - r, h, r, h);
        p.AddArc(0, h - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// ── Gradient Panel (used for top, tabs, bottom) ─
class GradientPanel : Panel
{
    public Color TopColor { get; set; } = Color.FromArgb(0xFD, 0xFE, 0xFE);
    public Color BottomColor { get; set; } = Color.FromArgb(0xE6, 0xE8, 0xE8);

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, TopColor, BottomColor, LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}

// ── Squircle helper ─
static class SquircleHelper
{
    public static GraphicsPath CreateSquircle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
        path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
        path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// ── Toolbar Button ─
class ToolbarButton : Control
{
    public string IconName;
    private Image icon;
    public ToolbarButton(string text, string iconName)
    {
        IconName = iconName;
        Size = new Size(24, 24);
        Cursor = Cursors.Hand;
        LoadIcon();
    }
    private void LoadIcon()
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "base", "icons", IconName + ".png");
            if (File.Exists(path)) icon = Image.FromFile(path);
        }
        catch { }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (icon != null)
            e.Graphics.DrawImage(icon, ClientRectangle);
        else
        {
            using var font = new Font("Segoe UI", 7f);
            TextRenderer.DrawText(e.Graphics, IconName.Substring(0, 1).ToUpper(), font, ClientRectangle, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

// ── Sub‑category squircle label ─
class SubCategorySquircle : Control
{
    public string Title;
    private static readonly Font SubFont = FontLoader.GetFont(8.5f);

    public SubCategorySquircle(string title)
    {
        Title = title;
        Height = 22;
        Width = 120;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 2, Width - 1, Height - 4);
        if (r.Width <= 0 || r.Height <= 0) return;
        using var path = SquircleHelper.CreateSquircle(r, r.Height / 2);
        using var brush = new SolidBrush(Color.FromArgb(0x5C, 0x5C, 0x5C));
        g.FillPath(brush, path);
        using var textBrush = new SolidBrush(Color.White);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(Title, SubFont, textBrush, r, fmt);
    }
}

// ── Category Button (squircle, icon + text) ─
class CategoryButton : Control
{
    public CategoryInfo Info;
    public bool IsSelected;
    private Image icon;
    private static readonly Font ButtonFont = FontLoader.GetFont(8.5f, FontStyle.Bold);

    public CategoryButton(CategoryInfo info)
    {
        Info = info;
        Height = 28;
        Width = 130;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        LoadIcon();
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    private void LoadIcon()
    {
        try
        {
            string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "base", "icons");
            string file = Path.Combine(basePath, Info.IconName + ".png");
            if (File.Exists(file)) icon = Image.FromFile(file);
        }
        catch { icon = null; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle;
        r.Inflate(-1, -1);
        if (r.Width <= 0 || r.Height <= 0) return;
        Color back = IsSelected ? ControlPaint.Dark(Info.Color, 0.3f) : Info.Color;
        using var path = SquircleHelper.CreateSquircle(r, 10);
        using var brush = new LinearGradientBrush(r, ControlPaint.Light(back, 0.2f), ControlPaint.Dark(back, 0.1f), LinearGradientMode.Vertical);
        g.FillPath(brush, path);
        using var pen = new Pen(ControlPaint.Dark(back, 0.4f), 1f);
        g.DrawPath(pen, path);

        int iconSize = 16;
        int x = r.X + 4;
        if (icon != null)
        {
            g.DrawImage(icon, new Rectangle(x, r.Y + (r.Height - iconSize) / 2, iconSize, iconSize));
            x += iconSize + 3;
        }
        using var textBrush = new SolidBrush(Color.White);
        g.DrawString(Info.Name, ButtonFont, textBrush, new PointF(x, r.Y + (r.Height - ButtonFont.Height) / 2));
    }

    protected override void OnClick(EventArgs e)
    {
        var parent = Parent as TableLayoutPanel;
        if (parent != null)
        {
            foreach (Control c in parent.Controls)
                if (c is CategoryButton cb) cb.IsSelected = false;
            IsSelected = true;
            ((MainForm)FindForm())?.OnCategorySelected(Info);
        }
        Invalidate();
        base.OnClick(e);
    }
}

// ── Block palette item ─
class BlockStorageItem : Control
{
    public BlockDefinition Definition;
    public bool IsHovered;
    private static readonly Font BlockFont = FontLoader.GetFont(8.5f, FontStyle.Bold);

    public BlockStorageItem(BlockDefinition def)
    {
        Definition = def;
        Size = new Size(148, 26);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        Rectangle r = new Rectangle(2, 2, Width - 4, Height - 4);
        if (r.Width <= 0 || r.Height <= 0) return;
        Color baseColor = Definition.Color;
        if (IsHovered) baseColor = ControlPaint.Light(baseColor, 0.2f);

        GraphicsPath path = Definition.Shape switch
        {
            BlockShape.Hat => ScratchBlockPath.HatPath(r.Width, r.Height),
            BlockShape.Stack when Definition.IsCBlock => ScratchBlockPath.CBlockPath(r.Width, r.Height, 20),
            BlockShape.Stack => ScratchBlockPath.StackPath(r.Width, r.Height),
            BlockShape.Reporter => ScratchBlockPath.ReporterPath(r.Width, r.Height),
            BlockShape.Boolean => ScratchBlockPath.BooleanPath(r.Width, r.Height),
            _ => ScratchBlockPath.StackPath(r.Width, r.Height)
        };

        using var brush = new LinearGradientBrush(r, ControlPaint.Light(baseColor, 0.3f), ControlPaint.Dark(baseColor, 0.15f), LinearGradientMode.Vertical);
        g.FillPath(brush, path);
        using var pen = new Pen(ControlPaint.Dark(baseColor, 0.35f), 1f);
        g.DrawPath(pen, path);
        path.Dispose();

        var textRect = new Rectangle(r.X + 6, r.Y + 3, r.Width - 12, r.Height - 6);
        using var textBrush = new SolidBrush(IsDarkColor(baseColor) ? Color.White : Color.Black);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(Definition.Label, BlockFont, textBrush, textRect, fmt);
    }

    protected override void OnMouseEnter(EventArgs e) { IsHovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { IsHovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            var wsBlock = new WorkspaceBlock(Definition, Point.Empty);
            DoDragDrop(wsBlock, DragDropEffects.Copy);
        }
        base.OnMouseDown(e);
    }

    private static bool IsDarkColor(Color c) => (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) < 140;
}

// ── Workspace Block ─
class WorkspaceBlock : ICloneable
{
    public BlockDefinition Definition;
    public Rectangle Bounds;
    public string[] ArgValues;
    public bool IsDragging, IsSelected;

    public WorkspaceBlock(BlockDefinition def, Point loc)
    {
        Definition = def;
        Bounds = new Rectangle(loc, GetBlockSize(def));
        ArgValues = (string[])def.DefaultArgs.Clone();
    }

    public static Size GetBlockSize(BlockDefinition def)
    {
        int w = Math.Max(80, TextRenderer.MeasureText(def.Label, FontLoader.GetFont(9f, FontStyle.Bold)).Width + 40);
        if (def.Shape == BlockShape.Boolean) return new Size(Math.Max(w, 60), 26);
        if (def.Shape == BlockShape.Reporter) return new Size(Math.Max(w, 50), 24);
        if (def.IsCBlock) return new Size(Math.Max(w, 120), 70);
        return new Size(w, 26);
    }

    public object Clone()
    {
        var c = new WorkspaceBlock(Definition, Bounds.Location);
        c.ArgValues = (string[])ArgValues.Clone();
        c.Bounds = Bounds;
        c.IsSelected = IsSelected;
        return c;
    }
}

// ── Undo/Redo ─
class UndoRedoManager
{
    Stack<List<WorkspaceBlock>> undo = new(), redo = new();
    public void SaveState(List<WorkspaceBlock> b)
    {
        undo.Push(b.Select(x => (WorkspaceBlock)x.Clone()).ToList());
        redo.Clear();
        if (undo.Count > 30) undo = new Stack<List<WorkspaceBlock>>(undo.Take(30));
    }
    public List<WorkspaceBlock> Undo(List<WorkspaceBlock> cur)
    {
        if (undo.Count == 0) return null;
        redo.Push(cur.Select(x => (WorkspaceBlock)x.Clone()).ToList());
        return undo.Pop();
    }
    public List<WorkspaceBlock> Redo(List<WorkspaceBlock> cur)
    {
        if (redo.Count == 0) return null;
        undo.Push(cur.Select(x => (WorkspaceBlock)x.Clone()).ToList());
        return redo.Pop();
    }
}

// ── Workspace Panel ─
class WorkspacePanel : Panel
{
    public List<WorkspaceBlock> Blocks = new();
    public UndoRedoManager UndoRedo = new();
    public WorkspaceBlock ClipboardBlock;
    private Point dragOffset;
    private WorkspaceBlock draggingBlock;
    private bool selecting;
    private Point selectStart, selectEnd;
    private ContextMenuStrip contextMenu;
    private const int SnapDistance = 10;

    public event Action CodeChanged;

    public WorkspacePanel()
    {
        DoubleBuffered = true;
        AllowDrop = true;
        BackColor = Color.FromArgb(0xE6, 0xE8, 0xE8); // #E6E8E8
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        InitializeContextMenu();
    }

    private void InitializeContextMenu()
    {
        contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Undo", null, (s, e) => Undo());
        contextMenu.Items.Add("Redo", null, (s, e) => Redo());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Copy", null, (s, e) => CopySelected());
        contextMenu.Items.Add("Paste", null, (s, e) => PasteFromClipboard());
        contextMenu.Items.Add("Paste a Block", null, (s, e) => PasteBlock());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Arrange Blocks", null, (s, e) => ArrangeBlocks());
    }

    public void SaveUndoState() => UndoRedo.SaveState(Blocks);

    private void Undo()
    {
        var restored = UndoRedo.Undo(Blocks);
        if (restored != null) { Blocks = restored; Invalidate(); TriggerCodeUpdate(); }
    }
    private void Redo()
    {
        var restored = UndoRedo.Redo(Blocks);
        if (restored != null) { Blocks = restored; Invalidate(); TriggerCodeUpdate(); }
    }
    private void CopySelected()
    {
        var sel = Blocks.FirstOrDefault(b => b.IsSelected);
        if (sel != null) ClipboardBlock = (WorkspaceBlock)sel.Clone();
    }
    private void PasteFromClipboard()
    {
        if (ClipboardBlock == null) return;
        SaveUndoState();
        var newBlock = (WorkspaceBlock)ClipboardBlock.Clone();
        newBlock.Bounds = new Rectangle(ClipboardBlock.Bounds.X + 20, ClipboardBlock.Bounds.Y + 20,
            newBlock.Bounds.Width, newBlock.Bounds.Height);
        Blocks.Add(newBlock);
        Invalidate();
        TriggerCodeUpdate();
    }
    private void PasteBlock() => PasteFromClipboard();

    public void ArrangeBlocks()
    {
        SaveUndoState();
        int y = 10, x = 10;
        foreach (var b in Blocks.OrderBy(b => b.Bounds.Y))
        {
            b.Bounds = new Rectangle(x, y, b.Bounds.Width, b.Bounds.Height);
            y += b.Bounds.Height + 4;
        }
        Invalidate();
        TriggerCodeUpdate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        // Dot pattern
        using var dotBrush = new SolidBrush(Color.FromArgb(180, 200, 200));
        for (int x = 15; x < Width; x += 24)
            for (int y = 15; y < Height; y += 24)
                g.FillEllipse(dotBrush, x - 1, y - 1, 2, 2);

        foreach (var block in Blocks)
        {
            if (block == draggingBlock) continue;
            DrawWorkspaceBlock(g, block);
        }
        if (draggingBlock != null) DrawWorkspaceBlock(g, draggingBlock);

        if (selecting)
        {
            var rect = GetSelectionRectangle();
            if (rect.Width > 0 && rect.Height > 0)
            {
                using var selPen = new Pen(Color.DodgerBlue, 2f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(selPen, rect);
            }
        }
    }

    private void DrawWorkspaceBlock(Graphics g, WorkspaceBlock block)
    {
        var r = block.Bounds;
        if (r.Width <= 0 || r.Height <= 0) return;
        Color c = block.Definition.Color;
        if (block.IsDragging) c = Color.FromArgb(200, c);
        if (block.IsSelected) c = ControlPaint.Light(c, 0.4f);

        GraphicsPath path = block.Definition.Shape switch
        {
            BlockShape.Hat => ScratchBlockPath.HatPath(r.Width, r.Height),
            BlockShape.Stack when block.Definition.IsCBlock => ScratchBlockPath.CBlockPath(r.Width, r.Height, 20),
            BlockShape.Stack => ScratchBlockPath.StackPath(r.Width, r.Height),
            BlockShape.Reporter => ScratchBlockPath.ReporterPath(r.Width, r.Height),
            BlockShape.Boolean => ScratchBlockPath.BooleanPath(r.Width, r.Height),
            _ => ScratchBlockPath.StackPath(r.Width, r.Height)
        };

        var state = g.Save();
        g.TranslateTransform(r.X, r.Y);
        using (var fill = new LinearGradientBrush(new Rectangle(0, 0, r.Width, r.Height),
                   ControlPaint.Light(c, 0.35f), ControlPaint.Dark(c, 0.12f), LinearGradientMode.Vertical))
            g.FillPath(fill, path);
        using (var outline = new Pen(ControlPaint.Dark(c, 0.4f), block.IsSelected ? 2.5f : 1.2f))
            g.DrawPath(outline, path);
        path.Dispose();

        using var textBrush = new SolidBrush(IsDarkColor(c) ? Color.White : Color.Black);
        var font = FontLoader.GetFont(9f, FontStyle.Bold);
        var textRect = block.Definition.IsCBlock
            ? new Rectangle(6, 3, r.Width - 12, 18)
            : new Rectangle(6, 2, r.Width - 12, r.Height - 4);
        if (textRect.Width > 0 && textRect.Height > 0)
        {
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(block.Definition.Label, font, textBrush, textRect, fmt);
        }
        g.Restore(state);
    }

    private Rectangle GetSelectionRectangle() => new Rectangle(
        Math.Min(selectStart.X, selectEnd.X),
        Math.Min(selectStart.Y, selectEnd.Y),
        Math.Abs(selectStart.X - selectEnd.X),
        Math.Abs(selectStart.Y - selectEnd.Y));

    protected override void OnDragOver(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(WorkspaceBlock)))
        {
            e.Effect = DragDropEffects.Copy;
            var pt = PointToClient(new Point(e.X, e.Y));
            if (draggingBlock == null)
            {
                draggingBlock = (WorkspaceBlock)e.Data.GetData(typeof(WorkspaceBlock));
                draggingBlock = new WorkspaceBlock(draggingBlock.Definition, pt);
                draggingBlock.IsDragging = true;
                Blocks.Add(draggingBlock);
                SaveUndoState();
            }
            draggingBlock.Bounds = new Rectangle(
                pt.X - draggingBlock.Bounds.Width / 2,
                pt.Y - draggingBlock.Bounds.Height / 2,
                draggingBlock.Bounds.Width,
                draggingBlock.Bounds.Height);
            Invalidate();
        }
        base.OnDragOver(e);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        if (draggingBlock != null)
        {
            draggingBlock.IsDragging = false;
            SnapToGridAndBlocks(draggingBlock);
            draggingBlock = null;
            Invalidate();
            TriggerCodeUpdate();
        }
        base.OnDragDrop(e);
    }

    protected override void OnDragLeave(EventArgs e)
    {
        if (draggingBlock != null) { Blocks.Remove(draggingBlock); draggingBlock = null; Invalidate(); }
        base.OnDragLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            for (int i = Blocks.Count - 1; i >= 0; i--)
            {
                if (Blocks[i].Bounds.Contains(e.Location))
                {
                    if (ModifierKeys.HasFlag(Keys.Control))
                    {
                        Blocks[i].IsSelected = !Blocks[i].IsSelected;
                        Invalidate();
                        return;
                    }
                    draggingBlock = Blocks[i];
                    dragOffset = new Point(e.X - Blocks[i].Bounds.X, e.Y - Blocks[i].Bounds.Y);
                    draggingBlock.IsDragging = true;
                    if (!Blocks[i].IsSelected) { DeselectAll(); Blocks[i].IsSelected = true; }
                    Invalidate();
                    return;
                }
            }
            selecting = true;
            selectStart = e.Location;
            selectEnd = e.Location;
            DeselectAll();
            Invalidate();
        }
        else if (e.Button == MouseButtons.Right)
            contextMenu.Show(this, e.Location);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (draggingBlock != null && e.Button == MouseButtons.Left)
        {
            var b = draggingBlock.Bounds;
            b.X = e.X - dragOffset.X;
            b.Y = e.Y - dragOffset.Y;
            draggingBlock.Bounds = b;
            Invalidate();
        }
        else if (selecting && e.Button == MouseButtons.Left)
        {
            selectEnd = e.Location;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (draggingBlock != null)
        {
            draggingBlock.IsDragging = false;
            SnapToGridAndBlocks(draggingBlock);
            SaveUndoState();
            draggingBlock = null;
            Invalidate();
            TriggerCodeUpdate();
        }
        if (selecting)
        {
            selecting = false;
            var rect = GetSelectionRectangle();
            if (rect.Width > 5 && rect.Height > 5)
                foreach (var b in Blocks) b.IsSelected = rect.IntersectsWith(b.Bounds);
            Invalidate();
        }
        base.OnMouseUp(e);
    }

    private void SnapToGridAndBlocks(WorkspaceBlock block)
    {
        int grid = 24;
        Point loc = block.Bounds.Location;
        loc.X = (int)Math.Round(loc.X / (double)grid) * grid;
        loc.Y = (int)Math.Round(loc.Y / (double)grid) * grid;
        block.Bounds = new Rectangle(loc, block.Bounds.Size);

        foreach (var other in Blocks)
        {
            if (other == block) continue;
            Point otherBottomCenter = new Point(other.Bounds.X + other.Bounds.Width / 2, other.Bounds.Bottom);
            Point blockTopCenter = new Point(block.Bounds.X + block.Bounds.Width / 2, block.Bounds.Top);
            if (Math.Abs(otherBottomCenter.X - blockTopCenter.X) < SnapDistance &&
                Math.Abs(otherBottomCenter.Y - blockTopCenter.Y) < SnapDistance)
            {
                block.Bounds = new Rectangle(
                    other.Bounds.X + (other.Bounds.Width - block.Bounds.Width) / 2,
                    other.Bounds.Bottom,
                    block.Bounds.Width,
                    block.Bounds.Height);
                break;
            }
        }
    }

    private void DeselectAll() { foreach (var b in Blocks) b.IsSelected = false; }

    private void TriggerCodeUpdate() => CodeChanged?.Invoke();

    private static bool IsDarkColor(Color c) => (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) < 140;

    public void ClearAll()
    {
        SaveUndoState();
        Blocks.Clear();
        Invalidate();
        TriggerCodeUpdate();
    }

    public string GeneratePython()
    {
        if (Blocks.Count == 0) return "# Drag blocks here to build your Python program\n";
        var sorted = Blocks.OrderBy(b => b.Bounds.Y).ThenBy(b => b.Bounds.X).ToList();
        var sb = new StringBuilder();
        int lastY = -1;
        foreach (var block in sorted)
        {
            if (lastY >= 0 && block.Bounds.Y > lastY + 30) sb.AppendLine();
            string py = block.Definition.PythonTemplate;
            for (int i = 0; i < block.ArgValues.Length; i++)
                py = py.Replace("{" + i + "}", block.ArgValues[i]);
            if (block.Definition.IsCBlock)
            {
                sb.AppendLine(py);
                sb.AppendLine("    pass");
            }
            else sb.AppendLine(py);
            lastY = block.Bounds.Bottom;
        }
        return sb.ToString();
    }
}

// ── Main Form ─
class MainForm : Form
{
    private GradientPanel topPanel, tabsPanel, bottomPanel;
    private TextBox searchBox; // plain square
    private TableLayoutPanel categoryGrid;
    private FlowLayoutPanel subCategoryPanel;
    private WorkspacePanel workspacePanel;
    private RichTextBox pythonCodeBox;
    private Label statusLabel;
    private Button runButton; // we'll put run button in top panel maybe
    private List<CategoryInfo> categories;
    private CategoryInfo selectedCategory;
    private SplitContainer outerSplitter, innerSplitter;

    public MainForm()
    {
        Text = "SoftwareBuilder – Visual Python Programming";
        Size = new Size(1400, 820);
        MinimumSize = new Size(1000, 650);
        BackColor = Color.FromArgb(0xE6, 0xE8, 0xE8);
        SetAppIcon();
        InitializeCategories();
        BuildUI();
        CenterToScreen();
    }

    private void SetAppIcon() { /* ... same as before ... */ }

    // Complete 12 categories (exactly as in previous full code)
    private void InitializeCategories()
    {
        categories = new List<CategoryInfo>
        {
            new(BlockCategory.Flow, "FLOW", Color.FromArgb(0xE1,0xA9,0x1A), "flow") { SubCategories = {
                new("Execution") { Blocks = {
                    new("pass", BlockShape.Stack, BlockCategory.Flow, "Execution", Color.FromArgb(0xE1,0xA9,0x1A), "pass"),
                    new("return", BlockShape.Stack, BlockCategory.Flow, "Execution", Color.FromArgb(0xE1,0xA9,0x1A), "return {0}", false, new[]{"None"}),
                    new("yield", BlockShape.Stack, BlockCategory.Flow, "Execution", Color.FromArgb(0xE1,0xA9,0x1A), "yield {0}", false, new[]{"value"})
                }},
                new("Conditions") { Blocks = {
                    new("if", BlockShape.Stack, BlockCategory.Flow, "Conditions", Color.FromArgb(0xE1,0xA9,0x1A), "if {0}:", true, new[]{"True"}),
                    new("elif", BlockShape.Stack, BlockCategory.Flow, "Conditions", Color.FromArgb(0xE1,0xA9,0x1A), "elif {0}:", true, new[]{"True"}),
                    new("else", BlockShape.Stack, BlockCategory.Flow, "Conditions", Color.FromArgb(0xE1,0xA9,0x1A), "else:", true),
                    new("match", BlockShape.Stack, BlockCategory.Flow, "Conditions", Color.FromArgb(0xE1,0xA9,0x1A), "match {0}:", true, new[]{"value"})
                }},
                new("Loops") { Blocks = {
                    new("for", BlockShape.Stack, BlockCategory.Flow, "Loops", Color.FromArgb(0xE1,0xA9,0x1A), "for {0} in {1}:", true, new[]{"i","range(10)"}),
                    new("while", BlockShape.Stack, BlockCategory.Flow, "Loops", Color.FromArgb(0xE1,0xA9,0x1A), "while {0}:", true, new[]{"True"}),
                    new("break", BlockShape.Stack, BlockCategory.Flow, "Loops", Color.FromArgb(0xE1,0xA9,0x1A), "break"),
                    new("continue", BlockShape.Stack, BlockCategory.Flow, "Loops", Color.FromArgb(0xE1,0xA9,0x1A), "continue")
                }},
                new("Iteration Helpers") { Blocks = {
                    new("range()", BlockShape.Reporter, BlockCategory.Flow, "Iteration Helpers", Color.FromArgb(0xE1,0xA9,0x1A), "range({0})", false, new[]{"10"}),
                    new("enumerate()", BlockShape.Reporter, BlockCategory.Flow, "Iteration Helpers", Color.FromArgb(0xE1,0xA9,0x1A), "enumerate({0})", false, new[]{"list"}),
                    new("zip()", BlockShape.Reporter, BlockCategory.Flow, "Iteration Helpers", Color.FromArgb(0xE1,0xA9,0x1A), "zip({0},{1})", false, new[]{"a","b"}),
                    new("reversed()", BlockShape.Reporter, BlockCategory.Flow, "Iteration Helpers", Color.FromArgb(0xE1,0xA9,0x1A), "reversed({0})", false, new[]{"seq"})
                }},
                new("Exceptions") { Blocks = {
                    new("try", BlockShape.Stack, BlockCategory.Flow, "Exceptions", Color.FromArgb(0xE1,0xA9,0x1A), "try:", true),
                    new("except", BlockShape.Stack, BlockCategory.Flow, "Exceptions", Color.FromArgb(0xE1,0xA9,0x1A), "except {0}:", true, new[]{"Exception"}),
                    new("finally", BlockShape.Stack, BlockCategory.Flow, "Exceptions", Color.FromArgb(0xE1,0xA9,0x1A), "finally:", true),
                    new("raise", BlockShape.Stack, BlockCategory.Flow, "Exceptions", Color.FromArgb(0xE1,0xA9,0x1A), "raise {0}", false, new[]{"Exception()"})
                }}
            }},
            // ... copy the rest of the 11 categories from the previous complete code
            new(BlockCategory.Variables, "VARIABLES", Color.FromArgb(0x4A,0x6C,0xD4), "variables") { SubCategories = {
                new("Assignment") { Blocks = {
                    new("=", BlockShape.Stack, BlockCategory.Variables, "Assignment", Color.FromArgb(0x4A,0x6C,0xD4), "{0} = {1}", false, new[]{"x","0"}),
                    new("+=", BlockShape.Stack, BlockCategory.Variables, "Assignment", Color.FromArgb(0x4A,0x6C,0xD4), "{0} += {1}", false, new[]{"x","1"}),
                    new("-=", BlockShape.Stack, BlockCategory.Variables, "Assignment", Color.FromArgb(0x4A,0x6C,0xD4), "{0} -= {1}", false, new[]{"x","1"}),
                    new("*=", BlockShape.Stack, BlockCategory.Variables, "Assignment", Color.FromArgb(0x4A,0x6C,0xD4), "{0} *= {1}", false, new[]{"x","2"}),
                    new("/=", BlockShape.Stack, BlockCategory.Variables, "Assignment", Color.FromArgb(0x4A,0x6C,0xD4), "{0} /= {1}", false, new[]{"x","2"})
                }},
                new("Types") { Blocks = {
                    new("int", BlockShape.Reporter, BlockCategory.Variables, "Types", Color.FromArgb(0x4A,0x6C,0xD4), "int({0})", false, new[]{"0"}),
                    new("float", BlockShape.Reporter, BlockCategory.Variables, "Types", Color.FromArgb(0x4A,0x6C,0xD4), "float({0})", false, new[]{"0.0"}),
                    new("str", BlockShape.Reporter, BlockCategory.Variables, "Types", Color.FromArgb(0x4A,0x6C,0xD4), "str({0})", false, new[]{"\"\""}),
                    new("bool", BlockShape.Reporter, BlockCategory.Variables, "Types", Color.FromArgb(0x4A,0x6C,0xD4), "bool({0})", false, new[]{"True"}),
                    new("list", BlockShape.Reporter, BlockCategory.Variables, "Types", Color.FromArgb(0x4A,0x6C,0xD4), "list({0})", false, new[]{"[]"}),
                    new("dict", BlockShape.Reporter, BlockCategory.Variables, "Types", Color.FromArgb(0x4A,0x6C,0xD4), "dict({0})", false, new[]{"{}"})
                }},
                new("Constants") { Blocks = {
                    new("True", BlockShape.Boolean, BlockCategory.Variables, "Constants", Color.FromArgb(0x4A,0x6C,0xD4), "True"),
                    new("False", BlockShape.Boolean, BlockCategory.Variables, "Constants", Color.FromArgb(0x4A,0x6C,0xD4), "False"),
                    new("None", BlockShape.Reporter, BlockCategory.Variables, "Constants", Color.FromArgb(0x4A,0x6C,0xD4), "None")
                }},
                new("Conversion") { Blocks = {
                    new("int()", BlockShape.Reporter, BlockCategory.Variables, "Conversion", Color.FromArgb(0x4A,0x6C,0xD4), "int({0})", false, new[]{"0"}),
                    new("float()", BlockShape.Reporter, BlockCategory.Variables, "Conversion", Color.FromArgb(0x4A,0x6C,0xD4), "float({0})", false, new[]{"0"}),
                    new("str()", BlockShape.Reporter, BlockCategory.Variables, "Conversion", Color.FromArgb(0x4A,0x6C,0xD4), "str({0})", false, new[]{"0"}),
                    new("bool()", BlockShape.Reporter, BlockCategory.Variables, "Conversion", Color.FromArgb(0x4A,0x6C,0xD4), "bool({0})", false, new[]{"0"})
                }}
            }},
            new(BlockCategory.Functions, "FUNCTIONS", Color.FromArgb(0x8A,0x55,0xD7), "functions") { SubCategories = {
                new("Definition") { Blocks = {
                    new("def", BlockShape.Hat, BlockCategory.Functions, "Definition", Color.FromArgb(0x8A,0x55,0xD7), "def {0}({1}):", true, new[]{"my_func",""}),
                    new("lambda", BlockShape.Reporter, BlockCategory.Functions, "Definition", Color.FromArgb(0x8A,0x55,0xD7), "lambda {0}: {1}", false, new[]{"x","x"})
                }},
                new("Return") { Blocks = { new("return", BlockShape.Stack, BlockCategory.Functions, "Return", Color.FromArgb(0x8A,0x55,0xD7), "return {0}", false, new[]{"None"}) }},
                new("Parameters") { Blocks = {
                    new("*args", BlockShape.Reporter, BlockCategory.Functions, "Parameters", Color.FromArgb(0x8A,0x55,0xD7), "*args"),
                    new("**kwargs", BlockShape.Reporter, BlockCategory.Functions, "Parameters", Color.FromArgb(0x8A,0x55,0xD7), "**kwargs"),
                    new("default", BlockShape.Stack, BlockCategory.Functions, "Parameters", Color.FromArgb(0x8A,0x55,0xD7), "{0} = {1}", false, new[]{"param","value"})
                }},
                new("Scope") { Blocks = {
                    new("global", BlockShape.Stack, BlockCategory.Functions, "Scope", Color.FromArgb(0x8A,0x55,0xD7), "global {0}", false, new[]{"x"}),
                    new("nonlocal", BlockShape.Stack, BlockCategory.Functions, "Scope", Color.FromArgb(0x8A,0x55,0xD7), "nonlocal {0}", false, new[]{"x"})
                }},
                new("Decorators") { Blocks = {
                    new("@property", BlockShape.Stack, BlockCategory.Functions, "Decorators", Color.FromArgb(0x8A,0x55,0xD7), "@property"),
                    new("@staticmethod", BlockShape.Stack, BlockCategory.Functions, "Decorators", Color.FromArgb(0x8A,0x55,0xD7), "@staticmethod"),
                    new("@classmethod", BlockShape.Stack, BlockCategory.Functions, "Decorators", Color.FromArgb(0x8A,0x55,0xD7), "@classmethod")
                }}
            }},
            new(BlockCategory.Objects, "OBJECTS", Color.FromArgb(0x63,0x2D,0x99), "objects") { SubCategories = {
                new("Classes") { Blocks = {
                    new("class", BlockShape.Hat, BlockCategory.Objects, "Classes", Color.FromArgb(0x63,0x2D,0x99), "class {0}:", true, new[]{"MyClass"}),
                    new("self", BlockShape.Reporter, BlockCategory.Objects, "Classes", Color.FromArgb(0x63,0x2D,0x99), "self"),
                    new("__init__", BlockShape.Stack, BlockCategory.Objects, "Classes", Color.FromArgb(0x63,0x2D,0x99), "def __init__(self{0}):", true, new[]{""})
                }},
                new("Attributes") { Blocks = {
                    new("getattr", BlockShape.Reporter, BlockCategory.Objects, "Attributes", Color.FromArgb(0x63,0x2D,0x99), "getattr({0},{1})", false, new[]{"obj","'attr'"}),
                    new("setattr", BlockShape.Stack, BlockCategory.Objects, "Attributes", Color.FromArgb(0x63,0x2D,0x99), "setattr({0},{1},{2})", false, new[]{"obj","'attr'","val"})
                }},
                new("Methods") { Blocks = {
                    new("instance method", BlockShape.Stack, BlockCategory.Objects, "Methods", Color.FromArgb(0x63,0x2D,0x99), "def {0}(self):", true, new[]{"method"}),
                    new("class method", BlockShape.Stack, BlockCategory.Objects, "Methods", Color.FromArgb(0x63,0x2D,0x99), "@classmethod\ndef {0}(cls):", true, new[]{"method"}),
                    new("static method", BlockShape.Stack, BlockCategory.Objects, "Methods", Color.FromArgb(0x63,0x2D,0x99), "@staticmethod\ndef {0}():", true, new[]{"method"})
                }},
                new("Inheritance") { Blocks = {
                    new("super()", BlockShape.Reporter, BlockCategory.Objects, "Inheritance", Color.FromArgb(0x63,0x2D,0x99), "super()"),
                    new("override", BlockShape.Stack, BlockCategory.Objects, "Inheritance", Color.FromArgb(0x63,0x2D,0x99), "def {0}(self):\n    super().{0}()", true, new[]{"method"})
                }}
            }},
            new(BlockCategory.Data, "DATA", Color.FromArgb(0x5C,0xB7,0x12), "data") { SubCategories = {
                new("Lists") { Blocks = {
                    new("append()", BlockShape.Stack, BlockCategory.Data, "Lists", Color.FromArgb(0x5C,0xB7,0x12), "{0}.append({1})", false, new[]{"lst","item"}),
                    new("extend()", BlockShape.Stack, BlockCategory.Data, "Lists", Color.FromArgb(0x5C,0xB7,0x12), "{0}.extend({1})", false, new[]{"lst","[]"}),
                    new("insert()", BlockShape.Stack, BlockCategory.Data, "Lists", Color.FromArgb(0x5C,0xB7,0x12), "{0}.insert({1},{2})", false, new[]{"lst","0","item"}),
                    new("remove()", BlockShape.Stack, BlockCategory.Data, "Lists", Color.FromArgb(0x5C,0xB7,0x12), "{0}.remove({1})", false, new[]{"lst","item"}),
                    new("pop()", BlockShape.Reporter, BlockCategory.Data, "Lists", Color.FromArgb(0x5C,0xB7,0x12), "{0}.pop({1})", false, new[]{"lst","-1"}),
                    new("sort()", BlockShape.Stack, BlockCategory.Data, "Lists", Color.FromArgb(0x5C,0xB7,0x12), "{0}.sort()", false, new[]{"lst"}),
                    new("reverse()", BlockShape.Stack, BlockCategory.Data, "Lists", Color.FromArgb(0x5C,0xB7,0x12), "{0}.reverse()", false, new[]{"lst"})
                }},
                new("Dictionaries") { Blocks = {
                    new("keys()", BlockShape.Reporter, BlockCategory.Data, "Dictionaries", Color.FromArgb(0x5C,0xB7,0x12), "{0}.keys()", false, new[]{"d"}),
                    new("values()", BlockShape.Reporter, BlockCategory.Data, "Dictionaries", Color.FromArgb(0x5C,0xB7,0x12), "{0}.values()", false, new[]{"d"}),
                    new("items()", BlockShape.Reporter, BlockCategory.Data, "Dictionaries", Color.FromArgb(0x5C,0xB7,0x12), "{0}.items()", false, new[]{"d"}),
                    new("get()", BlockShape.Reporter, BlockCategory.Data, "Dictionaries", Color.FromArgb(0x5C,0xB7,0x12), "{0}.get({1})", false, new[]{"d","'key'"}),
                    new("update()", BlockShape.Stack, BlockCategory.Data, "Dictionaries", Color.FromArgb(0x5C,0xB7,0x12), "{0}.update({1})", false, new[]{"d","{}"}),
                    new("pop()", BlockShape.Reporter, BlockCategory.Data, "Dictionaries", Color.FromArgb(0x5C,0xB7,0x12), "{0}.pop({1})", false, new[]{"d","'key'"})
                }},
                new("Sets") { Blocks = {
                    new("add()", BlockShape.Stack, BlockCategory.Data, "Sets", Color.FromArgb(0x5C,0xB7,0x12), "{0}.add({1})", false, new[]{"s","item"}),
                    new("remove()", BlockShape.Stack, BlockCategory.Data, "Sets", Color.FromArgb(0x5C,0xB7,0x12), "{0}.remove({1})", false, new[]{"s","item"}),
                    new("union()", BlockShape.Reporter, BlockCategory.Data, "Sets", Color.FromArgb(0x5C,0xB7,0x12), "{0}.union({1})", false, new[]{"s1","s2"}),
                    new("intersection()", BlockShape.Reporter, BlockCategory.Data, "Sets", Color.FromArgb(0x5C,0xB7,0x12), "{0}.intersection({1})", false, new[]{"s1","s2"})
                }},
                new("Tuples") { Blocks = {
                    new("indexing", BlockShape.Reporter, BlockCategory.Data, "Tuples", Color.FromArgb(0x5C,0xB7,0x12), "{0}[{1}]", false, new[]{"tup","0"}),
                    new("unpacking", BlockShape.Stack, BlockCategory.Data, "Tuples", Color.FromArgb(0x5C,0xB7,0x12), "{0} = {1}", false, new[]{"a,b","tup"})
                }}
            }},
            new(BlockCategory.Text, "TEXT", Color.FromArgb(0xEE,0x7D,0x16), "text") { SubCategories = {
                new("Creation") { Blocks = {
                    new("str()", BlockShape.Reporter, BlockCategory.Text, "Creation", Color.FromArgb(0xEE,0x7D,0x16), "str({0})", false, new[]{"0"}),
                    new("f-string", BlockShape.Reporter, BlockCategory.Text, "Creation", Color.FromArgb(0xEE,0x7D,0x16), "f\"{0}\"", false, new[]{"{value}"})
                }},
                new("Manipulation") { Blocks = {
                    new("upper()", BlockShape.Reporter, BlockCategory.Text, "Manipulation", Color.FromArgb(0xEE,0x7D,0x16), "{0}.upper()", false, new[]{"s"}),
                    new("lower()", BlockShape.Reporter, BlockCategory.Text, "Manipulation", Color.FromArgb(0xEE,0x7D,0x16), "{0}.lower()", false, new[]{"s"}),
                    new("strip()", BlockShape.Reporter, BlockCategory.Text, "Manipulation", Color.FromArgb(0xEE,0x7D,0x16), "{0}.strip()", false, new[]{"s"}),
                    new("replace()", BlockShape.Reporter, BlockCategory.Text, "Manipulation", Color.FromArgb(0xEE,0x7D,0x16), "{0}.replace({1},{2})", false, new[]{"s","'old'","'new'"}),
                    new("split()", BlockShape.Reporter, BlockCategory.Text, "Manipulation", Color.FromArgb(0xEE,0x7D,0x16), "{0}.split({1})", false, new[]{"s","','"}),
                    new("join()", BlockShape.Reporter, BlockCategory.Text, "Manipulation", Color.FromArgb(0xEE,0x7D,0x16), "{0}.join({1})", false, new[]{"','","lst"})
                }},
                new("Search") { Blocks = {
                    new("find()", BlockShape.Reporter, BlockCategory.Text, "Search", Color.FromArgb(0xEE,0x7D,0x16), "{0}.find({1})", false, new[]{"s","'sub'"}),
                    new("index()", BlockShape.Reporter, BlockCategory.Text, "Search", Color.FromArgb(0xEE,0x7D,0x16), "{0}.index({1})", false, new[]{"s","'sub'"}),
                    new("startswith()", BlockShape.Boolean, BlockCategory.Text, "Search", Color.FromArgb(0xEE,0x7D,0x16), "{0}.startswith({1})", false, new[]{"s","'pre'"}),
                    new("endswith()", BlockShape.Boolean, BlockCategory.Text, "Search", Color.FromArgb(0xEE,0x7D,0x16), "{0}.endswith({1})", false, new[]{"s","'suf'"}),
                    new("in", BlockShape.Boolean, BlockCategory.Text, "Search", Color.FromArgb(0xEE,0x7D,0x16), "{0} in {1}", false, new[]{"'sub'","s"})
                }},
                new("Formatting") { Blocks = {
                    new("format()", BlockShape.Reporter, BlockCategory.Text, "Formatting", Color.FromArgb(0xEE,0x7D,0x16), "{0}.format({1})", false, new[]{"'{}'","val"}),
                    new("f-string", BlockShape.Reporter, BlockCategory.Text, "Formatting", Color.FromArgb(0xEE,0x7D,0x16), "f'{0}'", false, new[]{"{var}"})
                }}
            }},
            new(BlockCategory.Math, "MATH", Color.FromArgb(0x2C,0xA5,0xE2), "math") { SubCategories = {
                new("Arithmetic") { Blocks = {
                    new("+", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C,0xA5,0xE2), "({0} + {1})", false, new[]{"a","b"}),
                    new("-", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C,0xA5,0xE2), "({0} - {1})", false, new[]{"a","b"}),
                    new("*", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C,0xA5,0xE2), "({0} * {1})", false, new[]{"a","b"}),
                    new("/", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C,0xA5,0xE2), "({0} / {1})", false, new[]{"a","b"}),
                    new("//", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C,0xA5,0xE2), "({0} // {1})", false, new[]{"a","b"}),
                    new("%", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C,0xA5,0xE2), "({0} % {1})", false, new[]{"a","b"}),
                    new("**", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C,0xA5,0xE2), "({0} ** {1})", false, new[]{"a","b"})
                }},
                new("Built-in Math") { Blocks = {
                    new("abs()", BlockShape.Reporter, BlockCategory.Math, "Built-in Math", Color.FromArgb(0x2C,0xA5,0xE2), "abs({0})", false, new[]{"x"}),
                    new("round()", BlockShape.Reporter, BlockCategory.Math, "Built-in Math", Color.FromArgb(0x2C,0xA5,0xE2), "round({0})", false, new[]{"x"}),
                    new("min()", BlockShape.Reporter, BlockCategory.Math, "Built-in Math", Color.FromArgb(0x2C,0xA5,0xE2), "min({0})", false, new[]{"a,b"}),
                    new("max()", BlockShape.Reporter, BlockCategory.Math, "Built-in Math", Color.FromArgb(0x2C,0xA5,0xE2), "max({0})", false, new[]{"a,b"}),
                    new("sum()", BlockShape.Reporter, BlockCategory.Math, "Built-in Math", Color.FromArgb(0x2C,0xA5,0xE2), "sum({0})", false, new[]{"lst"})
                }},
                new("Random") { Blocks = {
                    new("random()", BlockShape.Reporter, BlockCategory.Math, "Random", Color.FromArgb(0x2C,0xA5,0xE2), "random.random()"),
                    new("randint()", BlockShape.Reporter, BlockCategory.Math, "Random", Color.FromArgb(0x2C,0xA5,0xE2), "random.randint({0},{1})", false, new[]{"0","100"}),
                    new("choice()", BlockShape.Reporter, BlockCategory.Math, "Random", Color.FromArgb(0x2C,0xA5,0xE2), "random.choice({0})", false, new[]{"lst"}),
                    new("shuffle()", BlockShape.Stack, BlockCategory.Math, "Random", Color.FromArgb(0x2C,0xA5,0xE2), "random.shuffle({0})", false, new[]{"lst"})
                }},
                new("Advanced") { Blocks = {
                    new("sin()", BlockShape.Reporter, BlockCategory.Math, "Advanced", Color.FromArgb(0x2C,0xA5,0xE2), "math.sin({0})", false, new[]{"x"}),
                    new("cos()", BlockShape.Reporter, BlockCategory.Math, "Advanced", Color.FromArgb(0x2C,0xA5,0xE2), "math.cos({0})", false, new[]{"x"}),
                    new("tan()", BlockShape.Reporter, BlockCategory.Math, "Advanced", Color.FromArgb(0x2C,0xA5,0xE2), "math.tan({0})", false, new[]{"x"}),
                    new("sqrt()", BlockShape.Reporter, BlockCategory.Math, "Advanced", Color.FromArgb(0x2C,0xA5,0xE2), "math.sqrt({0})", false, new[]{"x"})
                }}
            }},
            new(BlockCategory.Files, "FILES", Color.FromArgb(0x8B,0x5E,0x3C), "files") { SubCategories = {
                new("Text Files") { Blocks = {
                    new("open()", BlockShape.Reporter, BlockCategory.Files, "Text Files", Color.FromArgb(0x8B,0x5E,0x3C), "open({0},{1})", false, new[]{"'file.txt'","'r'"}),
                    new("read()", BlockShape.Reporter, BlockCategory.Files, "Text Files", Color.FromArgb(0x8B,0x5E,0x3C), "{0}.read()", false, new[]{"f"}),
                    new("readline()", BlockShape.Reporter, BlockCategory.Files, "Text Files", Color.FromArgb(0x8B,0x5E,0x3C), "{0}.readline()", false, new[]{"f"}),
                    new("write()", BlockShape.Stack, BlockCategory.Files, "Text Files", Color.FromArgb(0x8B,0x5E,0x3C), "{0}.write({1})", false, new[]{"f","'text'"}),
                    new("append()", BlockShape.Stack, BlockCategory.Files, "Text Files", Color.FromArgb(0x8B,0x5E,0x3C), "open({0},'a').write({1})", false, new[]{"'file.txt'","'text'"})
                }},
                new("Binary Files") { Blocks = {
                    new("rb mode", BlockShape.Reporter, BlockCategory.Files, "Binary Files", Color.FromArgb(0x8B,0x5E,0x3C), "open({0},'rb')", false, new[]{"'file.bin'"}),
                    new("wb mode", BlockShape.Reporter, BlockCategory.Files, "Binary Files", Color.FromArgb(0x8B,0x5E,0x3C), "open({0},'wb')", false, new[]{"'file.bin'"}),
                    new("readbytes()", BlockShape.Reporter, BlockCategory.Files, "Binary Files", Color.FromArgb(0x8B,0x5E,0x3C), "{0}.read()", false, new[]{"f"}),
                    new("writebytes()", BlockShape.Stack, BlockCategory.Files, "Binary Files", Color.FromArgb(0x8B,0x5E,0x3C), "{0}.write({1})", false, new[]{"f","b'data'"})
                }},
                new("File System") { Blocks = {
                    new("exists()", BlockShape.Boolean, BlockCategory.Files, "File System", Color.FromArgb(0x8B,0x5E,0x3C), "os.path.exists({0})", false, new[]{"'path'"}),
                    new("remove()", BlockShape.Stack, BlockCategory.Files, "File System", Color.FromArgb(0x8B,0x5E,0x3C), "os.remove({0})", false, new[]{"'file'"}),
                    new("rename()", BlockShape.Stack, BlockCategory.Files, "File System", Color.FromArgb(0x8B,0x5E,0x3C), "os.rename({0},{1})", false, new[]{"'old'","'new'"}),
                    new("listdir()", BlockShape.Reporter, BlockCategory.Files, "File System", Color.FromArgb(0x8B,0x5E,0x3C), "os.listdir({0})", false, new[]{"'.'"})
                }},
                new("Paths") { Blocks = {
                    new("join()", BlockShape.Reporter, BlockCategory.Files, "Paths", Color.FromArgb(0x8B,0x5E,0x3C), "os.path.join({0})", false, new[]{"'a','b'"}),
                    new("split()", BlockShape.Reporter, BlockCategory.Files, "Paths", Color.FromArgb(0x8B,0x5E,0x3C), "os.path.split({0})", false, new[]{"'path'"}),
                    new("basename()", BlockShape.Reporter, BlockCategory.Files, "Paths", Color.FromArgb(0x8B,0x5E,0x3C), "os.path.basename({0})", false, new[]{"'path'"})
                }}
            }},
            new(BlockCategory.UI, "UI", Color.FromArgb(0x0E,0x9A,0x6C), "ui") { SubCategories = {
                new("Window") { Blocks = {
                    new("create window", BlockShape.Stack, BlockCategory.UI, "Window", Color.FromArgb(0x0E,0x9A,0x6C), "root = tk.Tk()"),
                    new("show", BlockShape.Stack, BlockCategory.UI, "Window", Color.FromArgb(0x0E,0x9A,0x6C), "root.mainloop()"),
                    new("hide", BlockShape.Stack, BlockCategory.UI, "Window", Color.FromArgb(0x0E,0x9A,0x6C), "root.withdraw()")
                }},
                new("Controls") { Blocks = {
                    new("button", BlockShape.Stack, BlockCategory.UI, "Controls", Color.FromArgb(0x0E,0x9A,0x6C), "tk.Button({0},text={1})", false, new[]{"root","'Click'"}),
                    new("label", BlockShape.Stack, BlockCategory.UI, "Controls", Color.FromArgb(0x0E,0x9A,0x6C), "tk.Label({0},text={1})", false, new[]{"root","'Hello'"}),
                    new("textbox", BlockShape.Stack, BlockCategory.UI, "Controls", Color.FromArgb(0x0E,0x9A,0x6C), "tk.Entry({0})", false, new[]{"root"}),
                    new("checkbox", BlockShape.Stack, BlockCategory.UI, "Controls", Color.FromArgb(0x0E,0x9A,0x6C), "tk.Checkbutton({0},text={1})", false, new[]{"root","'Option'"}),
                    new("slider", BlockShape.Stack, BlockCategory.UI, "Controls", Color.FromArgb(0x0E,0x9A,0x6C), "tk.Scale({0},from_={1},to={2})", false, new[]{"root","0","100"})
                }},
                new("Layout") { Blocks = {
                    new("grid", BlockShape.Stack, BlockCategory.UI, "Layout", Color.FromArgb(0x0E,0x9A,0x6C), "{0}.grid(row={1},column={2})", false, new[]{"widget","0","0"}),
                    new("vertical", BlockShape.Stack, BlockCategory.UI, "Layout", Color.FromArgb(0x0E,0x9A,0x6C), "{0}.pack(side=tk.TOP)", false, new[]{"widget"}),
                    new("horizontal", BlockShape.Stack, BlockCategory.UI, "Layout", Color.FromArgb(0x0E,0x9A,0x6C), "{0}.pack(side=tk.LEFT)", false, new[]{"widget"})
                }},
                new("Events") { Blocks = {
                    new("click", BlockShape.Stack, BlockCategory.UI, "Events", Color.FromArgb(0x0E,0x9A,0x6C), "{0}.bind('<Button-1>',{1})", false, new[]{"widget","callback"}),
                    new("hover", BlockShape.Stack, BlockCategory.UI, "Events", Color.FromArgb(0x0E,0x9A,0x6C), "{0}.bind('<Enter>',{1})", false, new[]{"widget","callback"}),
                    new("change", BlockShape.Stack, BlockCategory.UI, "Events", Color.FromArgb(0x0E,0x9A,0x6C), "{0}.bind('<Modified>',{1})", false, new[]{"widget","callback"})
                }}
            }},
            new(BlockCategory.Time, "TIME", Color.FromArgb(0x2E,0x8B,0x8B), "time") { SubCategories = {
                new("Current") { Blocks = {
                    new("now()", BlockShape.Reporter, BlockCategory.Time, "Current", Color.FromArgb(0x2E,0x8B,0x8B), "datetime.now()"),
                    new("timestamp()", BlockShape.Reporter, BlockCategory.Time, "Current", Color.FromArgb(0x2E,0x8B,0x8B), "time.time()")
                }},
                new("Sleep") { Blocks = {
                    new("sleep()", BlockShape.Stack, BlockCategory.Time, "Sleep", Color.FromArgb(0x2E,0x8B,0x8B), "time.sleep({0})", false, new[]{"1"})
                }},
                new("Formatting") { Blocks = {
                    new("strftime()", BlockShape.Reporter, BlockCategory.Time, "Formatting", Color.FromArgb(0x2E,0x8B,0x8B), "{0}.strftime({1})", false, new[]{"dt","'%Y-%m-%d'"}),
                    new("parse", BlockShape.Reporter, BlockCategory.Time, "Formatting", Color.FromArgb(0x2E,0x8B,0x8B), "datetime.strptime({0},{1})", false, new[]{"'date'","'%Y-%m-%d'"})
                }}
            }},
            new(BlockCategory.System, "SYSTEM", Color.FromArgb(0x55,0x55,0x55), "system") { SubCategories = {
                new("OS") { Blocks = {
                    new("platform", BlockShape.Reporter, BlockCategory.System, "OS", Color.FromArgb(0x55,0x55,0x55), "sys.platform"),
                    new("environment", BlockShape.Reporter, BlockCategory.System, "OS", Color.FromArgb(0x55,0x55,0x55), "os.environ")
                }},
                new("Process") { Blocks = {
                    new("exit()", BlockShape.Stack, BlockCategory.System, "Process", Color.FromArgb(0x55,0x55,0x55), "sys.exit({0})", false, new[]{"0"}),
                    new("argv", BlockShape.Reporter, BlockCategory.System, "Process", Color.FromArgb(0x55,0x55,0x55), "sys.argv")
                }},
                new("Clipboard") { Blocks = {
                    new("copy", BlockShape.Stack, BlockCategory.System, "Clipboard", Color.FromArgb(0x55,0x55,0x55), "pyperclip.copy({0})", false, new[]{"text"}),
                    new("paste", BlockShape.Reporter, BlockCategory.System, "Clipboard", Color.FromArgb(0x55,0x55,0x55), "pyperclip.paste()")
                }}
            }},
            new(BlockCategory.Advanced, "ADVANCED", Color.FromArgb(0x4B,0x4A,0x60), "advanced") { SubCategories = {
                new("Imports") { Blocks = {
                    new("import", BlockShape.Stack, BlockCategory.Advanced, "Imports", Color.FromArgb(0x4B,0x4A,0x60), "import {0}", false, new[]{"module"}),
                    new("from", BlockShape.Stack, BlockCategory.Advanced, "Imports", Color.FromArgb(0x4B,0x4A,0x60), "from {0} import {1}", false, new[]{"module","name"})
                }},
                new("Async") { Blocks = {
                    new("async", BlockShape.Stack, BlockCategory.Advanced, "Async", Color.FromArgb(0x4B,0x4A,0x60), "async def {0}():", true, new[]{"func"}),
                    new("await", BlockShape.Stack, BlockCategory.Advanced, "Async", Color.FromArgb(0x4B,0x4A,0x60), "await {0}", false, new[]{"coro"})
                }},
                new("Generators") { Blocks = {
                    new("yield", BlockShape.Stack, BlockCategory.Advanced, "Generators", Color.FromArgb(0x4B,0x4A,0x60), "yield {0}", false, new[]{"value"})
                }},
                new("Typing") { Blocks = {
                    new("type hints", BlockShape.Stack, BlockCategory.Advanced, "Typing", Color.FromArgb(0x4B,0x4A,0x60), "{0}: {1} = {2}", false, new[]{"x","int","0"}),
                    new("Optional", BlockShape.Reporter, BlockCategory.Advanced, "Typing", Color.FromArgb(0x4B,0x4A,0x60), "Optional[{0}]", false, new[]{"int"}),
                    new("List[T]", BlockShape.Reporter, BlockCategory.Advanced, "Typing", Color.FromArgb(0x4B,0x4A,0x60), "List[{0}]", false, new[]{"int"})
                }},
                new("Reflection") { Blocks = {
                    new("getattr", BlockShape.Reporter, BlockCategory.Advanced, "Reflection", Color.FromArgb(0x4B,0x4A,0x60), "getattr({0},{1})", false, new[]{"obj","'attr'"}),
                    new("setattr", BlockShape.Stack, BlockCategory.Advanced, "Reflection", Color.FromArgb(0x4B,0x4A,0x60), "setattr({0},{1},{2})", false, new[]{"obj","'attr'","val"}),
                    new("hasattr", BlockShape.Boolean, BlockCategory.Advanced, "Reflection", Color.FromArgb(0x4B,0x4A,0x60), "hasattr({0},{1})", false, new[]{"obj","'attr'"})
                }},
                new("Memory") { Blocks = {
                    new("gc", BlockShape.Stack, BlockCategory.Advanced, "Memory", Color.FromArgb(0x4B,0x4A,0x60), "gc.collect()"),
                    new("sys", BlockShape.Reporter, BlockCategory.Advanced, "Memory", Color.FromArgb(0x4B,0x4A,0x60), "sys.getsizeof({0})", false, new[]{"obj"})
                }}
            }}
        };
    }

    // … (all previous code remains exactly the same up to MainForm.BuildUI)
    
    private void BuildUI()
    {
        // ── Top panel 63px ──
        topPanel = new GradientPanel { Height = 63, Dock = DockStyle.Top };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4), BackColor = Color.Transparent };
        string[] buttonNames = { "Create new", "Save Script", "Import Script", null, "Settings", "Log Viewer", "Issues", null, "Wikipedia" };
        foreach (var name in buttonNames)
        {
            if (name == null) { toolbar.Controls.Add(new Panel { Width = 40, BackColor = Color.Transparent }); continue; }
            var btn = new ToolbarButton(name, name.Replace(" ", "").ToLower());
            btn.Click += (s, e) => { /* action placeholder */ };
            toolbar.Controls.Add(btn);
        }
        // Run button at right side of top panel
        runButton = new Button { Text = "Run", FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0x5C,0xB7,0x12), ForeColor = Color.White, Font = FontLoader.GetFont(9f, FontStyle.Bold), Size = new Size(80,28), Anchor = AnchorStyles.Right | AnchorStyles.Top };
        runButton.FlatAppearance.BorderSize = 0;
        runButton.Click += (s, e) => UpdatePythonCode();
        runButton.Location = new Point(topPanel.Width - runButton.Width - 10, 18);
        topPanel.Controls.Add(runButton);
        topPanel.Controls.Add(toolbar);
        topPanel.Resize += (s, e) => runButton.Location = new Point(topPanel.Width - runButton.Width - 10, 18);
    
        // ── Tab Switching bar 22px ──
        tabsPanel = new GradientPanel { Height = 22, Dock = DockStyle.Top };
        var tabsFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0), BackColor = Color.Transparent };
        foreach (var cat in categories)
        {
            string tabText = cat.Name.Length >= 3 ? cat.Name.Substring(0, 3) : cat.Name;  // FIX: handle short names like "UI"
            var tab = new Label { Text = tabText, Font = FontLoader.GetFont(7f), AutoSize = true, Margin = new Padding(4,2,4,0), ForeColor = Color.Black, BackColor = Color.Transparent };
            tab.Click += (s, e) => OnCategorySelected(cat);
            tabsFlow.Controls.Add(tab);
        }
        tabsPanel.Controls.Add(tabsFlow);
    
        // ── Bottom panel 35px ──
        bottomPanel = new GradientPanel { Height = 35, Dock = DockStyle.Bottom };
        statusLabel = new Label { Text = "  Ready – Select a category and drag blocks.", Font = FontLoader.GetFont(8f), ForeColor = Color.FromArgb(0x44,0x44,0x44), AutoSize = true, Location = new Point(8, 8) };
        bottomPanel.Controls.Add(statusLabel);
    
        // ── Outer splitter ──
        outerSplitter = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 4 };
    
        // Left panel: Block Storage
        var storagePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0xE6,0xE8,0xE8), Padding = new Padding(4) };
        var storageTitle = new Label { Text = "Block Storage", Font = FontLoader.GetFont(10f, FontStyle.Bold), ForeColor = Color.FromArgb(0x5C,0x5C,0x5C), Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleCenter };
        storagePanel.Controls.Add(storageTitle);
        // Search (square)
        searchBox = new TextBox { Dock = DockStyle.Top, Height = 24, Font = FontLoader.GetFont(9f), BorderStyle = BorderStyle.FixedSingle };
        searchBox.TextChanged += (s, e) => PopulateSubCategories(selectedCategory);
        storagePanel.Controls.Add(searchBox);
        // Category grid
        var gridContainer = new Panel { Dock = DockStyle.Top, Height = 130, BackColor = Color.Transparent };
        categoryGrid = new TableLayoutPanel { ColumnCount = 3, RowCount = 4, Dock = DockStyle.Fill, Padding = new Padding(2) };
        for (int i=0;i<3;i++) categoryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        for (int i=0;i<4;i++) categoryGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        int idx = 0;
        foreach (var cat in categories)
        {
            var btn = new CategoryButton(cat) { Dock = DockStyle.Fill };
            categoryGrid.Controls.Add(btn, idx%3, idx/3);
            idx++;
        }
        gridContainer.Controls.Add(categoryGrid);
        storagePanel.Controls.Add(gridContainer);
        // Sub-category list
        subCategoryPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.FromArgb(0xE6,0xE8,0xE8), BorderStyle = BorderStyle.None };
        storagePanel.Controls.Add(subCategoryPanel);
        outerSplitter.Panel1.Controls.Add(storagePanel);
    
        // Right side: inner splitter
        innerSplitter = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 4 };
    
        var wsContainer = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        var wsLabel = new Label { Text = "Block Space", Font = FontLoader.GetFont(9f, FontStyle.Bold), ForeColor = Color.FromArgb(0x5C,0x5C,0x5C), BackColor = Color.FromArgb(0xCD,0xCD,0xD2), Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleCenter };
        workspacePanel = new WorkspacePanel { Dock = DockStyle.Fill };
        workspacePanel.CodeChanged += UpdatePythonCode;
        wsContainer.Controls.Add(workspacePanel); wsContainer.Controls.Add(wsLabel);
        innerSplitter.Panel1.Controls.Add(wsContainer);
    
        var codeContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0x2D,0x2D,0x2D), BorderStyle = BorderStyle.FixedSingle };
        var codeLabel = new Label { Text = "Python Code", Font = FontLoader.GetFont(9f, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.FromArgb(0x5C,0x5C,0x5C), Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleCenter };
        pythonCodeBox = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0x1E,0x1E,0x1E), ForeColor = Color.FromArgb(0xD4,0xD4,0xD4), Font = new Font("Consolas", 10.5f), ReadOnly = true, BorderStyle = BorderStyle.None, Text = "# Drag blocks here to build your Python program\n", WordWrap = false, ScrollBars = RichTextBoxScrollBars.Both };
        codeContainer.Controls.Add(pythonCodeBox); codeContainer.Controls.Add(codeLabel);
        innerSplitter.Panel2.Controls.Add(codeContainer);
    
        outerSplitter.Panel2.Controls.Add(innerSplitter);
    
        Controls.Add(outerSplitter);
        Controls.Add(tabsPanel);
        Controls.Add(topPanel);
        Controls.Add(bottomPanel);
    
        if (categories.Count > 0)
        {
            selectedCategory = categories[0];
            ((CategoryButton)categoryGrid.Controls[0]).IsSelected = true;
            categoryGrid.Controls[0].Invalidate();
            PopulateSubCategories(selectedCategory);
        }
    
        // Safe sizing after shown
        this.Shown += (s, args) =>
        {
            this.BeginInvoke((Action)(() =>
            {
                outerSplitter.Panel1MinSize = 200;
                outerSplitter.Panel2MinSize = 400;
                outerSplitter.SplitterDistance = 415;
                innerSplitter.Panel1MinSize = 150;
                innerSplitter.Panel2MinSize = 150;
                int desired = innerSplitter.Width - 415;
                innerSplitter.SplitterDistance = Math.Max(innerSplitter.Panel1MinSize,
                    Math.Min(desired, innerSplitter.Width - innerSplitter.Panel2MinSize - innerSplitter.SplitterWidth));
            }));
        };
    }


    public void OnCategorySelected(CategoryInfo cat)
    {
        selectedCategory = cat;
        PopulateSubCategories(cat);
        foreach (Control c in categoryGrid.Controls) if (c is CategoryButton cb) cb.Invalidate();
    }

    private void PopulateSubCategories(CategoryInfo cat)
    {
        subCategoryPanel.Controls.Clear();
        if (cat == null) return;
        string filter = searchBox.Text.Trim();
        foreach (var sub in cat.SubCategories)
        {
            var filtered = sub.Blocks.Where(b => string.IsNullOrEmpty(filter) || b.Label.ToLower().Contains(filter.ToLower())).ToList();
            if (!filtered.Any()) continue;
            var squircleLabel = new SubCategorySquircle(sub.Name);
            int w = TextRenderer.MeasureText(sub.Name, squircleLabel.Font).Width + 20;
            squircleLabel.Width = w > 100 ? w : 100;
            subCategoryPanel.Controls.Add(squircleLabel);
            foreach (var b in filtered)
                subCategoryPanel.Controls.Add(new BlockStorageItem(b));
        }
    }

    private void UpdatePythonCode()
    {
        pythonCodeBox.Text = workspacePanel.GeneratePython();
        statusLabel.Text = $"  Python code updated – {workspacePanel.Blocks.Count} blocks placed.";
    }
}
