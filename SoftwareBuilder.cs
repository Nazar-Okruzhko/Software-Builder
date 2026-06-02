// SoftwareBuilder - Visual Python Programming Environment
// Single-file .NET 6.0 Windows Forms Application
// Build: dotnet new console -n SoftwareBuilder -f net6.0-windows
// Replace Program.cs with this file, then: dotnet run
// Icons: place .png files in a folder named "base\icons" next to the executable.
// Icon1.ico: place in the same folder as the executable to set the window icon.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// ──────────────────────────────────────────────
//  PROGRAM ENTRY POINT
// ──────────────────────────────────────────────
static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new MainForm());
    }
}

// ──────────────────────────────────────────────
//  ENUMS
// ──────────────────────────────────────────────
enum BlockShape { Hat, Stack, CBlock, Reporter, Boolean }
enum BlockCategory { Flow, Variables, Functions, Objects, Data, Text, Math, Files, UI, Time, System, Advanced }

// ──────────────────────────────────────────────
//  CATEGORY INFO
// ──────────────────────────────────────────────
class CategoryInfo
{
    public BlockCategory Category;
    public string Name;
    public Color Color;
    public string IconName; // e.g., "flow"
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

// ──────────────────────────────────────────────
//  BLOCK DEFINITION (Template)
// ──────────────────────────────────────────────
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

// ──────────────────────────────────────────────
//  WORKSPACE BLOCK (Placed in workspace)
// ──────────────────────────────────────────────
class WorkspaceBlock : ICloneable
{
    public BlockDefinition Definition;
    public Rectangle Bounds;
    public List<WorkspaceBlock> InnerBlocks = new();
    public string[] ArgValues;
    public bool IsDragging;
    public bool IsSelected;
    public int IndentLevel;

    public WorkspaceBlock(BlockDefinition def, Point location)
    {
        Definition = def;
        Bounds = new Rectangle(location, GetBlockSize(def));
        ArgValues = (string[])def.DefaultArgs.Clone();
        IndentLevel = 0;
    }

    public static Size GetBlockSize(BlockDefinition def)
    {
        int w = Math.Max(80, TextRenderer.MeasureText(def.Label, new Font("Segoe UI", 9f, FontStyle.Bold)).Width + 40);
        if (def.Shape == BlockShape.Boolean) return new Size(Math.Max(w, 60), 26);
        if (def.Shape == BlockShape.Reporter) return new Size(Math.Max(w, 50), 24);
        if (def.IsCBlock) return new Size(Math.Max(w, 120), 70);
        return new Size(w, 26);
    }

    public object Clone()
    {
        var clone = new WorkspaceBlock(Definition, Bounds.Location);
        clone.ArgValues = (string[])ArgValues.Clone();
        clone.Bounds = Bounds;
        clone.IsSelected = IsSelected;
        clone.IndentLevel = IndentLevel;
        // InnerBlocks are not deep-cloned for simplicity
        return clone;
    }
}

// ──────────────────────────────────────────────
//  UNDO / REDO MANAGER
// ──────────────────────────────────────────────
class UndoRedoManager
{
    private Stack<List<WorkspaceBlock>> undoStack = new();
    private Stack<List<WorkspaceBlock>> redoStack = new();
    private const int MaxSteps = 30;

    public void SaveState(List<WorkspaceBlock> blocks)
    {
        undoStack.Push(blocks.Select(b => (WorkspaceBlock)b.Clone()).ToList());
        redoStack.Clear();
        if (undoStack.Count > MaxSteps)
            undoStack = new Stack<List<WorkspaceBlock>>(undoStack.Take(MaxSteps));
    }

    public List<WorkspaceBlock> Undo(List<WorkspaceBlock> current)
    {
        if (undoStack.Count == 0) return null;
        redoStack.Push(current.Select(b => (WorkspaceBlock)b.Clone()).ToList());
        return undoStack.Pop();
    }

    public List<WorkspaceBlock> Redo(List<WorkspaceBlock> current)
    {
        if (redoStack.Count == 0) return null;
        undoStack.Push(current.Select(b => (WorkspaceBlock)b.Clone()).ToList());
        return redoStack.Pop();
    }
}

// ──────────────────────────────────────────────
//  BLOCK STORAGE ITEM (Palette block)
// ──────────────────────────────────────────────
class BlockStorageItem : Control
{
    public BlockDefinition Definition;
    public bool IsHovered;
    private static readonly Font BlockFont = new("Segoe UI", 8.5f, FontStyle.Bold);

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

        Rectangle r = new(2, 2, Width - 4, Height - 4);
        Color baseColor = Definition.Color;
        if (IsHovered) baseColor = ControlPaint.Light(baseColor, 0.2f);

        using var path = CreateBlockPath(r, Definition.Shape, Definition.IsCBlock);
        using var brush = new LinearGradientBrush(r, ControlPaint.Light(baseColor, 0.3f), ControlPaint.Dark(baseColor, 0.15f), LinearGradientMode.Vertical);
        g.FillPath(brush, path);
        using var pen = new Pen(ControlPaint.Dark(baseColor, 0.35f), 1f);
        g.DrawPath(pen, path);
        using var hlPen = new Pen(ControlPaint.Light(baseColor, 0.5f), 1f);
        var hlPath = CreateBlockPath(new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 4), Definition.Shape, Definition.IsCBlock);
        g.DrawPath(hlPen, hlPath);
        hlPath.Dispose();
        hlPen.Dispose();

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

    // Generate Scratch-style block shapes
    private GraphicsPath CreateBlockPath(Rectangle r, BlockShape shape, bool isCBlock)
    {
        var path = new GraphicsPath();
        int nw = 10, nh = 4, rx = 5;

        if (shape == BlockShape.Hat)
        {
            // Rounded top, notch at bottom
            path.AddArc(r.X, r.Y, rx * 2, rx * 2, 180, 90);
            path.AddArc(r.Right - rx * 2, r.Y, rx * 2, rx * 2, 270, 90);
            path.AddLine(r.Right, r.Y + rx, r.Right, r.Bottom - nh);
            path.AddLine(r.Right, r.Bottom - nh, r.X + r.Width / 2 + nw / 2, r.Bottom - nh);
            path.AddLine(r.X + r.Width / 2 + nw / 2, r.Bottom - nh, r.X + r.Width / 2, r.Bottom);
            path.AddLine(r.X + r.Width / 2, r.Bottom, r.X + r.Width / 2 - nw / 2, r.Bottom - nh);
            path.AddLine(r.X + r.Width / 2 - nw / 2, r.Bottom - nh, r.X, r.Bottom - nh);
            path.AddLine(r.X, r.Bottom - nh, r.X, r.Y + rx);
            path.CloseFigure();
        }
        else if (shape == BlockShape.Boolean)
        {
            int tip = 8, hh = r.Height / 2;
            path.AddPolygon(new Point[] {
                new(r.X + tip, r.Y), new(r.Right - tip, r.Y),
                new(r.Right, r.Y + hh), new(r.Right - tip, r.Bottom),
                new(r.X + tip, r.Bottom), new(r.X, r.Y + hh)
            });
        }
        else if (shape == BlockShape.Reporter)
        {
            int rr = r.Height / 2;
            path.AddArc(r.X, r.Y, rr * 2, rr * 2, 90, 180);
            path.AddArc(r.Right - rr * 2, r.Y, rr * 2, rr * 2, 270, 180);
            path.CloseFigure();
        }
        else if (isCBlock)
        {
            // C-block with notch top, inner cavity, notch bottom
            path.AddLine(r.X + r.Width / 2 - nw / 2, r.Y, r.X + r.Width / 2, r.Y - nh);
            path.AddLine(r.X + r.Width / 2, r.Y - nh, r.X + r.Width / 2 + nw / 2, r.Y);
            path.AddLine(r.Right - rx, r.Y, r.Right, r.Y + rx);
            path.AddLine(r.Right, r.Bottom - rx - nh, r.Right - rx, r.Bottom - nh);
            path.AddLine(r.Right - rx, r.Bottom - nh, r.X + r.Width / 2 + nw / 2, r.Bottom - nh);
            path.AddLine(r.X + r.Width / 2 + nw / 2, r.Bottom - nh, r.X + r.Width / 2, r.Bottom);
            path.AddLine(r.X + r.Width / 2, r.Bottom, r.X + r.Width / 2 - nw / 2, r.Bottom - nh);
            path.AddLine(r.X + r.Width / 2 - nw / 2, r.Bottom - nh, r.X + rx, r.Bottom - nh);
            path.AddLine(r.X, r.Bottom - nh - rx, r.X + rx, r.Bottom - nh - rx * 2);
            path.AddLine(r.X + rx, r.Bottom - nh - rx * 2, r.X + rx, r.Y + 20);
            path.AddLine(r.X + rx, r.Y + 20, r.Right - rx, r.Y + 20);
            path.AddLine(r.Right - rx, r.Y + 20, r.Right - rx, r.Bottom - nh - rx * 2);
            path.AddLine(r.Right - rx, r.Bottom - nh - rx * 2, r.Right, r.Bottom - nh - rx);
            path.CloseFigure();
        }
        else
        {
            // Stack: notch top, notch bottom
            path.AddLine(r.X + r.Width / 2 - nw / 2, r.Y, r.X + r.Width / 2, r.Y - nh);
            path.AddLine(r.X + r.Width / 2, r.Y - nh, r.X + r.Width / 2 + nw / 2, r.Y);
            path.AddLine(r.Right - rx, r.Y, r.Right, r.Y + rx);
            path.AddLine(r.Right, r.Bottom - rx - nh, r.Right - rx, r.Bottom - nh);
            path.AddLine(r.Right - rx, r.Bottom - nh, r.X + r.Width / 2 + nw / 2, r.Bottom - nh);
            path.AddLine(r.X + r.Width / 2 + nw / 2, r.Bottom - nh, r.X + r.Width / 2, r.Bottom);
            path.AddLine(r.X + r.Width / 2, r.Bottom, r.X + r.Width / 2 - nw / 2, r.Bottom - nh);
            path.AddLine(r.X + r.Width / 2 - nw / 2, r.Bottom - nh, r.X + rx, r.Bottom - nh);
            path.AddLine(r.X, r.Bottom - nh - rx, r.X, r.Y + rx);
            path.AddLine(r.X, r.Y + rx, r.X + rx, r.Y);
            path.CloseFigure();
        }
        return path;
    }

    private static bool IsDarkColor(Color c) => (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) < 140;
}

// ──────────────────────────────────────────────
//  SUB-CATEGORY ROUND LABEL
// ──────────────────────────────────────────────
class RoundLabel : Control
{
    public string Title;
    private static readonly Font SubFont = new("Segoe UI", 8.5f, FontStyle.Regular);

    public RoundLabel(string title)
    {
        Title = title;
        Height = 22;
        Width = 120; // will be auto-sized later
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 2, Width - 1, Height - 4);
        using var path = new GraphicsPath();
        int radius = r.Height / 2;
        path.AddArc(r.X, r.Y, radius * 2, radius * 2, 90, 180);
        path.AddArc(r.Right - radius * 2, r.Y, radius * 2, radius * 2, 270, 180);
        path.CloseFigure();
        using var brush = new SolidBrush(Color.FromArgb(0x5C, 0x5C, 0x5C));
        g.FillPath(brush, path);
        using var textBrush = new SolidBrush(Color.White);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(Title, SubFont, textBrush, new Rectangle(r.X, r.Y, r.Width, r.Height), fmt);
    }
}

// ──────────────────────────────────────────────
//  CATEGORY BUTTON (3x4 grid)
// ──────────────────────────────────────────────
class CategoryButton : Control
{
    public CategoryInfo Info;
    public bool IsSelected;
    private Image icon;
    private static readonly Font ButtonFont = new("Segoe UI", 7.5f, FontStyle.Bold);

    public CategoryButton(CategoryInfo info)
    {
        Info = info;
        Height = 52;
        Width = 100;
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
            if (File.Exists(file))
                icon = Image.FromFile(file);
        }
        catch { /* fallback */ }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle;
        r.Inflate(-2, -2);
        Color back = IsSelected ? ControlPaint.Dark(Info.Color, 0.3f) : Info.Color;
        using var brush = new LinearGradientBrush(r, ControlPaint.Light(back, 0.2f), ControlPaint.Dark(back, 0.1f), LinearGradientMode.Vertical);
        using var pen = new Pen(ControlPaint.Dark(back, 0.4f), 1f);
        g.FillRectangle(brush, r);
        g.DrawRectangle(pen, r);

        int iconSize = 20;
        if (icon != null)
            g.DrawImage(icon, new Rectangle(r.X + (r.Width - iconSize) / 2, r.Y + 4, iconSize, iconSize));
        else
        {
            // Fallback: draw two-letter abbreviation
            using var textBrush = new SolidBrush(Color.White);
            var fallbackFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            string abbrev = Info.Name.Length >= 2 ? Info.Name.Substring(0, 2).ToUpper() : Info.Name;
            g.DrawString(abbrev, fallbackFont, textBrush, new Point(r.X + (r.Width - 16) / 2, r.Y + 4));
            fallbackFont.Dispose();
        }
        using var textBrush2 = new SolidBrush(Color.White);
        g.DrawString(Info.Name, ButtonFont, textBrush2, new Rectangle(r.X, r.Y + 26, r.Width, 20), new StringFormat { Alignment = StringAlignment.Center });
    }

    protected override void OnClick(EventArgs e)
    {
        var parent = Parent as TableLayoutPanel;
        if (parent != null)
        {
            foreach (Control c in parent.Controls)
                if (c is CategoryButton cb) cb.IsSelected = false;
            IsSelected = true;
            // Trigger main form to update sub-categories
            ((MainForm)FindForm())?.OnCategorySelected(Info);
        }
        Invalidate();
        base.OnClick(e);
    }
}

// ──────────────────────────────────────────────
//  WORKSPACE PANEL (Center)
// ──────────────────────────────────────────────
class WorkspacePanel : Panel
{
    public List<WorkspaceBlock> Blocks = new();
    public UndoRedoManager UndoRedo = new();
    public WorkspaceBlock ClipboardBlock;
    private Point dragOffset;
    private WorkspaceBlock draggingBlock;
    private int gridSize = 8;
    private bool selecting;
    private Point selectStart, selectEnd;
    private List<WorkspaceBlock> selectedBlocks = new();
    private ContextMenuStrip contextMenu;

    public WorkspacePanel()
    {
        DoubleBuffered = true;
        AllowDrop = true;
        BackColor = Color.FromArgb(0xDD, 0xEE, 0xDE);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        InitializeContextMenu();
    }

    private void InitializeContextMenu()
    {
        contextMenu = new ContextMenuStrip();
        var undoItem = new ToolStripMenuItem("Undo", null, (s, e) => Undo());
        var redoItem = new ToolStripMenuItem("Redo", null, (s, e) => Redo());
        var copyItem = new ToolStripMenuItem("Copy", null, (s, e) => CopySelected());
        var pasteItem = new ToolStripMenuItem("Paste", null, (s, e) => PasteFromClipboard());
        var pasteBlockItem = new ToolStripMenuItem("Paste a Block", null, (s, e) => PasteBlock());
        var arrangeItem = new ToolStripMenuItem("Arrange Blocks", null, (s, e) => ArrangeBlocks());
        contextMenu.Items.AddRange(new ToolStripItem[] { undoItem, redoItem, new ToolStripSeparator(), copyItem, pasteItem, pasteBlockItem, new ToolStripSeparator(), arrangeItem });
    }

    public void SaveUndoState()
    {
        UndoRedo.SaveState(Blocks);
    }

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
        if (selectedBlocks.Count > 0)
            ClipboardBlock = (WorkspaceBlock)selectedBlocks[0].Clone();
    }

    private void PasteFromClipboard()
    {
        if (ClipboardBlock == null) return;
        SaveUndoState();
        var newBlock = (WorkspaceBlock)ClipboardBlock.Clone();
        newBlock.Bounds = new Rectangle(new Point(ClipboardBlock.Bounds.X + 20, ClipboardBlock.Bounds.Y + 20), newBlock.Bounds.Size);
        Blocks.Add(newBlock);
        Invalidate();
        TriggerCodeUpdate();
    }

    private void PasteBlock()
    {
        // Paste generic block? For simplicity, same as Paste but with user selection later.
        PasteFromClipboard();
    }

    public void ArrangeBlocks()
    {
        SaveUndoState();
        int y = 10;
        int x = 10;
        foreach (var b in Blocks.OrderBy(b => b.Bounds.Y))
        {
            b.Bounds = new Rectangle(new Point(x, y), b.Bounds.Size);
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

        using var gridPen = new Pen(Color.FromArgb(200, 210, 200), 0.5f);
        for (int x = 0; x < Width; x += gridSize * 3)
            g.DrawLine(gridPen, x, 0, x, Height);
        for (int y = 0; y < Height; y += gridSize * 3)
            g.DrawLine(gridPen, 0, y, Width, y);

        foreach (var block in Blocks)
        {
            if (block == draggingBlock) continue;
            DrawWorkspaceBlock(g, block);
        }
        if (draggingBlock != null)
            DrawWorkspaceBlock(g, draggingBlock);

        // Selection rectangle
        if (selecting)
        {
            var rect = GetSelectionRectangle();
            using var selPen = new Pen(Color.DodgerBlue, 2f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(selPen, rect);
        }
    }

    private void DrawWorkspaceBlock(Graphics g, WorkspaceBlock block)
    {
        var r = block.Bounds;
        Color c = block.Definition.Color;
        if (block.IsDragging) c = Color.FromArgb(200, c);
        if (block.IsSelected) c = ControlPaint.Light(c, 0.4f);

        using var path = CreateWorkspaceBlockPath(r, block.Definition.Shape, block.Definition.IsCBlock);
        using var fill = new LinearGradientBrush(r, ControlPaint.Light(c, 0.35f), ControlPaint.Dark(c, 0.12f), LinearGradientMode.Vertical);
        g.FillPath(fill, path);
        using var outline = new Pen(ControlPaint.Dark(c, 0.4f), block.IsSelected ? 2.5f : 1.2f);
        g.DrawPath(outline, path);
        using var hl = new Pen(ControlPaint.Light(c, 0.55f), 0.8f);
        var innerR = new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
        using var innerPath = CreateWorkspaceBlockPath(innerR, block.Definition.Shape, block.Definition.IsCBlock);
        g.DrawPath(hl, innerPath);

        using var textBrush = new SolidBrush(IsDarkColor(c) ? Color.White : Color.Black);
        var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        var textRect = block.Definition.IsCBlock
            ? new Rectangle(r.X + 6, r.Y + 3, r.Width - 12, 18)
            : new Rectangle(r.X + 6, r.Y + 2, r.Width - 12, r.Height - 4);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(block.Definition.Label, font, textBrush, textRect, fmt);
        font.Dispose();
    }

    private GraphicsPath CreateWorkspaceBlockPath(Rectangle r, BlockShape shape, bool isCBlock)
    {
        // Same logic as BlockStorageItem.CreateBlockPath but adapted for workspace scale
        var path = new GraphicsPath();
        int nw = 10, nh = 4, rx = 5;

        if (shape == BlockShape.Hat)
        {
            path.AddArc(r.X, r.Y, rx * 2, rx * 2, 180, 90);
            path.AddArc(r.Right - rx * 2, r.Y, rx * 2, rx * 2, 270, 90);
            path.AddLine(r.Right, r.Y + rx, r.Right, r.Bottom - nh);
            path.AddLine(r.Right, r.Bottom - nh, r.X + r.Width / 2 + nw / 2, r.Bottom - nh);
            path.AddLine(r.X + r.Width / 2 + nw / 2, r.Bottom - nh, r.X + r.Width / 2, r.Bottom);
            path.AddLine(r.X + r.Width / 2, r.Bottom, r.X + r.Width / 2 - nw / 2, r.Bottom - nh);
            path.AddLine(r.X + r.Width / 2 - nw / 2, r.Bottom - nh, r.X, r.Bottom - nh);
            path.AddLine(r.X, r.Bottom - nh, r.X, r.Y + rx);
            path.CloseFigure();
        }
        else if (shape == BlockShape.Boolean)
        {
            int tip = 8, hh = r.Height / 2;
            path.AddPolygon(new Point[] {
                new(r.X + tip, r.Y), new(r.Right - tip, r.Y),
                new(r.Right, r.Y + hh), new(r.Right - tip, r.Bottom),
                new(r.X + tip, r.Bottom), new(r.X, r.Y + hh)
            });
        }
        else if (shape == BlockShape.Reporter)
        {
            int rr = r.Height / 2;
            path.AddArc(r.X, r.Y, rr * 2, rr * 2, 90, 180);
            path.AddArc(r.Right - rr * 2, r.Y, rr * 2, rr * 2, 270, 180);
            path.CloseFigure();
        }
        else if (isCBlock)
        {
            path.AddLine(r.X + r.Width / 2 - nw / 2, r.Y, r.X + r.Width / 2, r.Y - nh);
            path.AddLine(r.X + r.Width / 2, r.Y - nh, r.X + r.Width / 2 + nw / 2, r.Y);
            path.AddLine(r.Right - rx, r.Y, r.Right, r.Y + rx);
            path.AddLine(r.Right, r.Bottom - rx - nh, r.Right - rx, r.Bottom - nh);
            path.AddLine(r.Right - rx, r.Bottom - nh, r.X + r.Width / 2 + nw / 2, r.Bottom - nh);
            path.AddLine(r.X + r.Width / 2 + nw / 2, r.Bottom - nh, r.X + r.Width / 2, r.Bottom);
            path.AddLine(r.X + r.Width / 2, r.Bottom, r.X + r.Width / 2 - nw / 2, r.Bottom - nh);
            path.AddLine(r.X + r.Width / 2 - nw / 2, r.Bottom - nh, r.X + rx, r.Bottom - nh);
            path.AddLine(r.X, r.Bottom - nh - rx, r.X + rx, r.Bottom - nh - rx * 2);
            path.AddLine(r.X + rx, r.Bottom - nh - rx * 2, r.X + rx, r.Y + 20);
            path.AddLine(r.X + rx, r.Y + 20, r.Right - rx, r.Y + 20);
            path.AddLine(r.Right - rx, r.Y + 20, r.Right - rx, r.Bottom - nh - rx * 2);
            path.AddLine(r.Right - rx, r.Bottom - nh - rx * 2, r.Right, r.Bottom - nh - rx);
            path.CloseFigure();
        }
        else
        {
            path.AddLine(r.X + r.Width / 2 - nw / 2, r.Y, r.X + r.Width / 2, r.Y - nh);
            path.AddLine(r.X + r.Width / 2, r.Y - nh, r.X + r.Width / 2 + nw / 2, r.Y);
            path.AddLine(r.Right - rx, r.Y, r.Right, r.Y + rx);
            path.AddLine(r.Right, r.Bottom - rx - nh, r.Right - rx, r.Bottom - nh);
            path.AddLine(r.Right - rx, r.Bottom - nh, r.X + r.Width / 2 + nw / 2, r.Bottom - nh);
            path.AddLine(r.X + r.Width / 2 + nw / 2, r.Bottom - nh, r.X + r.Width / 2, r.Bottom);
            path.AddLine(r.X + r.Width / 2, r.Bottom, r.X + r.Width / 2 - nw / 2, r.Bottom - nh);
            path.AddLine(r.X + r.Width / 2 - nw / 2, r.Bottom - nh, r.X + rx, r.Bottom - nh);
            path.AddLine(r.X, r.Bottom - nh - rx, r.X, r.Y + rx);
            path.AddLine(r.X, r.Y + rx, r.X + rx, r.Y);
            path.CloseFigure();
        }
        return path;
    }

    private Rectangle GetSelectionRectangle()
    {
        return new Rectangle(
            Math.Min(selectStart.X, selectEnd.X),
            Math.Min(selectStart.Y, selectEnd.Y),
            Math.Abs(selectStart.X - selectEnd.X),
            Math.Abs(selectStart.Y - selectEnd.Y));
    }

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
                new Point(pt.X - draggingBlock.Bounds.Width / 2, pt.Y - draggingBlock.Bounds.Height / 2),
                draggingBlock.Bounds.Size);
            Invalidate();
        }
        base.OnDragOver(e);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        if (draggingBlock != null)
        {
            draggingBlock.IsDragging = false;
            var b = draggingBlock.Bounds;
            b.X = (b.X / (gridSize * 3)) * (gridSize * 3);
            b.Y = (b.Y / (gridSize * 3)) * (gridSize * 3);
            draggingBlock.Bounds = b;
            var final = draggingBlock;
            draggingBlock = null;
            Blocks[Blocks.Count - 1] = final;
            Invalidate();
            TriggerCodeUpdate();
        }
        base.OnDragDrop(e);
    }

    protected override void OnDragLeave(EventArgs e)
    {
        if (draggingBlock != null)
        {
            Blocks.Remove(draggingBlock);
            draggingBlock = null;
            Invalidate();
        }
        base.OnDragLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            // Check if clicking on block for dragging
            for (int i = Blocks.Count - 1; i >= 0; i--)
            {
                if (Blocks[i].Bounds.Contains(e.Location))
                {
                    // Toggle selection if Ctrl held
                    if (ModifierKeys.HasFlag(Keys.Control))
                    {
                        Blocks[i].IsSelected = !Blocks[i].IsSelected;
                        Invalidate();
                        return;
                    }
                    draggingBlock = Blocks[i];
                    dragOffset = new Point(e.X - Blocks[i].Bounds.X, e.Y - Blocks[i].Bounds.Y);
                    draggingBlock.IsDragging = true;
                    if (!Blocks[i].IsSelected)
                    {
                        // Clear selection and select this one
                        DeselectAll();
                        Blocks[i].IsSelected = true;
                    }
                    Invalidate();
                    return;
                }
            }
            // Start selection rectangle
            selecting = true;
            selectStart = e.Location;
            selectEnd = e.Location;
            DeselectAll();
            Invalidate();
        }
        else if (e.Button == MouseButtons.Right)
        {
            // Right-click context menu
            contextMenu.Show(this, e.Location);
        }
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
            var b = draggingBlock.Bounds;
            b.X = (b.X / (gridSize * 3)) * (gridSize * 3);
            b.Y = (b.Y / (gridSize * 3)) * (gridSize * 3);
            draggingBlock.Bounds = b;
            SaveUndoState();
            draggingBlock = null;
            Invalidate();
            TriggerCodeUpdate();
        }
        if (selecting)
        {
            selecting = false;
            // Select blocks within selection rectangle
            var rect = GetSelectionRectangle();
            if (rect.Width > 5 && rect.Height > 5)
            {
                foreach (var b in Blocks)
                    b.IsSelected = rect.IntersectsWith(b.Bounds);
                Invalidate();
            }
        }
        base.OnMouseUp(e);
    }

    private void DeselectAll()
    {
        foreach (var b in Blocks) b.IsSelected = false;
    }

    public event Action CodeChanged;
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
            if (lastY >= 0 && block.Bounds.Y > lastY + 30)
                sb.AppendLine();
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

// ──────────────────────────────────────────────
//  MAIN FORM
// ──────────────────────────────────────────────
class MainForm : Form
{
    private Panel topPanel, bottomPanel;
    private TextBox searchBox;
    private TableLayoutPanel categoryGrid;
    private FlowLayoutPanel subCategoryPanel;
    private WorkspacePanel workspacePanel;
    private RichTextBox pythonCodeBox;
    private Label statusLabel;
    private Button runButton, clearButton, copyButton;
    private List<CategoryInfo> categories;
    private CategoryInfo selectedCategory;
    private TableLayoutPanel mainLayout;

    public MainForm()
    {
        Text = "SoftwareBuilder – Visual Python Programming";
        Size = new Size(1400, 820);
        MinimumSize = new Size(1000, 650);
        BackColor = Color.FromArgb(0xDD, 0xEE, 0xDE);
        SetAppIcon();
        InitializeCategories();
        BuildUI();
        CenterToScreen();
    }

    private void SetAppIcon()
    {
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Icon1.ico");
            if (File.Exists(iconPath))
            {
                Icon = new Icon(iconPath);
            }
            else
            {
                // Fallback generated icon
                using var bmp = new Bitmap(32, 32);
                using var g = Graphics.FromImage(bmp);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(0x4A, 0x6C, 0xD4));
                using var brush = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(0x5B, 0x8D, 0xF5), Color.FromArgb(0x3A, 0x5C, 0xC4), LinearGradientMode.Vertical);
                g.FillRectangle(brush, 0, 0, 32, 32);
                using var font = new Font("Consolas", 14f, FontStyle.Bold);
                g.DrawString("SB", font, Brushes.White, new PointF(2, 4));
                var iconHandle = bmp.GetHicon();
                Icon = Icon.FromHandle(iconHandle);
            }
        }
        catch { /* use default */ }
    }

    private void InitializeCategories()
    {
        categories = new List<CategoryInfo>
        {
            new(BlockCategory.Flow, "FLOW", Color.FromArgb(0xE1, 0xA9, 0x1A), "flow") {
                SubCategories = {
                    new("Execution") { Blocks = {
                        new("pass", BlockShape.Stack, BlockCategory.Flow, "Execution", Color.FromArgb(0xE1, 0xA9, 0x1A), "pass"),
                        new("return", BlockShape.Stack, BlockCategory.Flow, "Execution", Color.FromArgb(0xE1, 0xA9, 0x1A), "return {0}", false, new[]{"None"}),
                        new("yield", BlockShape.Stack, BlockCategory.Flow, "Execution", Color.FromArgb(0xE1, 0xA9, 0x1A), "yield {0}", false, new[]{"value"})
                    }},
                    new("Conditions") { Blocks = {
                        new("if", BlockShape.Stack, BlockCategory.Flow, "Conditions", Color.FromArgb(0xE1, 0xA9, 0x1A), "if {0}:", true, new[]{"True"}),
                        new("elif", BlockShape.Stack, BlockCategory.Flow, "Conditions", Color.FromArgb(0xE1, 0xA9, 0x1A), "elif {0}:", true, new[]{"True"}),
                        new("else", BlockShape.Stack, BlockCategory.Flow, "Conditions", Color.FromArgb(0xE1, 0xA9, 0x1A), "else:", true),
                        new("match", BlockShape.Stack, BlockCategory.Flow, "Conditions", Color.FromArgb(0xE1, 0xA9, 0x1A), "match {0}:", true, new[]{"value"})
                    }},
                    new("Loops") { Blocks = {
                        new("for", BlockShape.Stack, BlockCategory.Flow, "Loops", Color.FromArgb(0xE1, 0xA9, 0x1A), "for {0} in {1}:", true, new[]{"i", "range(10)"}),
                        new("while", BlockShape.Stack, BlockCategory.Flow, "Loops", Color.FromArgb(0xE1, 0xA9, 0x1A), "while {0}:", true, new[]{"True"}),
                        new("break", BlockShape.Stack, BlockCategory.Flow, "Loops", Color.FromArgb(0xE1, 0xA9, 0x1A), "break"),
                        new("continue", BlockShape.Stack, BlockCategory.Flow, "Loops", Color.FromArgb(0xE1, 0xA9, 0x1A), "continue")
                    }},
                    new("Iteration Helpers") { Blocks = {
                        new("range()", BlockShape.Reporter, BlockCategory.Flow, "Iteration Helpers", Color.FromArgb(0xE1, 0xA9, 0x1A), "range({0})", false, new[]{"10"}),
                        new("enumerate()", BlockShape.Reporter, BlockCategory.Flow, "Iteration Helpers", Color.FromArgb(0xE1, 0xA9, 0x1A), "enumerate({0})", false, new[]{"list"}),
                        new("zip()", BlockShape.Reporter, BlockCategory.Flow, "Iteration Helpers", Color.FromArgb(0xE1, 0xA9, 0x1A), "zip({0}, {1})", false, new[]{"a", "b"}),
                        new("reversed()", BlockShape.Reporter, BlockCategory.Flow, "Iteration Helpers", Color.FromArgb(0xE1, 0xA9, 0x1A), "reversed({0})", false, new[]{"seq"})
                    }},
                    new("Exceptions") { Blocks = {
                        new("try", BlockShape.Stack, BlockCategory.Flow, "Exceptions", Color.FromArgb(0xE1, 0xA9, 0x1A), "try:", true),
                        new("except", BlockShape.Stack, BlockCategory.Flow, "Exceptions", Color.FromArgb(0xE1, 0xA9, 0x1A), "except {0}:", true, new[]{"Exception"}),
                        new("finally", BlockShape.Stack, BlockCategory.Flow, "Exceptions", Color.FromArgb(0xE1, 0xA9, 0x1A), "finally:", true),
                        new("raise", BlockShape.Stack, BlockCategory.Flow, "Exceptions", Color.FromArgb(0xE1, 0xA9, 0x1A), "raise {0}", false, new[]{"Exception()"})
                    }}
                }
            },
            new(BlockCategory.Variables, "VARIABLES", Color.FromArgb(0x4A, 0x6C, 0xD4), "variables") {
                SubCategories = {
                    new("Assignment") { Blocks = {
                        new("=", BlockShape.Stack, BlockCategory.Variables, "Assignment", Color.FromArgb(0x4A, 0x6C, 0xD4), "{0} = {1}", false, new[]{"x", "0"}),
                        new("+=", BlockShape.Stack, BlockCategory.Variables, "Assignment", Color.FromArgb(0x4A, 0x6C, 0xD4), "{0} += {1}", false, new[]{"x", "1"}),
                        new("-=", BlockShape.Stack, BlockCategory.Variables, "Assignment", Color.FromArgb(0x4A, 0x6C, 0xD4), "{0} -= {1}", false, new[]{"x", "1"}),
                        new("*=", BlockShape.Stack, BlockCategory.Variables, "Assignment", Color.FromArgb(0x4A, 0x6C, 0xD4), "{0} *= {1}", false, new[]{"x", "2"}),
                        new("/=", BlockShape.Stack, BlockCategory.Variables, "Assignment", Color.FromArgb(0x4A, 0x6C, 0xD4), "{0} /= {1}", false, new[]{"x", "2"})
                    }},
                    new("Types") { Blocks = {
                        new("int", BlockShape.Reporter, BlockCategory.Variables, "Types", Color.FromArgb(0x4A, 0x6C, 0xD4), "int({0})", false, new[]{"0"}),
                        new("float", BlockShape.Reporter, BlockCategory.Variables, "Types", Color.FromArgb(0x4A, 0x6C, 0xD4), "float({0})", false, new[]{"0.0"}),
                        new("str", BlockShape.Reporter, BlockCategory.Variables, "Types", Color.FromArgb(0x4A, 0x6C, 0xD4), "str({0})", false, new[]{"\"\""}),
                        new("bool", BlockShape.Reporter, BlockCategory.Variables, "Types", Color.FromArgb(0x4A, 0x6C, 0xD4), "bool({0})", false, new[]{"True"}),
                        new("list", BlockShape.Reporter, BlockCategory.Variables, "Types", Color.FromArgb(0x4A, 0x6C, 0xD4), "list({0})", false, new[]{"[]"}),
                        new("dict", BlockShape.Reporter, BlockCategory.Variables, "Types", Color.FromArgb(0x4A, 0x6C, 0xD4), "dict({0})", false, new[]{"{}"})
                    }},
                    new("Constants") { Blocks = {
                        new("True", BlockShape.Boolean, BlockCategory.Variables, "Constants", Color.FromArgb(0x4A, 0x6C, 0xD4), "True"),
                        new("False", BlockShape.Boolean, BlockCategory.Variables, "Constants", Color.FromArgb(0x4A, 0x6C, 0xD4), "False"),
                        new("None", BlockShape.Reporter, BlockCategory.Variables, "Constants", Color.FromArgb(0x4A, 0x6C, 0xD4), "None")
                    }},
                    new("Conversion") { Blocks = {
                        new("int()", BlockShape.Reporter, BlockCategory.Variables, "Conversion", Color.FromArgb(0x4A, 0x6C, 0xD4), "int({0})", false, new[]{"0"}),
                        new("float()", BlockShape.Reporter, BlockCategory.Variables, "Conversion", Color.FromArgb(0x4A, 0x6C, 0xD4), "float({0})", false, new[]{"0"}),
                        new("str()", BlockShape.Reporter, BlockCategory.Variables, "Conversion", Color.FromArgb(0x4A, 0x6C, 0xD4), "str({0})", false, new[]{"0"}),
                        new("bool()", BlockShape.Reporter, BlockCategory.Variables, "Conversion", Color.FromArgb(0x4A, 0x6C, 0xD4), "bool({0})", false, new[]{"0"})
                    }}
                }
            },
            new(BlockCategory.Functions, "FUNCTIONS", Color.FromArgb(0x8A, 0x55, 0xD7), "functions") {
                SubCategories = {
                    new("Definition") { Blocks = {
                        new("def", BlockShape.Hat, BlockCategory.Functions, "Definition", Color.FromArgb(0x8A, 0x55, 0xD7), "def {0}({1}):", true, new[]{"my_func", ""}),
                        new("lambda", BlockShape.Reporter, BlockCategory.Functions, "Definition", Color.FromArgb(0x8A, 0x55, 0xD7), "lambda {0}: {1}", false, new[]{"x", "x"})
                    }},
                    new("Return") { Blocks = {
                        new("return", BlockShape.Stack, BlockCategory.Functions, "Return", Color.FromArgb(0x8A, 0x55, 0xD7), "return {0}", false, new[]{"None"})
                    }},
                    new("Parameters") { Blocks = {
                        new("*args", BlockShape.Reporter, BlockCategory.Functions, "Parameters", Color.FromArgb(0x8A, 0x55, 0xD7), "*args"),
                        new("**kwargs", BlockShape.Reporter, BlockCategory.Functions, "Parameters", Color.FromArgb(0x8A, 0x55, 0xD7), "**kwargs"),
                        new("default", BlockShape.Stack, BlockCategory.Functions, "Parameters", Color.FromArgb(0x8A, 0x55, 0xD7), "{0} = {1}", false, new[]{"param", "value"})
                    }},
                    new("Scope") { Blocks = {
                        new("global", BlockShape.Stack, BlockCategory.Functions, "Scope", Color.FromArgb(0x8A, 0x55, 0xD7), "global {0}", false, new[]{"x"}),
                        new("nonlocal", BlockShape.Stack, BlockCategory.Functions, "Scope", Color.FromArgb(0x8A, 0x55, 0xD7), "nonlocal {0}", false, new[]{"x"})
                    }},
                    new("Decorators") { Blocks = {
                        new("@property", BlockShape.Stack, BlockCategory.Functions, "Decorators", Color.FromArgb(0x8A, 0x55, 0xD7), "@property"),
                        new("@staticmethod", BlockShape.Stack, BlockCategory.Functions, "Decorators", Color.FromArgb(0x8A, 0x55, 0xD7), "@staticmethod"),
                        new("@classmethod", BlockShape.Stack, BlockCategory.Functions, "Decorators", Color.FromArgb(0x8A, 0x55, 0xD7), "@classmethod")
                    }}
                }
            },
            new(BlockCategory.Objects, "OBJECTS", Color.FromArgb(0x63, 0x2D, 0x99), "objects") {
                SubCategories = {
                    new("Classes") { Blocks = {
                        new("class", BlockShape.Hat, BlockCategory.Objects, "Classes", Color.FromArgb(0x63, 0x2D, 0x99), "class {0}:", true, new[]{"MyClass"}),
                        new("self", BlockShape.Reporter, BlockCategory.Objects, "Classes", Color.FromArgb(0x63, 0x2D, 0x99), "self"),
                        new("__init__", BlockShape.Stack, BlockCategory.Objects, "Classes", Color.FromArgb(0x63, 0x2D, 0x99), "def __init__(self{0}):", true, new[]{""})
                    }},
                    new("Attributes") { Blocks = {
                        new("getattr", BlockShape.Reporter, BlockCategory.Objects, "Attributes", Color.FromArgb(0x63, 0x2D, 0x99), "getattr({0}, {1})", false, new[]{"obj", "'attr'"}),
                        new("setattr", BlockShape.Stack, BlockCategory.Objects, "Attributes", Color.FromArgb(0x63, 0x2D, 0x99), "setattr({0}, {1}, {2})", false, new[]{"obj", "'attr'", "val"})
                    }},
                    new("Methods") { Blocks = {
                        new("instance method", BlockShape.Stack, BlockCategory.Objects, "Methods", Color.FromArgb(0x63, 0x2D, 0x99), "def {0}(self):", true, new[]{"method"}),
                        new("class method", BlockShape.Stack, BlockCategory.Objects, "Methods", Color.FromArgb(0x63, 0x2D, 0x99), "@classmethod\ndef {0}(cls):", true, new[]{"method"}),
                        new("static method", BlockShape.Stack, BlockCategory.Objects, "Methods", Color.FromArgb(0x63, 0x2D, 0x99), "@staticmethod\ndef {0}():", true, new[]{"method"})
                    }},
                    new("Inheritance") { Blocks = {
                        new("super()", BlockShape.Reporter, BlockCategory.Objects, "Inheritance", Color.FromArgb(0x63, 0x2D, 0x99), "super()"),
                        new("override", BlockShape.Stack, BlockCategory.Objects, "Inheritance", Color.FromArgb(0x63, 0x2D, 0x99), "def {0}(self):\n    super().{0}()", true, new[]{"method"})
                    }}
                }
            },
            new(BlockCategory.Data, "DATA", Color.FromArgb(0x5C, 0xB7, 0x12), "data") {
                SubCategories = {
                    new("Lists") { Blocks = {
                        new("append()", BlockShape.Stack, BlockCategory.Data, "Lists", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.append({1})", false, new[]{"lst", "item"}),
                        new("extend()", BlockShape.Stack, BlockCategory.Data, "Lists", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.extend({1})", false, new[]{"lst", "[]"}),
                        new("insert()", BlockShape.Stack, BlockCategory.Data, "Lists", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.insert({1}, {2})", false, new[]{"lst", "0", "item"}),
                        new("remove()", BlockShape.Stack, BlockCategory.Data, "Lists", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.remove({1})", false, new[]{"lst", "item"}),
                        new("pop()", BlockShape.Reporter, BlockCategory.Data, "Lists", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.pop({1})", false, new[]{"lst", "-1"}),
                        new("sort()", BlockShape.Stack, BlockCategory.Data, "Lists", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.sort()", false, new[]{"lst"}),
                        new("reverse()", BlockShape.Stack, BlockCategory.Data, "Lists", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.reverse()", false, new[]{"lst"})
                    }},
                    new("Dictionaries") { Blocks = {
                        new("keys()", BlockShape.Reporter, BlockCategory.Data, "Dictionaries", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.keys()", false, new[]{"d"}),
                        new("values()", BlockShape.Reporter, BlockCategory.Data, "Dictionaries", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.values()", false, new[]{"d"}),
                        new("items()", BlockShape.Reporter, BlockCategory.Data, "Dictionaries", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.items()", false, new[]{"d"}),
                        new("get()", BlockShape.Reporter, BlockCategory.Data, "Dictionaries", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.get({1})", false, new[]{"d", "'key'"}),
                        new("update()", BlockShape.Stack, BlockCategory.Data, "Dictionaries", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.update({1})", false, new[]{"d", "{}"}),
                        new("pop()", BlockShape.Reporter, BlockCategory.Data, "Dictionaries", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.pop({1})", false, new[]{"d", "'key'"})
                    }},
                    new("Sets") { Blocks = {
                        new("add()", BlockShape.Stack, BlockCategory.Data, "Sets", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.add({1})", false, new[]{"s", "item"}),
                        new("remove()", BlockShape.Stack, BlockCategory.Data, "Sets", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.remove({1})", false, new[]{"s", "item"}),
                        new("union()", BlockShape.Reporter, BlockCategory.Data, "Sets", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.union({1})", false, new[]{"s1", "s2"}),
                        new("intersection()", BlockShape.Reporter, BlockCategory.Data, "Sets", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}.intersection({1})", false, new[]{"s1", "s2"})
                    }},
                    new("Tuples") { Blocks = {
                        new("indexing", BlockShape.Reporter, BlockCategory.Data, "Tuples", Color.FromArgb(0x5C, 0xB7, 0x12), "{0}[{1}]", false, new[]{"tup", "0"}),
                        new("unpacking", BlockShape.Stack, BlockCategory.Data, "Tuples", Color.FromArgb(0x5C, 0xB7, 0x12), "{0} = {1}", false, new[]{"a, b", "tup"})
                    }}
                }
            },
            new(BlockCategory.Text, "TEXT", Color.FromArgb(0xEE, 0x7D, 0x16), "text") {
                SubCategories = {
                    new("Creation") { Blocks = {
                        new("str()", BlockShape.Reporter, BlockCategory.Text, "Creation", Color.FromArgb(0xEE, 0x7D, 0x16), "str({0})", false, new[]{"0"}),
                        new("f-string", BlockShape.Reporter, BlockCategory.Text, "Creation", Color.FromArgb(0xEE, 0x7D, 0x16), "f\"{0}\"", false, new[]{"{value}"})
                    }},
                    new("Manipulation") { Blocks = {
                        new("upper()", BlockShape.Reporter, BlockCategory.Text, "Manipulation", Color.FromArgb(0xEE, 0x7D, 0x16), "{0}.upper()", false, new[]{"s"}),
                        new("lower()", BlockShape.Reporter, BlockCategory.Text, "Manipulation", Color.FromArgb(0xEE, 0x7D, 0x16), "{0}.lower()", false, new[]{"s"}),
                        new("strip()", BlockShape.Reporter, BlockCategory.Text, "Manipulation", Color.FromArgb(0xEE, 0x7D, 0x16), "{0}.strip()", false, new[]{"s"}),
                        new("replace()", BlockShape.Reporter, BlockCategory.Text, "Manipulation", Color.FromArgb(0xEE, 0x7D, 0x16), "{0}.replace({1}, {2})", false, new[]{"s", "'old'", "'new'"}),
                        new("split()", BlockShape.Reporter, BlockCategory.Text, "Manipulation", Color.FromArgb(0xEE, 0x7D, 0x16), "{0}.split({1})", false, new[]{"s", "','"}),
                        new("join()", BlockShape.Reporter, BlockCategory.Text, "Manipulation", Color.FromArgb(0xEE, 0x7D, 0x16), "{0}.join({1})", false, new[]{"','", "lst"})
                    }},
                    new("Search") { Blocks = {
                        new("find()", BlockShape.Reporter, BlockCategory.Text, "Search", Color.FromArgb(0xEE, 0x7D, 0x16), "{0}.find({1})", false, new[]{"s", "'sub'"}),
                        new("index()", BlockShape.Reporter, BlockCategory.Text, "Search", Color.FromArgb(0xEE, 0x7D, 0x16), "{0}.index({1})", false, new[]{"s", "'sub'"}),
                        new("startswith()", BlockShape.Boolean, BlockCategory.Text, "Search", Color.FromArgb(0xEE, 0x7D, 0x16), "{0}.startswith({1})", false, new[]{"s", "'pre'"}),
                        new("endswith()", BlockShape.Boolean, BlockCategory.Text, "Search", Color.FromArgb(0xEE, 0x7D, 0x16), "{0}.endswith({1})", false, new[]{"s", "'suf'"}),
                        new("in", BlockShape.Boolean, BlockCategory.Text, "Search", Color.FromArgb(0xEE, 0x7D, 0x16), "{0} in {1}", false, new[]{"'sub'", "s"})
                    }},
                    new("Formatting") { Blocks = {
                        new("format()", BlockShape.Reporter, BlockCategory.Text, "Formatting", Color.FromArgb(0xEE, 0x7D, 0x16), "{0}.format({1})", false, new[]{"'{}'", "val"}),
                        new("f-string", BlockShape.Reporter, BlockCategory.Text, "Formatting", Color.FromArgb(0xEE, 0x7D, 0x16), "f'{0}'", false, new[]{"{var}"})
                    }}
                }
            },
            new(BlockCategory.Math, "MATH", Color.FromArgb(0x2C, 0xA5, 0xE2), "math") {
                SubCategories = {
                    new("Arithmetic") { Blocks = {
                        new("+", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C, 0xA5, 0xE2), "({0} + {1})", false, new[]{"a", "b"}),
                        new("-", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C, 0xA5, 0xE2), "({0} - {1})", false, new[]{"a", "b"}),
                        new("*", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C, 0xA5, 0xE2), "({0} * {1})", false, new[]{"a", "b"}),
                        new("/", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C, 0xA5, 0xE2), "({0} / {1})", false, new[]{"a", "b"}),
                        new("//", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C, 0xA5, 0xE2), "({0} // {1})", false, new[]{"a", "b"}),
                        new("%", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C, 0xA5, 0xE2), "({0} % {1})", false, new[]{"a", "b"}),
                        new("**", BlockShape.Reporter, BlockCategory.Math, "Arithmetic", Color.FromArgb(0x2C, 0xA5, 0xE2), "({0} ** {1})", false, new[]{"a", "b"})
                    }},
                    new("Built-in Math") { Blocks = {
                        new("abs()", BlockShape.Reporter, BlockCategory.Math, "Built-in Math", Color.FromArgb(0x2C, 0xA5, 0xE2), "abs({0})", false, new[]{"x"}),
                        new("round()", BlockShape.Reporter, BlockCategory.Math, "Built-in Math", Color.FromArgb(0x2C, 0xA5, 0xE2), "round({0})", false, new[]{"x"}),
                        new("min()", BlockShape.Reporter, BlockCategory.Math, "Built-in Math", Color.FromArgb(0x2C, 0xA5, 0xE2), "min({0})", false, new[]{"a, b"}),
                        new("max()", BlockShape.Reporter, BlockCategory.Math, "Built-in Math", Color.FromArgb(0x2C, 0xA5, 0xE2), "max({0})", false, new[]{"a, b"}),
                        new("sum()", BlockShape.Reporter, BlockCategory.Math, "Built-in Math", Color.FromArgb(0x2C, 0xA5, 0xE2), "sum({0})", false, new[]{"lst"})
                    }},
                    new("Random") { Blocks = {
                        new("random()", BlockShape.Reporter, BlockCategory.Math, "Random", Color.FromArgb(0x2C, 0xA5, 0xE2), "random.random()"),
                        new("randint()", BlockShape.Reporter, BlockCategory.Math, "Random", Color.FromArgb(0x2C, 0xA5, 0xE2), "random.randint({0}, {1})", false, new[]{"0", "100"}),
                        new("choice()", BlockShape.Reporter, BlockCategory.Math, "Random", Color.FromArgb(0x2C, 0xA5, 0xE2), "random.choice({0})", false, new[]{"lst"}),
                        new("shuffle()", BlockShape.Stack, BlockCategory.Math, "Random", Color.FromArgb(0x2C, 0xA5, 0xE2), "random.shuffle({0})", false, new[]{"lst"})
                    }},
                    new("Advanced") { Blocks = {
                        new("sin()", BlockShape.Reporter, BlockCategory.Math, "Advanced", Color.FromArgb(0x2C, 0xA5, 0xE2), "math.sin({0})", false, new[]{"x"}),
                        new("cos()", BlockShape.Reporter, BlockCategory.Math, "Advanced", Color.FromArgb(0x2C, 0xA5, 0xE2), "math.cos({0})", false, new[]{"x"}),
                        new("tan()", BlockShape.Reporter, BlockCategory.Math, "Advanced", Color.FromArgb(0x2C, 0xA5, 0xE2), "math.tan({0})", false, new[]{"x"}),
                        new("sqrt()", BlockShape.Reporter, BlockCategory.Math, "Advanced", Color.FromArgb(0x2C, 0xA5, 0xE2), "math.sqrt({0})", false, new[]{"x"})
                    }}
                }
            },
            new(BlockCategory.Files, "FILES", Color.FromArgb(0x8B, 0x5E, 0x3C), "files") {
                SubCategories = {
                    new("Text Files") { Blocks = {
                        new("open()", BlockShape.Reporter, BlockCategory.Files, "Text Files", Color.FromArgb(0x8B, 0x5E, 0x3C), "open({0}, {1})", false, new[]{"'file.txt'", "'r'"}),
                        new("read()", BlockShape.Reporter, BlockCategory.Files, "Text Files", Color.FromArgb(0x8B, 0x5E, 0x3C), "{0}.read()", false, new[]{"f"}),
                        new("readline()", BlockShape.Reporter, BlockCategory.Files, "Text Files", Color.FromArgb(0x8B, 0x5E, 0x3C), "{0}.readline()", false, new[]{"f"}),
                        new("write()", BlockShape.Stack, BlockCategory.Files, "Text Files", Color.FromArgb(0x8B, 0x5E, 0x3C), "{0}.write({1})", false, new[]{"f", "'text'"}),
                        new("append()", BlockShape.Stack, BlockCategory.Files, "Text Files", Color.FromArgb(0x8B, 0x5E, 0x3C), "open({0}, 'a').write({1})", false, new[]{"'file.txt'", "'text'"})
                    }},
                    new("Binary Files") { Blocks = {
                        new("rb mode", BlockShape.Reporter, BlockCategory.Files, "Binary Files", Color.FromArgb(0x8B, 0x5E, 0x3C), "open({0}, 'rb')", false, new[]{"'file.bin'"}),
                        new("wb mode", BlockShape.Reporter, BlockCategory.Files, "Binary Files", Color.FromArgb(0x8B, 0x5E, 0x3C), "open({0}, 'wb')", false, new[]{"'file.bin'"}),
                        new("readbytes()", BlockShape.Reporter, BlockCategory.Files, "Binary Files", Color.FromArgb(0x8B, 0x5E, 0x3C), "{0}.read()", false, new[]{"f"}),
                        new("writebytes()", BlockShape.Stack, BlockCategory.Files, "Binary Files", Color.FromArgb(0x8B, 0x5E, 0x3C), "{0}.write({1})", false, new[]{"f", "b'data'"})
                    }},
                    new("File System") { Blocks = {
                        new("exists()", BlockShape.Boolean, BlockCategory.Files, "File System", Color.FromArgb(0x8B, 0x5E, 0x3C), "os.path.exists({0})", false, new[]{"'path'"}),
                        new("remove()", BlockShape.Stack, BlockCategory.Files, "File System", Color.FromArgb(0x8B, 0x5E, 0x3C), "os.remove({0})", false, new[]{"'file'"}),
                        new("rename()", BlockShape.Stack, BlockCategory.Files, "File System", Color.FromArgb(0x8B, 0x5E, 0x3C), "os.rename({0}, {1})", false, new[]{"'old'", "'new'"}),
                        new("listdir()", BlockShape.Reporter, BlockCategory.Files, "File System", Color.FromArgb(0x8B, 0x5E, 0x3C), "os.listdir({0})", false, new[]{"'.'"})
                    }},
                    new("Paths") { Blocks = {
                        new("join()", BlockShape.Reporter, BlockCategory.Files, "Paths", Color.FromArgb(0x8B, 0x5E, 0x3C), "os.path.join({0})", false, new[]{"'a', 'b'"}),
                        new("split()", BlockShape.Reporter, BlockCategory.Files, "Paths", Color.FromArgb(0x8B, 0x5E, 0x3C), "os.path.split({0})", false, new[]{"'path'"}),
                        new("basename()", BlockShape.Reporter, BlockCategory.Files, "Paths", Color.FromArgb(0x8B, 0x5E, 0x3C), "os.path.basename({0})", false, new[]{"'path'"})
                    }}
                }
            },
            new(BlockCategory.UI, "UI", Color.FromArgb(0x0E, 0x9A, 0x6C), "ui") {
                SubCategories = {
                    new("Window") { Blocks = {
                        new("create window", BlockShape.Stack, BlockCategory.UI, "Window", Color.FromArgb(0x0E, 0x9A, 0x6C), "root = tk.Tk()"),
                        new("show", BlockShape.Stack, BlockCategory.UI, "Window", Color.FromArgb(0x0E, 0x9A, 0x6C), "root.mainloop()"),
                        new("hide", BlockShape.Stack, BlockCategory.UI, "Window", Color.FromArgb(0x0E, 0x9A, 0x6C), "root.withdraw()")
                    }},
                    new("Controls") { Blocks = {
                        new("button", BlockShape.Stack, BlockCategory.UI, "Controls", Color.FromArgb(0x0E, 0x9A, 0x6C), "tk.Button({0}, text={1})", false, new[]{"root", "'Click'"}),
                        new("label", BlockShape.Stack, BlockCategory.UI, "Controls", Color.FromArgb(0x0E, 0x9A, 0x6C), "tk.Label({0}, text={1})", false, new[]{"root", "'Hello'"}),
                        new("textbox", BlockShape.Stack, BlockCategory.UI, "Controls", Color.FromArgb(0x0E, 0x9A, 0x6C), "tk.Entry({0})", false, new[]{"root"}),
                        new("checkbox", BlockShape.Stack, BlockCategory.UI, "Controls", Color.FromArgb(0x0E, 0x9A, 0x6C), "tk.Checkbutton({0}, text={1})", false, new[]{"root", "'Option'"}),
                        new("slider", BlockShape.Stack, BlockCategory.UI, "Controls", Color.FromArgb(0x0E, 0x9A, 0x6C), "tk.Scale({0}, from_={1}, to={2})", false, new[]{"root", "0", "100"})
                    }},
                    new("Layout") { Blocks = {
                        new("grid", BlockShape.Stack, BlockCategory.UI, "Layout", Color.FromArgb(0x0E, 0x9A, 0x6C), "{0}.grid(row={1}, column={2})", false, new[]{"widget", "0", "0"}),
                        new("vertical", BlockShape.Stack, BlockCategory.UI, "Layout", Color.FromArgb(0x0E, 0x9A, 0x6C), "{0}.pack(side=tk.TOP)", false, new[]{"widget"}),
                        new("horizontal", BlockShape.Stack, BlockCategory.UI, "Layout", Color.FromArgb(0x0E, 0x9A, 0x6C), "{0}.pack(side=tk.LEFT)", false, new[]{"widget"})
                    }},
                    new("Events") { Blocks = {
                        new("click", BlockShape.Stack, BlockCategory.UI, "Events", Color.FromArgb(0x0E, 0x9A, 0x6C), "{0}.bind('<Button-1>', {1})", false, new[]{"widget", "callback"}),
                        new("hover", BlockShape.Stack, BlockCategory.UI, "Events", Color.FromArgb(0x0E, 0x9A, 0x6C), "{0}.bind('<Enter>', {1})", false, new[]{"widget", "callback"}),
                        new("change", BlockShape.Stack, BlockCategory.UI, "Events", Color.FromArgb(0x0E, 0x9A, 0x6C), "{0}.bind('<Modified>', {1})", false, new[]{"widget", "callback"})
                    }}
                }
            },
            new(BlockCategory.Time, "TIME", Color.FromArgb(0x2E, 0x8B, 0x8B), "time") {
                SubCategories = {
                    new("Current") { Blocks = {
                        new("now()", BlockShape.Reporter, BlockCategory.Time, "Current", Color.FromArgb(0x2E, 0x8B, 0x8B), "datetime.now()"),
                        new("timestamp()", BlockShape.Reporter, BlockCategory.Time, "Current", Color.FromArgb(0x2E, 0x8B, 0x8B), "time.time()")
                    }},
                    new("Sleep") { Blocks = {
                        new("sleep()", BlockShape.Stack, BlockCategory.Time, "Sleep", Color.FromArgb(0x2E, 0x8B, 0x8B), "time.sleep({0})", false, new[]{"1"})
                    }},
                    new("Formatting") { Blocks = {
                        new("strftime()", BlockShape.Reporter, BlockCategory.Time, "Formatting", Color.FromArgb(0x2E, 0x8B, 0x8B), "{0}.strftime({1})", false, new[]{"dt", "'%Y-%m-%d'"}),
                        new("parse", BlockShape.Reporter, BlockCategory.Time, "Formatting", Color.FromArgb(0x2E, 0x8B, 0x8B), "datetime.strptime({0}, {1})", false, new[]{"'date'", "'%Y-%m-%d'"})
                    }}
                }
            },
            new(BlockCategory.System, "SYSTEM", Color.FromArgb(0x55, 0x55, 0x55), "system") {
                SubCategories = {
                    new("OS") { Blocks = {
                        new("platform", BlockShape.Reporter, BlockCategory.System, "OS", Color.FromArgb(0x55, 0x55, 0x55), "sys.platform"),
                        new("environment", BlockShape.Reporter, BlockCategory.System, "OS", Color.FromArgb(0x55, 0x55, 0x55), "os.environ")
                    }},
                    new("Process") { Blocks = {
                        new("exit()", BlockShape.Stack, BlockCategory.System, "Process", Color.FromArgb(0x55, 0x55, 0x55), "sys.exit({0})", false, new[]{"0"}),
                        new("argv", BlockShape.Reporter, BlockCategory.System, "Process", Color.FromArgb(0x55, 0x55, 0x55), "sys.argv")
                    }},
                    new("Clipboard") { Blocks = {
                        new("copy", BlockShape.Stack, BlockCategory.System, "Clipboard", Color.FromArgb(0x55, 0x55, 0x55), "pyperclip.copy({0})", false, new[]{"text"}),
                        new("paste", BlockShape.Reporter, BlockCategory.System, "Clipboard", Color.FromArgb(0x55, 0x55, 0x55), "pyperclip.paste()")
                    }}
                }
            },
            new(BlockCategory.Advanced, "ADVANCED", Color.FromArgb(0x4B, 0x4A, 0x60), "advanced") {
                SubCategories = {
                    new("Imports") { Blocks = {
                        new("import", BlockShape.Stack, BlockCategory.Advanced, "Imports", Color.FromArgb(0x4B, 0x4A, 0x60), "import {0}", false, new[]{"module"}),
                        new("from", BlockShape.Stack, BlockCategory.Advanced, "Imports", Color.FromArgb(0x4B, 0x4A, 0x60), "from {0} import {1}", false, new[]{"module", "name"})
                    }},
                    new("Async") { Blocks = {
                        new("async", BlockShape.Stack, BlockCategory.Advanced, "Async", Color.FromArgb(0x4B, 0x4A, 0x60), "async def {0}():", true, new[]{"func"}),
                        new("await", BlockShape.Stack, BlockCategory.Advanced, "Async", Color.FromArgb(0x4B, 0x4A, 0x60), "await {0}", false, new[]{"coro"})
                    }},
                    new("Generators") { Blocks = {
                        new("yield", BlockShape.Stack, BlockCategory.Advanced, "Generators", Color.FromArgb(0x4B, 0x4A, 0x60), "yield {0}", false, new[]{"value"})
                    }},
                    new("Typing") { Blocks = {
                        new("type hints", BlockShape.Stack, BlockCategory.Advanced, "Typing", Color.FromArgb(0x4B, 0x4A, 0x60), "{0}: {1} = {2}", false, new[]{"x", "int", "0"}),
                        new("Optional", BlockShape.Reporter, BlockCategory.Advanced, "Typing", Color.FromArgb(0x4B, 0x4A, 0x60), "Optional[{0}]", false, new[]{"int"}),
                        new("List[T]", BlockShape.Reporter, BlockCategory.Advanced, "Typing", Color.FromArgb(0x4B, 0x4A, 0x60), "List[{0}]", false, new[]{"int"})
                    }},
                    new("Reflection") { Blocks = {
                        new("getattr", BlockShape.Reporter, BlockCategory.Advanced, "Reflection", Color.FromArgb(0x4B, 0x4A, 0x60), "getattr({0}, {1})", false, new[]{"obj", "'attr'"}),
                        new("setattr", BlockShape.Stack, BlockCategory.Advanced, "Reflection", Color.FromArgb(0x4B, 0x4A, 0x60), "setattr({0}, {1}, {2})", false, new[]{"obj", "'attr'", "val"}),
                        new("hasattr", BlockShape.Boolean, BlockCategory.Advanced, "Reflection", Color.FromArgb(0x4B, 0x4A, 0x60), "hasattr({0}, {1})", false, new[]{"obj", "'attr'"})
                    }},
                    new("Memory") { Blocks = {
                        new("gc", BlockShape.Stack, BlockCategory.Advanced, "Memory", Color.FromArgb(0x4B, 0x4A, 0x60), "gc.collect()"),
                        new("sys", BlockShape.Reporter, BlockCategory.Advanced, "Memory", Color.FromArgb(0x4B, 0x4A, 0x60), "sys.getsizeof({0})", false, new[]{"obj"})
                    }}
                }
            }
        };
    }

    private void BuildUI()
    {
        mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = Color.FromArgb(0xDD, 0xEE, 0xDE)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 415)); // Block Storage
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // Workspace
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 415)); // Python Code
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        // ── TOP PANEL ──────────────────────────
        topPanel = new Panel { BackColor = Color.FromArgb(0xCD, 0xCD, 0xD2), Dock = DockStyle.Fill, Margin = new Padding(0) };
        var topLabel = new Label
        {
            Text = "  SoftwareBuilder – Visual Python Programming",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x33, 0x33, 0x33),
            AutoSize = true,
            Location = new Point(8, 6)
        };
        runButton = new Button
        {
            Text = "▶ Run",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x5C, 0xB7, 0x12),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Size = new Size(80, 28),
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Cursor = Cursors.Hand
        };
        runButton.FlatAppearance.BorderSize = 0;
        runButton.Click += (s, e) => UpdatePythonCode();
        // Position Run button at the right
        topPanel.Controls.Add(topLabel);
        topPanel.Controls.Add(runButton);
        runButton.Location = new Point(topPanel.Width - runButton.Width - 10, 4);
        topPanel.Resize += (s, e) => runButton.Location = new Point(topPanel.Width - runButton.Width - 10, 4);

        clearButton = new Button { /* hidden, replaced by right-click */ Visible = false };
        copyButton = new Button { /* hidden */ Visible = false };

        // ── BLOCK STORAGE PANEL (Left) ─────────
        var storageContainer = new Panel
        {
            BackColor = Color.FromArgb(0xF0, 0xF2, 0xF0),
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BorderStyle = BorderStyle.FixedSingle
        };
        // Search Bar
        searchBox = new TextBox
        {
            Text = "Search blocks...",
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9f),
            Width = 390,
            Location = new Point(4, 4)
        };
        searchBox.Enter += (s, e) => { if (searchBox.Text == "Search blocks...") { searchBox.Text = ""; searchBox.ForeColor = Color.Black; } };
        searchBox.Leave += (s, e) => { if (string.IsNullOrWhiteSpace(searchBox.Text)) { searchBox.Text = "Search blocks..."; searchBox.ForeColor = Color.Gray; } };
        searchBox.TextChanged += (s, e) => PopulateSubCategories(selectedCategory);
        storageContainer.Controls.Add(searchBox);

        // Category Grid (3x4)
        categoryGrid = new TableLayoutPanel
        {
            Location = new Point(4, 30),
            Size = new Size(406, 220),
            ColumnCount = 3,
            RowCount = 4,
            BackColor = Color.Transparent,
            Padding = new Padding(2)
        };
        for (int i = 0; i < 3; i++) categoryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        for (int i = 0; i < 4; i++) categoryGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        int idx = 0;
        foreach (var cat in categories)
        {
            var btn = new CategoryButton(cat);
            btn.Dock = DockStyle.Fill;
            categoryGrid.Controls.Add(btn, idx % 3, idx / 3);
            idx++;
        }
        storageContainer.Controls.Add(categoryGrid);

        // Sub-category panel (scrollable)
        subCategoryPanel = new FlowLayoutPanel
        {
            Location = new Point(4, 254),
            Size = new Size(406, 400),
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.FromArgb(0xF5, 0xF7, 0xF5),
            BorderStyle = BorderStyle.None
        };
        storageContainer.Controls.Add(subCategoryPanel);

        // Select first category by default
        if (categories.Count > 0)
        {
            selectedCategory = categories[0];
            ((CategoryButton)categoryGrid.Controls[0]).IsSelected = true;
            categoryGrid.Controls[0].Invalidate();
            PopulateSubCategories(selectedCategory);
        }

        // ── WORKSPACE PANEL (Center) ────────────
        var workspaceContainer = new Panel
        {
            BackColor = Color.FromArgb(0xDD, 0xEE, 0xDE),
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 0, 2, 0),
            BorderStyle = BorderStyle.FixedSingle
        };
        var wsLabel = new Label
        {
            Text = "Block Space  (Drag blocks here)",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x5C, 0x5C, 0x5C),
            BackColor = Color.FromArgb(0xCD, 0xCD, 0xD2),
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter
        };
        workspacePanel = new WorkspacePanel { Dock = DockStyle.Fill };
        workspacePanel.CodeChanged += UpdatePythonCode;
        workspaceContainer.Controls.Add(workspacePanel);
        workspaceContainer.Controls.Add(wsLabel);

        // ── PYTHON CODE PANEL (Right) ───────────
        var codeContainer = new Panel
        {
            BackColor = Color.FromArgb(0x2D, 0x2D, 0x2D),
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BorderStyle = BorderStyle.FixedSingle
        };
        var codeLabel = new Label
        {
            Text = "Translated Python Code",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(0x5C, 0x5C, 0x5C),
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter
        };
        pythonCodeBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(0x1E, 0x1E, 0x1E),
            ForeColor = Color.FromArgb(0xD4, 0xD4, 0xD4),
            Font = new Font("Consolas", 10.5f, FontStyle.Regular),
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Text = "# Drag blocks here to build your Python program\n",
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        codeContainer.Controls.Add(pythonCodeBox);
        codeContainer.Controls.Add(codeLabel);

        // ── BOTTOM PANEL ────────────────────────
        bottomPanel = new Panel { BackColor = Color.FromArgb(0xCD, 0xCD, 0xD2), Dock = DockStyle.Fill, Margin = new Padding(0) };
        statusLabel = new Label
        {
            Text = "  Ready – Select a category and drag blocks into the workspace.",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(0x44, 0x44, 0x44),
            AutoSize = true,
            Location = new Point(8, 4)
        };
        bottomPanel.Controls.Add(statusLabel);

        // Add to main layout
        mainLayout.Controls.Add(topPanel, 0, 0);
        mainLayout.SetColumnSpan(topPanel, 3);
        mainLayout.Controls.Add(storageContainer, 0, 1);
        mainLayout.Controls.Add(workspaceContainer, 1, 1);
        mainLayout.Controls.Add(codeContainer, 2, 1);
        mainLayout.Controls.Add(bottomPanel, 0, 2);
        mainLayout.SetColumnSpan(bottomPanel, 3);

        Controls.Add(mainLayout);
    }

    // Called when a category button is clicked
    public void OnCategorySelected(CategoryInfo cat)
    {
        selectedCategory = cat;
        PopulateSubCategories(cat);
        // Update the category grid buttons' visual states
        foreach (Control c in categoryGrid.Controls)
            if (c is CategoryButton cb) cb.Invalidate();
    }

    private void PopulateSubCategories(CategoryInfo cat)
    {
        subCategoryPanel.Controls.Clear();
        if (cat == null) return;
        string filter = searchBox.Text.Trim();
        if (filter == "Search blocks..." || filter == "") filter = "";

        foreach (var sub in cat.SubCategories)
        {
            var filteredBlocks = sub.Blocks.Where(b =>
                string.IsNullOrEmpty(filter) || b.Label.ToLower().Contains(filter.ToLower())).ToList();
            if (filteredBlocks.Count == 0) continue;

            // Round label for sub-category
            var roundLabel = new RoundLabel(sub.Name);
            // Auto-size width based on text
            int textWidth = TextRenderer.MeasureText(sub.Name, roundLabel.Font).Width + 20;
            roundLabel.Width = textWidth > 100 ? textWidth : 100;
            subCategoryPanel.Controls.Add(roundLabel);

            foreach (var block in filteredBlocks)
            {
                var item = new BlockStorageItem(block);
                subCategoryPanel.Controls.Add(item);
            }
        }
    }

    private void UpdatePythonCode()
    {
        string code = workspacePanel.GeneratePython();
        pythonCodeBox.Text = code;
        statusLabel.Text = $"  Python code updated – {workspacePanel.Blocks.Count} blocks placed.";
    }
}
