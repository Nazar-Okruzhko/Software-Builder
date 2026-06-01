// ----------------------------------------------------------------------------
//  PyStencyl  —  Visual Python Programming Editor
//  .NET 6 · WPF · Single Source File · No XAML
//  Blocks are drawn as authentic Scratch / Snap! jigsaw puzzle pieces:
//      CommandBlock  : rectangle + top notch (female) + bottom bump (male)
//      HatBlock      : curved arch top + bottom bump  (script start)
//      ReporterBlock : oval / pill shape  (expression / value)
//      BooleanBlock  : hexagon / diamond  (true-false)
//      C-Block       : CommandBlock with an open mouth for child blocks
//  Build:  dotnet build   Run:  dotnet run
// ----------------------------------------------------------------------------
#nullable enable
 
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
 
namespace PyStencyl
{
    // -- Jigsaw shape constants (all in device pixels) ------------------------
    internal static class BS  // BlockShape constants
    {
        public const double R   = 3;    // corner radius
        public const double NX  = 13;   // notch / bump left edge x
        public const double NW  = 16;   // notch / bump width
        public const double ND  = 7;    // notch depth going INTO body top
        public const double BD  = 7;    // bump protrusion below body bottom
        public const double BlockH  = 32; // standard body height
        public const double HatH    = 48; // hat block body height  
        public const double HatArch = 16; // extra arch above hat body
    }
 
    // ------------------------------------------------------------------------
    //  MODELS
    // ------------------------------------------------------------------------
    public enum BShape { Hat, Command, Control, Reporter, Boolean }
 
    public sealed class BParam
    {
        public string Name    { get; init; } = "";
        public string Default { get; init; } = "";
    }
 
    public sealed class BDef   // Block Definition
    {
        public string   Id      { get; init; } = "";
        public string   Label   { get; init; } = "";    // display label with {param} holes
        public string   Python  { get; init; } = "";    // code template
        public BShape   Shape   { get; init; } = BShape.Command;
        public string   Cat     { get; init; } = "";
        public string   Sub     { get; init; } = "";
        public BParam[] Params  { get; init; } = Array.Empty<BParam>();
        public bool     HasBody { get; init; } = false; // C-block with inner slot
        public string   Tip     { get; init; } = "";
    }
 
    public sealed class BInst  // Block Instance on canvas
    {
        public BDef                       Def     { get; }
        public Dictionary<string,string>  Vals    { get; } = new();
        public int                        Indent  { get; set; } = 0;
 
        public BInst(BDef d)
        {
            Def = d;
            foreach (var p in d.Params) Vals[p.Name] = p.Default;
        }
 
        public string ToPython()
        {
            var s = Def.Python;
            foreach (var kv in Vals)
                s = s.Replace("{" + kv.Key + "}",
                    string.IsNullOrWhiteSpace(kv.Value) ? $"<{kv.Key}>" : kv.Value);
            return s;
        }
    }
 
    // ------------------------------------------------------------------------
    //  GEOMETRY — jigsaw puzzle path factory
    // ------------------------------------------------------------------------
    internal static class BlockGeom
    {
        // -- Command block: rectangular body + top notch + bottom bump --------
        public static Geometry Command(double w, double h,
            bool topNotch = true, bool bottomBump = true)
        {
            double r = BS.R, nx = BS.NX, nw = BS.NW, nd = BS.ND, bd = BS.BD;
            var fig = new PathFigure { IsClosed = true, IsFilled = true,
                StartPoint = new Point(r, 0) };
 
            // -- TOP EDGE (left ? right) ------------------------------------
            if (topNotch)
            {
                fig.Segments.Add(L(nx,    0 ));
                fig.Segments.Add(L(nx,    nd));     // down into notch
                fig.Segments.Add(L(nx+nw, nd));     // across notch floor
                fig.Segments.Add(L(nx+nw, 0 ));     // back up
            }
            fig.Segments.Add(L(w-r, 0));
            fig.Segments.Add(A(w,   r,   r));        // top-right corner
 
            // -- RIGHT EDGE (top ? bottom) ----------------------------------
            fig.Segments.Add(L(w, h-r));
            fig.Segments.Add(A(w-r, h, r));          // bottom-right corner
 
            // -- BOTTOM EDGE (right ? left) ---------------------------------
            if (bottomBump)
            {
                fig.Segments.Add(L(nx+nw, h    ));
                fig.Segments.Add(L(nx+nw, h+bd ));  // bump down
                fig.Segments.Add(L(nx,    h+bd ));  // bump floor
                fig.Segments.Add(L(nx,    h    ));  // bump back up
            }
            fig.Segments.Add(L(r, h));
            fig.Segments.Add(A(0, h-r, r));          // bottom-left corner
 
            // -- LEFT EDGE (bottom ? top) -----------------------------------
            fig.Segments.Add(L(0, r));
            fig.Segments.Add(A(r, 0, r));            // top-left corner
 
            return Pg(fig);
        }
 
        // -- Hat block: curved arch top + bottom bump ---------------------
        public static Geometry Hat(double w)
        {
            double r   = BS.R;
            double ah  = BS.HatArch;  // arch height above body
            double h   = BS.HatH;
            double nx  = BS.NX; double nw = BS.NW; double bd = BS.BD;
            double sp  = Math.Min(w * 0.6, w - r);  // arch spread
            double cy  = -ah * 0.2;                   // bezier control y
 
            var fig = new PathFigure { IsClosed = true, IsFilled = true,
                StartPoint = new Point(0, ah + r) };
 
            // LEFT SIDE — up the arch
            fig.Segments.Add(new ArcSegment(new Point(sp * 0.2, cy + ah * 0.5),
                new Size(sp * 0.4, ah + r), 0, false, SweepDirection.Counterclockwise, true));
            // TOP of arch — bezier across
            fig.Segments.Add(new BezierSegment(
                new Point(sp * 0.5, -ah),
                new Point(sp * 0.9, -ah * 0.2),
                new Point(sp, ah * 0.8), true));
            // RIGHT SIDE — back down
            fig.Segments.Add(new BezierSegment(
                new Point(sp * 1.1, ah * 1.8),
                new Point(w - r, ah),
                new Point(w - r, ah + r), true));
            // arch to body transition: top-right
            fig.Segments.Add(A(w, ah + r, r));
 
            // RIGHT EDGE
            fig.Segments.Add(L(w, h - r));
            fig.Segments.Add(A(w-r, h, r));
 
            // BOTTOM with bump
            fig.Segments.Add(L(nx+nw, h    ));
            fig.Segments.Add(L(nx+nw, h+bd ));
            fig.Segments.Add(L(nx,    h+bd ));
            fig.Segments.Add(L(nx,    h    ));
            fig.Segments.Add(L(r, h));
            fig.Segments.Add(A(0, h-r, r));
            fig.Segments.Add(L(0, ah+r));
 
            return Pg(fig);
        }
 
        // -- Reporter block: pill / oval shape (no notch or bump) ---------
        public static Geometry Reporter(double w, double h)
        {
            double rr = Math.Min(h / 2.0, 12);
            var fig = new PathFigure { IsClosed = true, IsFilled = true,
                StartPoint = new Point(rr, 0) };
            fig.Segments.Add(L(w-rr, 0));
            fig.Segments.Add(new ArcSegment(new Point(w, rr),
                new Size(rr,rr), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(L(w, h-rr));
            fig.Segments.Add(new ArcSegment(new Point(w-rr, h),
                new Size(rr,rr), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(L(rr, h));
            fig.Segments.Add(new ArcSegment(new Point(0, h-rr),
                new Size(rr,rr), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(L(0, rr));
            fig.Segments.Add(new ArcSegment(new Point(rr, 0),
                new Size(rr,rr), 0, false, SweepDirection.Clockwise, true));
            return Pg(fig);
        }
 
        // -- Boolean block: hexagon shape ----------------------------------
        public static Geometry Bool(double w, double h)
        {
            double pt = h / 2;  // pointy side size
            var fig = new PathFigure { IsClosed = true, IsFilled = true,
                StartPoint = new Point(pt, 0) };
            fig.Segments.Add(L(w-pt, 0));
            fig.Segments.Add(L(w,    h/2));
            fig.Segments.Add(L(w-pt, h));
            fig.Segments.Add(L(pt,   h));
            fig.Segments.Add(L(0,    h/2));
            return Pg(fig);
        }
 
        // -- C-Block: Command shape but with a mouth/arm opening -----------
        public static Geometry CBlock(double w, double headerH, double mouthH)
        {
            double r  = BS.R; double nx = BS.NX; double nw = BS.NW;
            double nd = BS.ND; double bd = BS.BD;
            double aw = 24;   // arm width (left indent for inner blocks)
            double h  = headerH + mouthH + 12; // total height inc bottom arm
 
            var fig = new PathFigure { IsClosed = true, IsFilled = true,
                StartPoint = new Point(r, 0) };
 
            // TOP EDGE with notch
            fig.Segments.Add(L(nx,    0 )); fig.Segments.Add(L(nx,    nd));
            fig.Segments.Add(L(nx+nw, nd)); fig.Segments.Add(L(nx+nw, 0 ));
            fig.Segments.Add(L(w-r, 0)); fig.Segments.Add(A(w, r, r));
 
            // RIGHT EDGE down to header bottom
            fig.Segments.Add(L(w, headerH - r)); fig.Segments.Add(A(w-r, headerH, r));
 
            // INNER TOP (mouth ceiling) — step inward
            fig.Segments.Add(L(aw+r, headerH));
            fig.Segments.Add(A(aw, headerH+r, r));
 
            // INNER LEFT (mouth left wall) 
            fig.Segments.Add(L(aw, headerH + mouthH - r));
            fig.Segments.Add(A(aw+r, headerH+mouthH, r));
 
            // INNER BOTTOM — bottom arm top edge with inner notch
            double bax = aw; // bottom arm left x
            fig.Segments.Add(L(nx+nw, headerH+mouthH));
            fig.Segments.Add(L(nx+nw, headerH+mouthH+nd));
            fig.Segments.Add(L(nx,    headerH+mouthH+nd));
            fig.Segments.Add(L(nx,    headerH+mouthH));
            fig.Segments.Add(L(w-r,   headerH+mouthH));
            fig.Segments.Add(A(w,     headerH+mouthH+r, r));
 
            // Continue down and close bottom
            fig.Segments.Add(L(w, h-r)); fig.Segments.Add(A(w-r, h, r));
            // Bottom bump
            fig.Segments.Add(L(nx+nw, h)); fig.Segments.Add(L(nx+nw, h+bd));
            fig.Segments.Add(L(nx,    h+bd)); fig.Segments.Add(L(nx, h));
            fig.Segments.Add(L(r, h)); fig.Segments.Add(A(0, h-r, r));
            fig.Segments.Add(L(0, r)); fig.Segments.Add(A(r, 0, r));
 
            return Pg(fig);
        }
 
        // -- helpers -------------------------------------------------------
        private static LineSegment L(double x, double y) =>
            new(new Point(x, y), true);
 
        private static ArcSegment A(double x, double y, double r) =>
            new(new Point(x, y), new Size(r, r), 0, false, SweepDirection.Clockwise, true);
 
        private static PathGeometry Pg(PathFigure fig)
        {
            var pg = new PathGeometry();
            pg.Figures.Add(fig);
            return pg;
        }
    }
 
    // ------------------------------------------------------------------------
    //  BLOCK LIBRARY  —  12 categories, 130 + blocks
    // ------------------------------------------------------------------------
    public static class Lib
    {
        private static BParam P(string n, string d="") => new(){Name=n,Default=d};
 
        public static readonly BDef[] All =
        {
            // -- FLOW ------------------------------------------------------
            new(){ Id="flow_comment",  Cat="FLOW", Sub="Execution", Shape=BShape.Command,
                   Label="# {comment}", Python="# {comment}",
                   Params=new[]{P("comment","your comment here")},
                   Tip="Add a comment — not executed" },
 
            new(){ Id="flow_pass",     Cat="FLOW", Sub="Execution", Shape=BShape.Command,
                   Label="pass", Python="pass",
                   Tip="Do nothing — placeholder" },
 
            new(){ Id="flow_return",   Cat="FLOW", Sub="Execution", Shape=BShape.Command,
                   Label="return {value}", Python="return {value}",
                   Params=new[]{P("value","")},
                   Tip="Return a value from the function" },
 
            new(){ Id="flow_if",       Cat="FLOW", Sub="Conditions", Shape=BShape.Control, HasBody=true,
                   Label="if {condition} :", Python="if {condition}:",
                   Params=new[]{P("condition","x > 0")},
                   Tip="Execute body if condition is True" },
 
            new(){ Id="flow_elif",     Cat="FLOW", Sub="Conditions", Shape=BShape.Control, HasBody=true,
                   Label="elif {condition} :", Python="elif {condition}:",
                   Params=new[]{P("condition","x == 0")},
                   Tip="Else-if branch" },
 
            new(){ Id="flow_else",     Cat="FLOW", Sub="Conditions", Shape=BShape.Control, HasBody=true,
                   Label="else :", Python="else:",
                   Tip="Default branch when all conditions are False" },
 
            new(){ Id="flow_match",    Cat="FLOW", Sub="Conditions", Shape=BShape.Control, HasBody=true,
                   Label="match {value} :", Python="match {value}:",
                   Params=new[]{P("value","status")},
                   Tip="Structural pattern matching (Python 3.10+)" },
 
            new(){ Id="flow_for",      Cat="FLOW", Sub="Loops", Shape=BShape.Control, HasBody=true,
                   Label="for {var} in {iter} :", Python="for {var} in {iter}:",
                   Params=new[]{P("var","i"),P("iter","items")},
                   Tip="Iterate over each item in a sequence" },
 
            new(){ Id="flow_while",    Cat="FLOW", Sub="Loops", Shape=BShape.Control, HasBody=true,
                   Label="while {condition} :", Python="while {condition}:",
                   Params=new[]{P("condition","True")},
                   Tip="Repeat while condition stays True" },
 
            new(){ Id="flow_break",    Cat="FLOW", Sub="Loops", Shape=BShape.Command,
                   Label="break", Python="break",
                   Tip="Exit the loop immediately" },
 
            new(){ Id="flow_continue", Cat="FLOW", Sub="Loops", Shape=BShape.Command,
                   Label="continue", Python="continue",
                   Tip="Skip to the next iteration" },
 
            new(){ Id="flow_range1",   Cat="FLOW", Sub="Iteration Helpers", Shape=BShape.Reporter,
                   Label="range ( {stop} )", Python="range({stop})",
                   Params=new[]{P("stop","10")},
                   Tip="Generate numbers 0 … stop-1" },
 
            new(){ Id="flow_range3",   Cat="FLOW", Sub="Iteration Helpers", Shape=BShape.Reporter,
                   Label="range ( {start} , {stop} , {step} )", Python="range({start}, {stop}, {step})",
                   Params=new[]{P("start","0"),P("stop","10"),P("step","1")},
                   Tip="Range with start, stop, step" },
 
            new(){ Id="flow_enumerate",Cat="FLOW", Sub="Iteration Helpers", Shape=BShape.Reporter,
                   Label="enumerate ( {iter} )", Python="enumerate({iter})",
                   Params=new[]{P("iter","items")},
                   Tip="Get (index, value) pairs" },
 
            new(){ Id="flow_zip",      Cat="FLOW", Sub="Iteration Helpers", Shape=BShape.Reporter,
                   Label="zip ( {a} , {b} )", Python="zip({a}, {b})",
                   Params=new[]{P("a","list1"),P("b","list2")},
                   Tip="Pair two sequences together" },
 
            new(){ Id="flow_reversed", Cat="FLOW", Sub="Iteration Helpers", Shape=BShape.Reporter,
                   Label="reversed ( {iter} )", Python="reversed({iter})",
                   Params=new[]{P("iter","items")},
                   Tip="Iterate in reverse" },
 
            new(){ Id="flow_try",      Cat="FLOW", Sub="Exceptions", Shape=BShape.Control, HasBody=true,
                   Label="try :", Python="try:",
                   Tip="Attempt code that might raise an error" },
 
            new(){ Id="flow_except",   Cat="FLOW", Sub="Exceptions", Shape=BShape.Control, HasBody=true,
                   Label="except {exc} :", Python="except {exc}:",
                   Params=new[]{P("exc","Exception as e")},
                   Tip="Catch an exception" },
 
            new(){ Id="flow_finally",  Cat="FLOW", Sub="Exceptions", Shape=BShape.Control, HasBody=true,
                   Label="finally :", Python="finally:",
                   Tip="Always runs after try/except" },
 
            new(){ Id="flow_raise",    Cat="FLOW", Sub="Exceptions", Shape=BShape.Command,
                   Label="raise {exc}", Python="raise {exc}",
                   Params=new[]{P("exc","ValueError(\"bad input\")")},
                   Tip="Raise an exception" },
 
            // -- VARIABLES -------------------------------------------------
            new(){ Id="var_assign",    Cat="VARIABLES", Sub="Assignment", Shape=BShape.Command,
                   Label="{name} = {value}", Python="{name} = {value}",
                   Params=new[]{P("name","x"),P("value","0")},
                   Tip="Assign a value to a variable" },
 
            new(){ Id="var_pluseq",    Cat="VARIABLES", Sub="Assignment", Shape=BShape.Command,
                   Label="{name} += {value}", Python="{name} += {value}",
                   Params=new[]{P("name","x"),P("value","1")},
                   Tip="Add and assign  (x = x + value)" },
 
            new(){ Id="var_minuseq",   Cat="VARIABLES", Sub="Assignment", Shape=BShape.Command,
                   Label="{name} -= {value}", Python="{name} -= {value}",
                   Params=new[]{P("name","x"),P("value","1")},
                   Tip="Subtract and assign" },
 
            new(){ Id="var_multeq",    Cat="VARIABLES", Sub="Assignment", Shape=BShape.Command,
                   Label="{name} *= {value}", Python="{name} *= {value}",
                   Params=new[]{P("name","x"),P("value","2")},
                   Tip="Multiply and assign" },
 
            new(){ Id="var_diveq",     Cat="VARIABLES", Sub="Assignment", Shape=BShape.Command,
                   Label="{name} /= {value}", Python="{name} /= {value}",
                   Params=new[]{P("name","x"),P("value","2")},
                   Tip="Divide and assign" },
 
            new(){ Id="var_true",      Cat="VARIABLES", Sub="Constants", Shape=BShape.Boolean,
                   Label="True", Python="True", Tip="Boolean True" },
 
            new(){ Id="var_false",     Cat="VARIABLES", Sub="Constants", Shape=BShape.Boolean,
                   Label="False", Python="False", Tip="Boolean False" },
 
            new(){ Id="var_none",      Cat="VARIABLES", Sub="Constants", Shape=BShape.Reporter,
                   Label="None", Python="None", Tip="None — null / nothing" },
 
            new(){ Id="var_int",       Cat="VARIABLES", Sub="Conversion", Shape=BShape.Reporter,
                   Label="int ( {value} )", Python="int({value})",
                   Params=new[]{P("value","x")}, Tip="Convert to integer" },
 
            new(){ Id="var_float",     Cat="VARIABLES", Sub="Conversion", Shape=BShape.Reporter,
                   Label="float ( {value} )", Python="float({value})",
                   Params=new[]{P("value","x")}, Tip="Convert to float" },
 
            new(){ Id="var_str",       Cat="VARIABLES", Sub="Conversion", Shape=BShape.Reporter,
                   Label="str ( {value} )", Python="str({value})",
                   Params=new[]{P("value","x")}, Tip="Convert to string" },
 
            new(){ Id="var_bool",      Cat="VARIABLES", Sub="Conversion", Shape=BShape.Reporter,
                   Label="bool ( {value} )", Python="bool({value})",
                   Params=new[]{P("value","x")}, Tip="Convert to True / False" },
 
            // -- FUNCTIONS -------------------------------------------------
            new(){ Id="func_def",      Cat="FUNCTIONS", Sub="Definition", Shape=BShape.Hat, HasBody=true,
                   Label="def {name} ( {params} ) :", Python="def {name}({params}):",
                   Params=new[]{P("name","my_func"),P("params","")},
                   Tip="Define a reusable function" },
 
            new(){ Id="func_call",     Cat="FUNCTIONS", Sub="Definition", Shape=BShape.Command,
                   Label="{name} ( {args} )", Python="{name}({args})",
                   Params=new[]{P("name","my_func"),P("args","")},
                   Tip="Call a function" },
 
            new(){ Id="func_print",    Cat="FUNCTIONS", Sub="Definition", Shape=BShape.Command,
                   Label="print ( {value} )", Python="print({value})",
                   Params=new[]{P("value","\"Hello, World!\"")},
                   Tip="Print to the console" },
 
            new(){ Id="func_input",    Cat="FUNCTIONS", Sub="Definition", Shape=BShape.Reporter,
                   Label="input ( {prompt} )", Python="input({prompt})",
                   Params=new[]{P("prompt","\"Enter: \"")},
                   Tip="Read user input" },
 
            new(){ Id="func_lambda",   Cat="FUNCTIONS", Sub="Definition", Shape=BShape.Reporter,
                   Label="lambda {params} : {expr}", Python="lambda {params}: {expr}",
                   Params=new[]{P("params","x"),P("expr","x * 2")},
                   Tip="Anonymous short function" },
 
            new(){ Id="func_return",   Cat="FUNCTIONS", Sub="Return", Shape=BShape.Command,
                   Label="return {value}", Python="return {value}",
                   Params=new[]{P("value","")},
                   Tip="Return a value" },
 
            new(){ Id="func_global",   Cat="FUNCTIONS", Sub="Scope", Shape=BShape.Command,
                   Label="global {name}", Python="global {name}",
                   Params=new[]{P("name","x")}, Tip="Declare a global variable" },
 
            new(){ Id="func_nonlocal", Cat="FUNCTIONS", Sub="Scope", Shape=BShape.Command,
                   Label="nonlocal {name}", Python="nonlocal {name}",
                   Params=new[]{P("name","x")}, Tip="Access outer scope variable" },
 
            new(){ Id="func_property", Cat="FUNCTIONS", Sub="Decorators", Shape=BShape.Hat,
                   Label="@property", Python="@property", Tip="Property decorator" },
 
            new(){ Id="func_static",   Cat="FUNCTIONS", Sub="Decorators", Shape=BShape.Hat,
                   Label="@staticmethod", Python="@staticmethod", Tip="Static method decorator" },
 
            new(){ Id="func_classm",   Cat="FUNCTIONS", Sub="Decorators", Shape=BShape.Hat,
                   Label="@classmethod", Python="@classmethod", Tip="Class method decorator" },
 
            // -- OBJECTS ---------------------------------------------------
            new(){ Id="obj_class",     Cat="OBJECTS", Sub="Classes", Shape=BShape.Hat, HasBody=true,
                   Label="class {name} :", Python="class {name}:",
                   Params=new[]{P("name","MyClass")}, Tip="Define a class" },
 
            new(){ Id="obj_inherit",   Cat="OBJECTS", Sub="Classes", Shape=BShape.Hat, HasBody=true,
                   Label="class {name} ( {parent} ) :", Python="class {name}({parent}):",
                   Params=new[]{P("name","Child"),P("parent","Parent")},
                   Tip="Class with inheritance" },
 
            new(){ Id="obj_init",      Cat="OBJECTS", Sub="Classes", Shape=BShape.Hat, HasBody=true,
                   Label="def __init__ ( self , {params} ) :", Python="def __init__(self, {params}):",
                   Params=new[]{P("params","")}, Tip="Constructor method" },
 
            new(){ Id="obj_self",      Cat="OBJECTS", Sub="Attributes", Shape=BShape.Command,
                   Label="self . {attr} = {value}", Python="self.{attr} = {value}",
                   Params=new[]{P("attr","name"),P("value","value")},
                   Tip="Set an instance attribute" },
 
            new(){ Id="obj_getattr",   Cat="OBJECTS", Sub="Attributes", Shape=BShape.Reporter,
                   Label="getattr ( {obj} , {attr} )", Python="getattr({obj}, {attr})",
                   Params=new[]{P("obj","obj"),P("attr","\"name\"")},
                   Tip="Get attribute by string name" },
 
            new(){ Id="obj_setattr",   Cat="OBJECTS", Sub="Attributes", Shape=BShape.Command,
                   Label="setattr ( {obj} , {attr} , {val} )", Python="setattr({obj}, {attr}, {val})",
                   Params=new[]{P("obj","obj"),P("attr","\"name\""),P("val","value")},
                   Tip="Set attribute by string name" },
 
            new(){ Id="obj_super",     Cat="OBJECTS", Sub="Inheritance", Shape=BShape.Command,
                   Label="super() . {method} ( {args} )", Python="super().{method}({args})",
                   Params=new[]{P("method","__init__"),P("args","")},
                   Tip="Call parent class method" },
 
            new(){ Id="obj_method",    Cat="OBJECTS", Sub="Methods", Shape=BShape.Hat, HasBody=true,
                   Label="def {name} ( self , {params} ) :", Python="def {name}(self, {params}):",
                   Params=new[]{P("name","my_method"),P("params","")},
                   Tip="Instance method" },
 
            // -- DATA ------------------------------------------------------
            new(){ Id="data_list",     Cat="DATA", Sub="Lists", Shape=BShape.Command,
                   Label="{name} = [ {items} ]", Python="{name} = [{items}]",
                   Params=new[]{P("name","my_list"),P("items","")},
                   Tip="Create a list" },
 
            new(){ Id="data_append",   Cat="DATA", Sub="Lists", Shape=BShape.Command,
                   Label="{list} . append ( {value} )", Python="{list}.append({value})",
                   Params=new[]{P("list","my_list"),P("value","item")},
                   Tip="Add item to end of list" },
 
            new(){ Id="data_insert",   Cat="DATA", Sub="Lists", Shape=BShape.Command,
                   Label="{list} . insert ( {idx} , {val} )", Python="{list}.insert({idx}, {val})",
                   Params=new[]{P("list","my_list"),P("idx","0"),P("val","item")},
                   Tip="Insert item at position" },
 
            new(){ Id="data_remove",   Cat="DATA", Sub="Lists", Shape=BShape.Command,
                   Label="{list} . remove ( {value} )", Python="{list}.remove({value})",
                   Params=new[]{P("list","my_list"),P("value","item")},
                   Tip="Remove first occurrence" },
 
            new(){ Id="data_pop",      Cat="DATA", Sub="Lists", Shape=BShape.Command,
                   Label="{list} . pop ( {index} )", Python="{list}.pop({index})",
                   Params=new[]{P("list","my_list"),P("index","")},
                   Tip="Remove and return item" },
 
            new(){ Id="data_sort",     Cat="DATA", Sub="Lists", Shape=BShape.Command,
                   Label="{list} . sort()", Python="{list}.sort()",
                   Params=new[]{P("list","my_list")}, Tip="Sort in place" },
 
            new(){ Id="data_lindex",   Cat="DATA", Sub="Lists", Shape=BShape.Reporter,
                   Label="{list} [ {index} ]", Python="{list}[{index}]",
                   Params=new[]{P("list","my_list"),P("index","0")},
                   Tip="Item at index" },
 
            new(){ Id="data_llen",     Cat="DATA", Sub="Lists", Shape=BShape.Reporter,
                   Label="len ( {list} )", Python="len({list})",
                   Params=new[]{P("list","my_list")}, Tip="Length of list" },
 
            new(){ Id="data_dict",     Cat="DATA", Sub="Dictionaries", Shape=BShape.Command,
                   Label="{name} = dict()", Python="{name} = dict()",
                   Params=new[]{P("name","my_dict")}, Tip="Create empty dict" },
 
            new(){ Id="data_dset",     Cat="DATA", Sub="Dictionaries", Shape=BShape.Command,
                   Label="{dict} [ {key} ] = {value}", Python="{dict}[{key}] = {value}",
                   Params=new[]{P("dict","my_dict"),P("key","\"k\""),P("value","v")},
                   Tip="Set key-value pair" },
 
            new(){ Id="data_dget",     Cat="DATA", Sub="Dictionaries", Shape=BShape.Reporter,
                   Label="{dict} . get ( {key} , {default} )", Python="{dict}.get({key}, {default})",
                   Params=new[]{P("dict","my_dict"),P("key","\"k\""),P("default","None")},
                   Tip="Get value safely" },
 
            new(){ Id="data_dkeys",    Cat="DATA", Sub="Dictionaries", Shape=BShape.Reporter,
                   Label="{dict} . keys()", Python="{dict}.keys()",
                   Params=new[]{P("dict","my_dict")}, Tip="All keys" },
 
            new(){ Id="data_dvalues",  Cat="DATA", Sub="Dictionaries", Shape=BShape.Reporter,
                   Label="{dict} . values()", Python="{dict}.values()",
                   Params=new[]{P("dict","my_dict")}, Tip="All values" },
 
            new(){ Id="data_ditems",   Cat="DATA", Sub="Dictionaries", Shape=BShape.Reporter,
                   Label="{dict} . items()", Python="{dict}.items()",
                   Params=new[]{P("dict","my_dict")}, Tip="All (key, value) pairs" },
 
            new(){ Id="data_set",      Cat="DATA", Sub="Sets", Shape=BShape.Command,
                   Label="{name} = set()", Python="{name} = set()",
                   Params=new[]{P("name","my_set")}, Tip="Create empty set" },
 
            new(){ Id="data_sadd",     Cat="DATA", Sub="Sets", Shape=BShape.Command,
                   Label="{set} . add ( {value} )", Python="{set}.add({value})",
                   Params=new[]{P("set","my_set"),P("value","item")}, Tip="Add to set" },
 
            new(){ Id="data_sunion",   Cat="DATA", Sub="Sets", Shape=BShape.Reporter,
                   Label="{a} . union ( {b} )", Python="{a}.union({b})",
                   Params=new[]{P("a","s1"),P("b","s2")}, Tip="All items in either set" },
 
            new(){ Id="data_sinter",   Cat="DATA", Sub="Sets", Shape=BShape.Reporter,
                   Label="{a} . intersection ( {b} )", Python="{a}.intersection({b})",
                   Params=new[]{P("a","s1"),P("b","s2")}, Tip="Items in both sets" },
 
            new(){ Id="data_tuple",    Cat="DATA", Sub="Tuples", Shape=BShape.Command,
                   Label="{name} = ( {values} ,)", Python="{name} = ({values},)",
                   Params=new[]{P("name","my_tuple"),P("values","1, 2, 3")},
                   Tip="Create a tuple (immutable list)" },
 
            new(){ Id="data_tunpack",  Cat="DATA", Sub="Tuples", Shape=BShape.Command,
                   Label="{a} , {b} = {tuple}", Python="{a}, {b} = {tuple}",
                   Params=new[]{P("a","a"),P("b","b"),P("tuple","my_tuple")},
                   Tip="Unpack tuple values" },
 
            // -- TEXT ------------------------------------------------------
            new(){ Id="text_str",      Cat="TEXT", Sub="Creation", Shape=BShape.Reporter,
                   Label="\" {text} \"", Python="\"{text}\"",
                   Params=new[]{P("text","Hello!")}, Tip="String literal" },
 
            new(){ Id="text_fstr",     Cat="TEXT", Sub="Creation", Shape=BShape.Reporter,
                   Label="f\" {template} \"", Python="f\"{template}\"",
                   Params=new[]{P("template","Hello, {name}!")},
                   Tip="F-string — embed variables in text" },
 
            new(){ Id="text_upper",    Cat="TEXT", Sub="Manipulation", Shape=BShape.Reporter,
                   Label="{text} . upper()", Python="{text}.upper()",
                   Params=new[]{P("text","text")}, Tip="To UPPERCASE" },
 
            new(){ Id="text_lower",    Cat="TEXT", Sub="Manipulation", Shape=BShape.Reporter,
                   Label="{text} . lower()", Python="{text}.lower()",
                   Params=new[]{P("text","text")}, Tip="To lowercase" },
 
            new(){ Id="text_strip",    Cat="TEXT", Sub="Manipulation", Shape=BShape.Reporter,
                   Label="{text} . strip()", Python="{text}.strip()",
                   Params=new[]{P("text","text")}, Tip="Remove surrounding spaces" },
 
            new(){ Id="text_replace",  Cat="TEXT", Sub="Manipulation", Shape=BShape.Reporter,
                   Label="{text} . replace ( {old} , {new} )", Python="{text}.replace({old}, {new})",
                   Params=new[]{P("text","text"),P("old","\"old\""),P("new","\"new\"")},
                   Tip="Replace all occurrences" },
 
            new(){ Id="text_split",    Cat="TEXT", Sub="Manipulation", Shape=BShape.Reporter,
                   Label="{text} . split ( {sep} )", Python="{text}.split({sep})",
                   Params=new[]{P("text","text"),P("sep","\",\"")}, Tip="Split into list" },
 
            new(){ Id="text_join",     Cat="TEXT", Sub="Manipulation", Shape=BShape.Reporter,
                   Label="{sep} . join ( {list} )", Python="{sep}.join({list})",
                   Params=new[]{P("sep","\", \""),P("list","words")}, Tip="Join list to string" },
 
            new(){ Id="text_find",     Cat="TEXT", Sub="Search", Shape=BShape.Reporter,
                   Label="{text} . find ( {sub} )", Python="{text}.find({sub})",
                   Params=new[]{P("text","text"),P("sub","\"word\"")},
                   Tip="Position of substring (-1 if absent)" },
 
            new(){ Id="text_in",       Cat="TEXT", Sub="Search", Shape=BShape.Boolean,
                   Label="{sub} in {text}", Python="{sub} in {text}",
                   Params=new[]{P("sub","\"word\""),P("text","text")},
                   Tip="True if substring present" },
 
            new(){ Id="text_starts",   Cat="TEXT", Sub="Search", Shape=BShape.Boolean,
                   Label="{text} . startswith ( {prefix} )", Python="{text}.startswith({prefix})",
                   Params=new[]{P("text","text"),P("prefix","\"hi\"")},
                   Tip="Does text start with prefix?" },
 
            new(){ Id="text_ends",     Cat="TEXT", Sub="Search", Shape=BShape.Boolean,
                   Label="{text} . endswith ( {suffix} )", Python="{text}.endswith({suffix})",
                   Params=new[]{P("text","text"),P("suffix","\"bye\"")},
                   Tip="Does text end with suffix?" },
 
            new(){ Id="text_len",      Cat="TEXT", Sub="Search", Shape=BShape.Reporter,
                   Label="len ( {text} )", Python="len({text})",
                   Params=new[]{P("text","text")}, Tip="Character count" },
 
            // -- MATH ------------------------------------------------------
            new(){ Id="math_add",      Cat="MATH", Sub="Arithmetic", Shape=BShape.Reporter,
                   Label="{a} + {b}", Python="{a} + {b}",
                   Params=new[]{P("a","x"),P("b","y")}, Tip="Add" },
 
            new(){ Id="math_sub",      Cat="MATH", Sub="Arithmetic", Shape=BShape.Reporter,
                   Label="{a} - {b}", Python="{a} - {b}",
                   Params=new[]{P("a","x"),P("b","y")}, Tip="Subtract" },
 
            new(){ Id="math_mul",      Cat="MATH", Sub="Arithmetic", Shape=BShape.Reporter,
                   Label="{a} * {b}", Python="{a} * {b}",
                   Params=new[]{P("a","x"),P("b","y")}, Tip="Multiply" },
 
            new(){ Id="math_div",      Cat="MATH", Sub="Arithmetic", Shape=BShape.Reporter,
                   Label="{a} / {b}", Python="{a} / {b}",
                   Params=new[]{P("a","x"),P("b","y")}, Tip="Divide (float result)" },
 
            new(){ Id="math_fdiv",     Cat="MATH", Sub="Arithmetic", Shape=BShape.Reporter,
                   Label="{a} // {b}", Python="{a} // {b}",
                   Params=new[]{P("a","x"),P("b","y")}, Tip="Floor divide (int result)" },
 
            new(){ Id="math_mod",      Cat="MATH", Sub="Arithmetic", Shape=BShape.Reporter,
                   Label="{a} % {b}", Python="{a} % {b}",
                   Params=new[]{P("a","x"),P("b","y")}, Tip="Remainder" },
 
            new(){ Id="math_pow",      Cat="MATH", Sub="Arithmetic", Shape=BShape.Reporter,
                   Label="{a} ** {b}", Python="{a} ** {b}",
                   Params=new[]{P("a","x"),P("b","2")}, Tip="Power / exponent" },
 
            new(){ Id="math_abs",      Cat="MATH", Sub="Built-in Math", Shape=BShape.Reporter,
                   Label="abs ( {value} )", Python="abs({value})",
                   Params=new[]{P("value","x")}, Tip="Absolute value" },
 
            new(){ Id="math_round",    Cat="MATH", Sub="Built-in Math", Shape=BShape.Reporter,
                   Label="round ( {value} , {digits} )", Python="round({value}, {digits})",
                   Params=new[]{P("value","3.14"),P("digits","2")}, Tip="Round to N decimals" },
 
            new(){ Id="math_min",      Cat="MATH", Sub="Built-in Math", Shape=BShape.Reporter,
                   Label="min ( {a} , {b} )", Python="min({a}, {b})",
                   Params=new[]{P("a","x"),P("b","y")}, Tip="Smaller value" },
 
            new(){ Id="math_max",      Cat="MATH", Sub="Built-in Math", Shape=BShape.Reporter,
                   Label="max ( {a} , {b} )", Python="max({a}, {b})",
                   Params=new[]{P("a","x"),P("b","y")}, Tip="Larger value" },
 
            new(){ Id="math_sum",      Cat="MATH", Sub="Built-in Math", Shape=BShape.Reporter,
                   Label="sum ( {iter} )", Python="sum({iter})",
                   Params=new[]{P("iter","nums")}, Tip="Sum all values" },
 
            new(){ Id="math_rimp",     Cat="MATH", Sub="Random", Shape=BShape.Hat,
                   Label="import random", Python="import random", Tip="Import random module" },
 
            new(){ Id="math_randint",  Cat="MATH", Sub="Random", Shape=BShape.Reporter,
                   Label="random . randint ( {a} , {b} )", Python="random.randint({a}, {b})",
                   Params=new[]{P("a","1"),P("b","10")}, Tip="Random int between a and b" },
 
            new(){ Id="math_choice",   Cat="MATH", Sub="Random", Shape=BShape.Reporter,
                   Label="random . choice ( {seq} )", Python="random.choice({seq})",
                   Params=new[]{P("seq","items")}, Tip="Random item from sequence" },
 
            new(){ Id="math_shuffle",  Cat="MATH", Sub="Random", Shape=BShape.Command,
                   Label="random . shuffle ( {list} )", Python="random.shuffle({list})",
                   Params=new[]{P("list","my_list")}, Tip="Shuffle list in place" },
 
            new(){ Id="math_mimp",     Cat="MATH", Sub="Advanced", Shape=BShape.Hat,
                   Label="import math", Python="import math", Tip="Import math module" },
 
            new(){ Id="math_sqrt",     Cat="MATH", Sub="Advanced", Shape=BShape.Reporter,
                   Label="math . sqrt ( {x} )", Python="math.sqrt({x})",
                   Params=new[]{P("x","x")}, Tip="Square root" },
 
            new(){ Id="math_sin",      Cat="MATH", Sub="Advanced", Shape=BShape.Reporter,
                   Label="math . sin ( {x} )", Python="math.sin({x})",
                   Params=new[]{P("x","x")}, Tip="Sine (radians)" },
 
            new(){ Id="math_pi",       Cat="MATH", Sub="Advanced", Shape=BShape.Reporter,
                   Label="math . pi", Python="math.pi", Tip="p ˜ 3.14159" },
 
            // -- FILES -----------------------------------------------------
            new(){ Id="file_with_r",   Cat="FILES", Sub="Text Files", Shape=BShape.Control, HasBody=true,
                   Label="with open ( {path} , \"r\" ) as {var} :", Python="with open({path}, \"r\") as {var}:",
                   Params=new[]{P("path","\"file.txt\""),P("var","f")},
                   Tip="Open file for reading (auto-closes)" },
 
            new(){ Id="file_with_w",   Cat="FILES", Sub="Text Files", Shape=BShape.Control, HasBody=true,
                   Label="with open ( {path} , \"w\" ) as {var} :", Python="with open({path}, \"w\") as {var}:",
                   Params=new[]{P("path","\"file.txt\""),P("var","f")},
                   Tip="Open file for writing" },
 
            new(){ Id="file_with_a",   Cat="FILES", Sub="Text Files", Shape=BShape.Control, HasBody=true,
                   Label="with open ( {path} , \"a\" ) as {var} :", Python="with open({path}, \"a\") as {var}:",
                   Params=new[]{P("path","\"file.txt\""),P("var","f")},
                   Tip="Open file for appending" },
 
            new(){ Id="file_read",     Cat="FILES", Sub="Text Files", Shape=BShape.Reporter,
                   Label="{file} . read()", Python="{file}.read()",
                   Params=new[]{P("file","f")}, Tip="Read entire file" },
 
            new(){ Id="file_write",    Cat="FILES", Sub="Text Files", Shape=BShape.Command,
                   Label="{file} . write ( {content} )", Python="{file}.write({content})",
                   Params=new[]{P("file","f"),P("content","\"text\"")}, Tip="Write to file" },
 
            new(){ Id="file_oimp",     Cat="FILES", Sub="File System", Shape=BShape.Hat,
                   Label="import os", Python="import os", Tip="Import os module" },
 
            new(){ Id="file_exists",   Cat="FILES", Sub="File System", Shape=BShape.Boolean,
                   Label="os . path . exists ( {path} )", Python="os.path.exists({path})",
                   Params=new[]{P("path","\"file.txt\"")}, Tip="File/folder exists?" },
 
            new(){ Id="file_remove",   Cat="FILES", Sub="File System", Shape=BShape.Command,
                   Label="os . remove ( {path} )", Python="os.remove({path})",
                   Params=new[]{P("path","\"file.txt\"")}, Tip="Delete a file" },
 
            new(){ Id="file_listdir",  Cat="FILES", Sub="File System", Shape=BShape.Reporter,
                   Label="os . listdir ( {path} )", Python="os.listdir({path})",
                   Params=new[]{P("path","\".\"")} , Tip="List directory contents" },
 
            new(){ Id="file_pathjoin", Cat="FILES", Sub="Paths", Shape=BShape.Reporter,
                   Label="os . path . join ( {a} , {b} )", Python="os.path.join({a}, {b})",
                   Params=new[]{P("a","\"folder\""),P("b","\"file.txt\"")},
                   Tip="Build a file path" },
 
            // -- UI --------------------------------------------------------
            new(){ Id="ui_import",     Cat="UI", Sub="Window", Shape=BShape.Hat,
                   Label="import tkinter as tk", Python="import tkinter as tk",
                   Tip="Import tkinter (built-in GUI)" },
 
            new(){ Id="ui_window",     Cat="UI", Sub="Window", Shape=BShape.Command,
                   Label="{name} = tk . Tk()", Python="{name} = tk.Tk()",
                   Params=new[]{P("name","window")}, Tip="Create main window" },
 
            new(){ Id="ui_title",      Cat="UI", Sub="Window", Shape=BShape.Command,
                   Label="{window} . title ( {title} )", Python="{window}.title({title})",
                   Params=new[]{P("window","window"),P("title","\"My App\"")},
                   Tip="Set window title" },
 
            new(){ Id="ui_mainloop",   Cat="UI", Sub="Window", Shape=BShape.Command,
                   Label="{window} . mainloop()", Python="{window}.mainloop()",
                   Params=new[]{P("window","window")}, Tip="Start event loop" },
 
            new(){ Id="ui_button",     Cat="UI", Sub="Controls", Shape=BShape.Command,
                   Label="{name} = tk . Button ( {parent} , text={text} , command={cmd} )",
                   Python="{name} = tk.Button({parent}, text={text}, command={cmd})",
                   Params=new[]{P("name","btn"),P("parent","window"),P("text","\"Click\""),P("cmd","on_click")},
                   Tip="Create a button" },
 
            new(){ Id="ui_label",      Cat="UI", Sub="Controls", Shape=BShape.Command,
                   Label="{name} = tk . Label ( {parent} , text={text} )",
                   Python="{name} = tk.Label({parent}, text={text})",
                   Params=new[]{P("name","lbl"),P("parent","window"),P("text","\"Hello!\"")},
                   Tip="Create a text label" },
 
            new(){ Id="ui_entry",      Cat="UI", Sub="Controls", Shape=BShape.Command,
                   Label="{name} = tk . Entry ( {parent} )", Python="{name} = tk.Entry({parent})",
                   Params=new[]{P("name","entry"),P("parent","window")}, Tip="Text input field" },
 
            new(){ Id="ui_pack",       Cat="UI", Sub="Layout", Shape=BShape.Command,
                   Label="{widget} . pack()", Python="{widget}.pack()",
                   Params=new[]{P("widget","btn")}, Tip="Add widget to window" },
 
            new(){ Id="ui_grid",       Cat="UI", Sub="Layout", Shape=BShape.Command,
                   Label="{widget} . grid ( row={row} , column={col} )",
                   Python="{widget}.grid(row={row}, column={col})",
                   Params=new[]{P("widget","btn"),P("row","0"),P("col","0")},
                   Tip="Place widget in grid" },
 
            // -- TIME ------------------------------------------------------
            new(){ Id="time_dtimp",    Cat="TIME", Sub="Current", Shape=BShape.Hat,
                   Label="import datetime", Python="import datetime", Tip="Import datetime" },
 
            new(){ Id="time_now",      Cat="TIME", Sub="Current", Shape=BShape.Reporter,
                   Label="datetime . datetime . now()", Python="datetime.datetime.now()",
                   Tip="Current date and time" },
 
            new(){ Id="time_timp",     Cat="TIME", Sub="Sleep", Shape=BShape.Hat,
                   Label="import time", Python="import time", Tip="Import time" },
 
            new(){ Id="time_sleep",    Cat="TIME", Sub="Sleep", Shape=BShape.Command,
                   Label="time . sleep ( {seconds} )", Python="time.sleep({seconds})",
                   Params=new[]{P("seconds","1")}, Tip="Pause for N seconds" },
 
            new(){ Id="time_strftime", Cat="TIME", Sub="Formatting", Shape=BShape.Reporter,
                   Label="{dt} . strftime ( {fmt} )", Python="{dt}.strftime({fmt})",
                   Params=new[]{P("dt","now"),P("fmt","\"%Y-%m-%d\"")},
                   Tip="Format datetime as string" },
 
            // -- SYSTEM ----------------------------------------------------
            new(){ Id="sys_sys",       Cat="SYSTEM", Sub="OS", Shape=BShape.Hat,
                   Label="import sys", Python="import sys", Tip="Import sys module" },
 
            new(){ Id="sys_os",        Cat="SYSTEM", Sub="OS", Shape=BShape.Hat,
                   Label="import os", Python="import os", Tip="Import os module" },
 
            new(){ Id="sys_platform",  Cat="SYSTEM", Sub="OS", Shape=BShape.Reporter,
                   Label="sys . platform", Python="sys.platform",
                   Tip="Current OS platform string" },
 
            new(){ Id="sys_exit",      Cat="SYSTEM", Sub="Process", Shape=BShape.Command,
                   Label="sys . exit ( {code} )", Python="sys.exit({code})",
                   Params=new[]{P("code","0")}, Tip="Exit the program" },
 
            new(){ Id="sys_argv",      Cat="SYSTEM", Sub="Process", Shape=BShape.Reporter,
                   Label="sys . argv", Python="sys.argv", Tip="Command-line arguments list" },
 
            // -- ADVANCED --------------------------------------------------
            new(){ Id="adv_import",    Cat="ADVANCED", Sub="Imports", Shape=BShape.Hat,
                   Label="import {module}", Python="import {module}",
                   Params=new[]{P("module","os")}, Tip="Import a module" },
 
            new(){ Id="adv_from",      Cat="ADVANCED", Sub="Imports", Shape=BShape.Hat,
                   Label="from {module} import {name}", Python="from {module} import {name}",
                   Params=new[]{P("module","os.path"),P("name","join")},
                   Tip="Import specific name" },
 
            new(){ Id="adv_async",     Cat="ADVANCED", Sub="Async", Shape=BShape.Hat, HasBody=true,
                   Label="async def {name} ( {params} ) :", Python="async def {name}({params}):",
                   Params=new[]{P("name","my_async"),P("params","")}, Tip="Async function" },
 
            new(){ Id="adv_await",     Cat="ADVANCED", Sub="Async", Shape=BShape.Reporter,
                   Label="await {expr}", Python="await {expr}",
                   Params=new[]{P("expr","coro()")}, Tip="Await a coroutine" },
 
            new(){ Id="adv_yield",     Cat="ADVANCED", Sub="Generators", Shape=BShape.Command,
                   Label="yield {value}", Python="yield {value}",
                   Params=new[]{P("value","item")}, Tip="Yield from generator" },
 
            new(){ Id="adv_typehint",  Cat="ADVANCED", Sub="Typing", Shape=BShape.Command,
                   Label="{name} : {type} = {value}", Python="{name}: {type} = {value}",
                   Params=new[]{P("name","x"),P("type","int"),P("value","0")},
                   Tip="Variable with type annotation" },
 
            new(){ Id="adv_hasattr",   Cat="ADVANCED", Sub="Reflection", Shape=BShape.Boolean,
                   Label="hasattr ( {obj} , {attr} )", Python="hasattr({obj}, {attr})",
                   Params=new[]{P("obj","obj"),P("attr","\"name\"")},
                   Tip="Object has this attribute?" },
 
            new(){ Id="adv_gc_imp",    Cat="ADVANCED", Sub="Memory", Shape=BShape.Hat,
                   Label="import gc", Python="import gc", Tip="Import garbage collector" },
 
            new(){ Id="adv_gc",        Cat="ADVANCED", Sub="Memory", Shape=BShape.Command,
                   Label="gc . collect()", Python="gc.collect()", Tip="Force garbage collection" },
        };
 
        // -- Query helpers -------------------------------------------------
        public static IEnumerable<string> Categories =>
            All.Select(b => b.Cat).Distinct().OrderBy(Ord);
 
        private static int Ord(string c) => c switch
        {
            "FLOW"=>0,"VARIABLES"=>1,"FUNCTIONS"=>2,"OBJECTS"=>3,"DATA"=>4,"TEXT"=>5,
            "MATH"=>6,"FILES"=>7,"UI"=>8,"TIME"=>9,"SYSTEM"=>10,"ADVANCED"=>11,_=>99
        };
 
        public static IEnumerable<string> SubsOf(string cat) =>
            All.Where(b => b.Cat==cat).Select(b => b.Sub).Distinct();
 
        public static IEnumerable<BDef> BlocksFor(string cat, string sub) =>
            All.Where(b => b.Cat==cat && b.Sub==sub);
 
        // Category brand color (matches Scratch/Snap category palettes)
        public static string ColorOf(string cat) => cat switch
        {
            "FLOW"      => "#E6A020",
            "VARIABLES" => "#FF8C00",
            "FUNCTIONS" => "#8050D0",
            "OBJECTS"   => "#9040C0",
            "DATA"      => "#22A060",
            "TEXT"      => "#CC5010",
            "MATH"      => "#4A8CC8",
            "FILES"     => "#805018",
            "UI"        => "#009FC8",
            "TIME"      => "#008878",
            "SYSTEM"    => "#506878",
            "ADVANCED"  => "#681890",
            _           => "#606060"
        };
 
        public static string IconOf(string cat) => cat switch
        {
            "FLOW"=>"Flow","VARIABLES"=>"Variables","FUNCTIONS"=>"Functions",
            "OBJECTS"=>"Objects","DATA"=>"Data","TEXT"=>"Text",
            "MATH"=>"Math","FILES"=>"Files","UI"=>"UI",
            "TIME"=>"Time","SYSTEM"=>"System","ADVANCED"=>"Advanced",_=>cat
        };
    }
 
    // ------------------------------------------------------------------------
    //  ENTRY POINT
    // ------------------------------------------------------------------------
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new Application();
            app.Run(new MainWindow());
        }
    }
 
    // ------------------------------------------------------------------------
    //  MAIN WINDOW
    // ------------------------------------------------------------------------
    public sealed class MainWindow : Window
    {
        // -- Named UI nodes -----------------------------------------------
        private readonly WrapPanel  _catGrid   = new() { Margin=T(4) };
        private readonly StackPanel _subRow    = new() { Orientation=Orientation.Horizontal, Margin=T(3,2,3,2) };
        private readonly StackPanel _palette   = new() { Margin=T(6,4,6,16) };
        private readonly StackPanel _canvas    = new() { Margin=T(14,14,14,60) };
        private readonly TextBox    _codeBox   = new();
        private readonly TextBox    _searchBox = new();
        private readonly TextBlock  _status    = new() { FontSize=10, VerticalAlignment=VerticalAlignment.Center };
        private readonly TextBlock  _blkCount  = new() { FontSize=10, Foreground=SB("#777777"), VerticalAlignment=VerticalAlignment.Center };
        private readonly TextBlock  _codeInfo  = new() { FontSize=10, Foreground=SB("#555555") };
 
        // -- State --------------------------------------------------------
        private readonly List<BInst> _blocks = new();
        private string _cat = "FLOW";
        private string _sub = "";
        private const string SearchPH = "??  Search blocks…";
 
        // ----------------------------------------------------------------
        public MainWindow()
        {
            Title  = "PyStencyl — Visual Python Editor";
            Width  = 1340; Height = 760;
            MinWidth = 940; MinHeight = 620;
            Background = SB("#CDCDD2");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Icon    = MakeIcon();
            Content = BuildRoot();
            Loaded += (_,_) => { SelectCat("FLOW"); UpdateCode(); };
        }
 
        // ----------------------------------------------------------------
        //  ICON (Python dual-snake, drawn programmatically)
        // ----------------------------------------------------------------
        private static ImageSource MakeIcon()
        {
            var dv = new DrawingVisual();
            using var dc = dv.RenderOpen();
            // Background
            dc.DrawRoundedRectangle(SB("#3A6F9F"), null, new Rect(0,0,64,64), 12, 12);
            // Yellow head
            dc.DrawEllipse(SB("#FFD43B"), null, new Point(20,20), 13,13);
            // Blue head
            dc.DrawEllipse(SB("#4B8BBE"), new Pen(SB("#FFD43B"),2.5), new Point(44,44), 13,13);
            // Body
            var p = new Pen(SB("#FFD43B"),5){ StartLineCap=PenLineCap.Round, EndLineCap=PenLineCap.Round };
            dc.DrawLine(p, new Point(20,20), new Point(44,44));
            // Eyes
            dc.DrawEllipse(SB("#1A1A1A"), null, new Point(24,16), 2.5,2.5);
            dc.DrawEllipse(SB("#1A1A1A"), null, new Point(41,49), 2.5,2.5);
            // Highlight
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90,255,255,255)), null, new Point(16,16), 5,5);
            var bmp = new RenderTargetBitmap(64,64,96,96,PixelFormats.Pbgra32);
            bmp.Render(dv); bmp.Freeze();
            return bmp;
        }
 
        // ----------------------------------------------------------------
        //  ROOT  [toolbar | main area | status bar]
        // ----------------------------------------------------------------
        private UIElement BuildRoot()
        {
            var g = new Grid();
            g.RowDefinitions.Add(RH(46));
            g.RowDefinitions.Add(RStar());
            g.RowDefinitions.Add(RH(24));
            g.Children.Add(At(BuildToolbar(),   0,0));
            g.Children.Add(At(BuildMainArea(),  1,0));
            g.Children.Add(At(BuildStatusBar(), 2,0));
            return g;
        }
 
        // -- Toolbar ------------------------------------------------------
        private UIElement BuildToolbar()
        {
            var dk = new DockPanel { Margin=T(8,0,8,0), LastChildFill=false };
            var brd = new Border { Background=SB("#CDCDD2"),
                BorderBrush=SB("#AAAAAA"), BorderThickness=new Thickness(0,0,0,1), Child=dk };
 
            var logo = Txt("??  PyStencyl", 15, "#2C2C2C", bold:true);
            logo.VerticalAlignment = VerticalAlignment.Center;
            logo.Margin = T(0,0,20,0);
            DK(dk, logo, Dock.Left);
 
            TBtn(dk,"New",       "#607D8B", 60, OnNew);
            TBtn(dk,"Save",      "#607D8B", 60, OnSave);
            TBtn(dk,"Load",      "#607D8B", 60, OnLoad);
            // separator
            var sep = new Border { Width=1, Background=SB("#BBBBBB"), Margin=T(5,8,5,8) };
            DK(dk, sep, Dock.Left);
            TBtn(dk,"?  Run",    "#2E7D32", 76, OnRun);
            TBtn(dk,"Export .py","#3D5A8A", 84, OnExport);
 
            var ver = Txt("PyStencyl v1.0", 10, "#999999");
            ver.VerticalAlignment = VerticalAlignment.Center;
            DK(dk, ver, Dock.Right);
            return brd;
        }
 
        // -- Three-panel main area -----------------------------------------
        private UIElement BuildMainArea()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(CW(312, 190, 500));
            g.ColumnDefinitions.Add(CW(5));
            g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star), MinWidth=200 });
            g.ColumnDefinitions.Add(CW(5));
            g.ColumnDefinitions.Add(CW(286, 180, 500));
            g.Children.Add(At(BuildLeft(),   0,0));
            g.Children.Add(At(Spl(),         0,1));
            g.Children.Add(At(BuildCenter(), 0,2));
            g.Children.Add(At(Spl(),         0,3));
            g.Children.Add(At(BuildRight(),  0,4));
            return g;
        }
 
        // ----------------------------------------------------------------
        //  LEFT PANEL  — Block Storage
        //  Layout: header | 2-column category grid | sub-row | search | palette
        // ----------------------------------------------------------------
        private UIElement BuildLeft()
        {
            var g = new Grid { Background=SB("#CDCDD2") };
            g.RowDefinitions.Add(RH(28));   // panel header
            g.RowDefinitions.Add(RAuto());  // 2-col category grid
            g.RowDefinitions.Add(RH(32));   // sub-category strip
            g.RowDefinitions.Add(RH(28));   // search bar
            g.RowDefinitions.Add(RStar());  // palette scroll
 
            // Panel header
            g.Children.Add(At(PHdr("BLOCK  STORAGE"), 0,0));
 
            // 2-column category grid (WrapPanel auto-wraps to 2 columns)
            BuildCatButtons();
            g.Children.Add(At(new Border { Background=SB("#CDCDD2"), Padding=T(5,4,5,4), Child=_catGrid }, 1,0));
 
            // Sub-category horizontal scroll
            var subScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility  =ScrollBarVisibility.Disabled,
                Content=_subRow
            };
            g.Children.Add(At(new Border { Background=SB("#D4D4D8"), BorderBrush=SB("#AAAAAA"),
                BorderThickness=new Thickness(0,1,0,1), Child=subScroll }, 2,0));
 
            // Search
            _searchBox.Text       = SearchPH;
            _searchBox.Foreground = SB("#AAAAAA");
            _searchBox.FontSize   = 11;
            _searchBox.Height     = 22;
            _searchBox.Padding    = T(6,1,6,1);
            _searchBox.VerticalContentAlignment = VerticalAlignment.Center;
            _searchBox.GotFocus   += (_,_) => { if(_searchBox.Text==SearchPH){_searchBox.Text="";_searchBox.Foreground=SB("#222222");} };
            _searchBox.LostFocus  += (_,_) => { if(string.IsNullOrWhiteSpace(_searchBox.Text)){_searchBox.Text=SearchPH;_searchBox.Foreground=SB("#AAAAAA");} };
            _searchBox.TextChanged += (_,_) => RebuildPalette();
            g.Children.Add(At(new Border { Background=SB("#E8E8E8"), Padding=T(5,3,5,3), Child=_searchBox }, 3,0));
 
            // Palette
            g.Children.Add(At(new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Content=_palette }, 4,0));
            return g;
        }
 
        // ----------------------------------------------------------------
        //  CENTER PANEL  — Block Space (scripting canvas)
        // ----------------------------------------------------------------
        private UIElement BuildCenter()
        {
            var g = new Grid { Background=SB("#D6E4D6") };
            g.RowDefinitions.Add(RH(30));
            g.RowDefinitions.Add(RStar());
 
            // Header
            var hd = new DockPanel { Margin=T(8,0,8,0), LastChildFill=false };
            g.Children.Add(At(new Border { Background=SB("#C2D4C2"),
                BorderBrush=SB("#AAAAAA"), BorderThickness=new Thickness(0,0,0,1), Child=hd }, 0,0));
            DK(hd, Txt("BLOCK  SPACE", 11, "#333333", bold:true), Dock.Left);
            _blkCount.Margin = T(12,0,0,0);
            DK(hd, _blkCount, Dock.Left);
            DK(hd, MkBtn("?  Clear All","#AA4040",60+24, OnClearAll), Dock.Right);
 
            // Canvas with dot-pattern background
            var canvasBg = new Border { Background=DotPattern() };
            var sv = new ScrollViewer
            {
                VerticalScrollBarVisibility  =ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
                AllowDrop=true,
                Content=new Grid { Children={ { canvasBg }, { _canvas } } }
            };
            // Fix: we need the dot background to stretch
            _canvas.Background = Brushes.Transparent;
            sv.Drop     += (_,e) => { if(e.Data.GetData(typeof(BDef)) is BDef d) PushBlock(d); };
            sv.DragOver += (_,e) => { e.Effects = e.Data.GetDataPresent(typeof(BDef)) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled=true; };
            g.Children.Add(At(sv, 1,0));
            return g;
        }
 
        // ----------------------------------------------------------------
        //  RIGHT PANEL  — Translated Python Code
        // ----------------------------------------------------------------
        private UIElement BuildRight()
        {
            var g = new Grid { Background=SB("#CDCDD2") };
            g.RowDefinitions.Add(RH(30));
            g.RowDefinitions.Add(RStar());
            g.RowDefinitions.Add(RH(22));
 
            var hd = new DockPanel { Margin=T(8,0,8,0), LastChildFill=false };
            g.Children.Add(At(new Border { Background=SB("#B0B0B6"),
                BorderBrush=SB("#AAAAAA"), BorderThickness=new Thickness(0,0,0,1), Child=hd }, 0,0));
            DK(hd, Txt("??  PYTHON CODE", 11, "#EEEEEE", bold:true), Dock.Left);
            DK(hd, MkBtn("Copy","#3D5A8A",50, OnCopyCode), Dock.Right);
 
            // Code box — dark theme
            _codeBox.FontFamily   = new FontFamily("Consolas, Courier New");
            _codeBox.FontSize     = 12;
            _codeBox.Background   = SB("#1E1E2E");
            _codeBox.Foreground   = SB("#CDD6F4");
            _codeBox.CaretBrush   = SB("#CDD6F4");
            _codeBox.IsReadOnly   = true;
            _codeBox.AcceptsReturn= true;
            _codeBox.TextWrapping = TextWrapping.NoWrap;
            _codeBox.VerticalScrollBarVisibility   = ScrollBarVisibility.Auto;
            _codeBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _codeBox.Padding         = T(10,8,10,8);
            _codeBox.BorderThickness = new Thickness(0);
            g.Children.Add(At(_codeBox, 1,0));
 
            _codeInfo.Foreground = SB("#555555");
            g.Children.Add(At(new Border { Background=SB("#B0B0B6"), Padding=T(8,2,8,2),
                BorderBrush=SB("#AAAAAA"), BorderThickness=new Thickness(0,1,0,0),
                Child=_codeInfo }, 2,0));
            return g;
        }
 
        // -- Status bar ---------------------------------------------------
        private UIElement BuildStatusBar()
        {
            _status.Foreground = SB("#555555");
            var dk = new DockPanel { Margin=T(8,0,8,0), LastChildFill=false };
            DK(dk, _status, Dock.Left);
            SetStatus("Ready  ·  Click any block in the palette to add it to the script canvas");
            return new Border { Background=SB("#B0B0B6"), BorderBrush=SB("#AAAAAA"),
                BorderThickness=new Thickness(0,1,0,0), Child=dk };
        }
 
        // ----------------------------------------------------------------
        //  CATEGORY GRID  (2-column layout, colored toggle buttons)
        // ----------------------------------------------------------------
        private void BuildCatButtons()
        {
            _catGrid.Children.Clear();
            double btnW = 138;  // each button ~138px so 2 fit in a ~290px panel
 
            foreach (var cat in Lib.Categories)
            {
                var c   = cat;
                var col = Lib.ColorOf(c);
 
                // Each category button is a rounded colored rectangle
                var btn = new Border
                {
                    Width        = btnW,
                    Height       = 24,
                    Background   = SB(col),
                    CornerRadius = new CornerRadius(4),
                    Margin       = T(2),
                    Cursor       = Cursors.Hand,
                    ToolTip      = c,
                    Tag          = c
                };
 
                var inner = new Grid();
                // Colored "preview" strip on left (darker)
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(8) });
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
                var preview = new Border { Background=SB(Dk(col)), CornerRadius=new CornerRadius(4,0,0,4) };
                inner.Children.Add(At(preview, 0,0));
                var lbl = Txt(Lib.IconOf(c), 10, "#FFFFFF", bold:true);
                lbl.Margin=T(5,0,4,0);
                lbl.VerticalAlignment = VerticalAlignment.Center;
                inner.Children.Add(At(lbl, 0,1));
                btn.Child = inner;
 
                btn.MouseLeftButtonDown += (_,e) => { SelectCat(c); e.Handled=true; };
                Hover(btn, 0.75);
                _catGrid.Children.Add(btn);
            }
        }
 
        private void SelectCat(string cat)
        {
            _cat = cat;
            foreach (Border b in _catGrid.Children.OfType<Border>())
            {
                bool sel = b.Tag?.ToString()==cat;
                b.BorderBrush     = sel ? SB("#FFFFFF") : null;
                b.BorderThickness = sel ? new Thickness(0,0,0,3) : new Thickness(0);
            }
            BuildSubButtons(cat);
            var subs = Lib.SubsOf(cat).ToList();
            if (subs.Any()) SelectSub(subs[0]);
        }
 
        private void BuildSubButtons(string cat)
        {
            _subRow.Children.Clear();
            foreach (var sub in Lib.SubsOf(cat))
            {
                var s = sub;
                var b = new Border
                {
                    Background   = SB("#5C5C5C"),
                    CornerRadius = new CornerRadius(3),
                    Margin       = T(2,1,2,1),
                    Padding      = T(8,2,8,2),
                    Cursor       = Cursors.Hand,
                    Tag          = s
                };
                b.Child = Txt(s, 10, "#FFFFFF");
                b.MouseLeftButtonDown += (_,e) => { SelectSub(s); e.Handled=true; };
                Hover(b, 0.75);
                _subRow.Children.Add(b);
            }
        }
 
        private void SelectSub(string sub)
        {
            _sub = sub;
            foreach (Border b in _subRow.Children.OfType<Border>())
            {
                bool sel = b.Tag?.ToString()==sub;
                b.Background = sel ? SB(Lib.ColorOf(_cat)) : SB("#5C5C5C");
                if (b.Child is TextBlock tb) tb.FontWeight = sel ? FontWeights.Bold : FontWeights.Normal;
            }
            RebuildPalette();
        }
 
        // ----------------------------------------------------------------
        //  PALETTE  — jigsaw block shapes (read-only, draggable)
        // ----------------------------------------------------------------
        private void RebuildPalette()
        {
            _palette.Children.Clear();
            var q = _searchBox.Text == SearchPH ? "" : _searchBox.Text.Trim().ToLower();
 
            var list = Lib.BlocksFor(_cat, _sub)
                .Where(b => q.Length==0 || b.Label.ToLower().Contains(q) || b.Tip.ToLower().Contains(q))
                .ToList();
 
            if (!list.Any()) { _palette.Children.Add(Txt("No blocks match.", 10, "#888888")); return; }
 
            var hdr = Txt($"?  {_sub}", 11, "#444444", bold:true);
            hdr.Margin = T(2,5,2,6);
            _palette.Children.Add(hdr);
 
            foreach (var def in list)
                _palette.Children.Add(MakePaletteBlock(def));
        }
 
        /// <summary>
        /// Build a palette block — a real jigsaw-shaped Path + text label overlay.
        /// Uses Canvas so the Path size can be exactly the block shape size.
        /// </summary>
        private UIElement MakePaletteBlock(BDef def)
        {
            const double PW = 276;  // palette block width
            string col     = Lib.ColorOf(def.Cat);
            double bodyH   = def.Shape == BShape.Hat ? BS.HatH : BS.BlockH;
 
            // Build geometry
            Geometry geom = def.Shape switch
            {
                BShape.Hat      => BlockGeom.Hat(PW),
                BShape.Reporter => BlockGeom.Reporter(PW, 24),
                BShape.Boolean  => BlockGeom.Bool(PW, 24),
                BShape.Control  => BlockGeom.Command(PW, bodyH, topNotch:true, bottomBump:true),
                _               => BlockGeom.Command(PW, bodyH, topNotch:true, bottomBump:true)
            };
 
            double totalH = def.Shape == BShape.Hat
                ? BS.HatH + BS.BD + BS.HatArch
                : bodyH + BS.BD + BS.ND;
 
            // Outer canvas sized to full block visual (including bump + notch offset)
            var cvs = new Canvas
            {
                Width         = PW,
                Height        = totalH,
                Margin        = T(0,0,0,3),
                Cursor        = Cursors.Hand,
                ToolTip       = $"{def.Tip}\n\nPython ? {def.Python}"
            };
 
            // -- shape path ------------------------------------------------
            var path = new Path
            {
                Data            = geom,
                Fill            = BuildBlockBrush(col, bodyH),
                Stroke          = SB(Dk(col)),
                StrokeThickness = 1
            };
            // Offset so the notch indent is above y=0, but in palette we start at ND offset
            double yOff = def.Shape == BShape.Hat ? BS.HatArch : BS.ND;
            Canvas.SetTop(path, 0);
            cvs.Children.Add(path);
 
            // -- label text ------------------------------------------------
            var lbl = new TextBlock
            {
                Text              = SimplifyLabel(def.Label),
                FontFamily        = new FontFamily("Verdana"),
                FontSize          = 10,
                Foreground        = Brushes.White,
                TextWrapping      = TextWrapping.Wrap,
                MaxWidth          = PW - 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas.SetLeft(lbl, 8);
            Canvas.SetTop(lbl, yOff + 7);
            cvs.Children.Add(lbl);
 
            // Interaction
            cvs.MouseLeftButtonDown += (_,e) => { PushBlock(def); e.Handled=true; };
            cvs.MouseMove += (_,e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                    DragDrop.DoDragDrop(cvs, def, DragDropEffects.Copy);
            };
            // Hover: subtle opacity pulse
            cvs.MouseEnter += (_,_) => cvs.Opacity = 0.78;
            cvs.MouseLeave += (_,_) => cvs.Opacity = 1.0;
 
            return cvs;
        }
 
        // ----------------------------------------------------------------
        //  CANVAS BLOCKS  — interactive jigsaw blocks with text inputs
        // ----------------------------------------------------------------
        private void PushBlock(BDef def)
        {
            var inst = new BInst(def);
            if (_blocks.Count > 0)
            {
                var prev = _blocks[^1];
                inst.Indent = prev.Def.HasBody ? prev.Indent+1 : prev.Indent;
            }
            _blocks.Add(inst);
            RebuildCanvas();
            UpdateCode();
            SetStatus($"Added:  {SimplifyLabel(def.Label)}");
        }
 
        private void RebuildCanvas()
        {
            _canvas.Children.Clear();
            if (_blocks.Count == 0)
            {
                var hint = Txt("? Click palette blocks to build your script here\n\n" +
                    "  ? ?  move     ? ?  indent/outdent     ×  delete", 12, "#7AAA7A");
                hint.TextAlignment = TextAlignment.Center;
                hint.Margin = T(20,60,20,0);
                _canvas.Children.Add(hint);
                _blkCount.Text = "empty";
                return;
            }
            for (int i = 0; i < _blocks.Count; i++)
                _canvas.Children.Add(MakeCanvasBlock(_blocks[i], i));
 
            _blkCount.Text = $"{_blocks.Count} block{(_blocks.Count!=1?"s":"")}";
        }
 
        private UIElement MakeCanvasBlock(BInst inst, int idx)
        {
            string col   = Lib.ColorOf(inst.Def.Cat);
            double bodyH = inst.Def.Shape == BShape.Hat ? BS.HatH : BS.BlockH;
            double yOff  = inst.Def.Shape == BShape.Hat ? BS.HatArch : BS.ND;
 
            // Width: compute based on content (simplified: auto + min)
            double bw = 320;
 
            Geometry geom = inst.Def.Shape switch
            {
                BShape.Hat      => BlockGeom.Hat(bw),
                BShape.Reporter => BlockGeom.Reporter(bw, bodyH),
                BShape.Boolean  => BlockGeom.Bool(bw, bodyH),
                BShape.Control  => BlockGeom.Command(bw, bodyH, topNotch:true, bottomBump:true),
                _               => BlockGeom.Command(bw, bodyH, topNotch:true, bottomBump:true)
            };
 
            double totalH = inst.Def.Shape == BShape.Hat
                ? BS.HatH + BS.BD + BS.HatArch
                : bodyH + BS.BD + BS.ND;
 
            // Indent wrapper
            var outer = new Canvas
            {
                Width  = bw + inst.Indent * 24,
                Height = totalH,
                Margin = T(0, 0, 0, -BS.ND + 1)  // overlap by notch so blocks visually connect
            };
 
            // -- block path ---------------------------------------------
            var path = new Path
            {
                Data            = geom,
                Fill            = BuildBlockBrush(col, bodyH),
                Stroke          = SB(Dk(col)),
                StrokeThickness = 1
            };
            Canvas.SetLeft(path, inst.Indent * 24);
            Canvas.SetTop(path, 0);
            outer.Children.Add(path);
 
            // -- block content: label parts + TextBox inputs -------------
            var content = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            BuildBlockContent(inst, content);
            Canvas.SetLeft(content, inst.Indent * 24 + 10);
            Canvas.SetTop(content, yOff + 5);
            outer.Children.Add(content);
 
            // -- control buttons (right side of block) -------------------
            var btns = new StackPanel { Orientation=Orientation.Horizontal };
            btns.Children.Add(CBt("?","#4A4A5C", ()=>{ if(idx>0){ var b=_blocks[idx]; _blocks.RemoveAt(idx); _blocks.Insert(idx-1,b); RebuildCanvas(); UpdateCode(); }}, "Move up"));
            btns.Children.Add(CBt("?","#4A4A5C", ()=>{ if(idx<_blocks.Count-1){ var b=_blocks[idx]; _blocks.RemoveAt(idx); _blocks.Insert(idx+1,b); RebuildCanvas(); UpdateCode(); }}, "Move down"));
            btns.Children.Add(CBt("?","#2E5A7A", ()=>{ inst.Indent=Math.Min(inst.Indent+1,12); RebuildCanvas(); UpdateCode(); }, "Indent"));
            btns.Children.Add(CBt("?","#2E5A7A", ()=>{ inst.Indent=Math.Max(inst.Indent-1,0);  RebuildCanvas(); UpdateCode(); }, "Outdent"));
            btns.Children.Add(CBt("×","#A83030", ()=>{ _blocks.RemoveAt(idx); RebuildCanvas(); UpdateCode(); }, "Delete block"));
 
            Canvas.SetLeft(btns, inst.Indent*24 + bw + 4);
            Canvas.SetTop(btns, yOff + 5);
            outer.Children.Add(btns);
 
            // -- hover glow ---------------------------------------------
            outer.MouseEnter += (_,_) => path.Stroke = SB("#FFFFFF");
            outer.MouseLeave += (_,_) => path.Stroke = SB(Dk(col));
            outer.ToolTip = inst.Def.Tip;
            return outer;
        }
 
        private void BuildBlockContent(BInst inst, WrapPanel panel)
        {
            foreach (var part in SplitLabel(inst.Def.Label))
            {
                if (part.StartsWith("{") && part.EndsWith("}"))
                {
                    var name = part[1..^1];
                    if (!name.All(c => char.IsLetterOrDigit(c)||c=='_') || name.Length==0)
                    { panel.Children.Add(BLbl(part)); continue; }
 
                    var cur = inst.Vals.TryGetValue(name, out var v) ? v : "";
                    var box = new TextBox
                    {
                        Text                     = cur,
                        Width                    = Math.Max(48, Math.Min(140, cur.Length*8+28)),
                        MinWidth                 = 36,
                        MaxWidth                 = 160,
                        Height                   = 20,
                        FontFamily               = new FontFamily("Consolas"),
                        FontSize                 = 10,
                        Background               = new SolidColorBrush(Color.FromArgb(200,255,255,255)),
                        BorderBrush              = SB("#CCCCCC"),
                        BorderThickness          = new Thickness(1),
                        Padding                  = T(3,1,3,1),
                        Margin                   = T(2,0,2,0),
                        VerticalContentAlignment = VerticalAlignment.Center
                    };
                    var n = name;
                    box.TextChanged += (_,_) =>
                    {
                        inst.Vals[n] = box.Text;
                        box.Width = Math.Max(48, Math.Min(140, box.Text.Length*8+28));
                        UpdateCode();
                    };
                    panel.Children.Add(box);
                }
                else if (!string.IsNullOrEmpty(part))
                {
                    panel.Children.Add(BLbl(part));
                }
            }
 
            if (inst.Def.HasBody)
            {
                var tag = new Border
                {
                    Background=new SolidColorBrush(Color.FromArgb(55,0,0,0)),
                    CornerRadius=new CornerRadius(3),
                    Padding=T(4,1,4,1), Margin=T(5,0,0,0)
                };
                tag.Child = Txt("body ?", 8, "#FFFFFF", italic:true);
                panel.Children.Add(tag);
            }
        }
 
        // -- Split label "if {cond} :" into ["if ","{cond}"," :"] --------
        private static List<string> SplitLabel(string label)
        {
            var parts = new List<string>();
            int pos = 0;
            while (pos < label.Length)
            {
                int open = label.IndexOf('{', pos);
                if (open < 0) { if (pos < label.Length) parts.Add(label[pos..]); break; }
                if (open > pos) parts.Add(label[pos..open]);
                int close = label.IndexOf('}', open+1);
                if (close < 0) { parts.Add(label[open..]); break; }
                parts.Add(label[open..(close+1)]);
                pos = close+1;
            }
            return parts.Where(p => p.Length>0).ToList();
        }
 
        private static string SimplifyLabel(string label) =>
            string.Join("", SplitLabel(label).Select(p =>
                (p.StartsWith("{") && p.EndsWith("}")) ? $"[ {p[1..^1]} ]" : p));
 
        // ----------------------------------------------------------------
        //  PYTHON CODE GENERATION
        // ----------------------------------------------------------------
        private void UpdateCode()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# -----------------------------------------------");
            sb.AppendLine("# Generated by PyStencyl  ·  python.org");
            sb.AppendLine("# -----------------------------------------------");
            sb.AppendLine();
 
            if (!_blocks.Any())
                sb.AppendLine("# No blocks yet — add some from the left panel!");
            else
                foreach (var b in _blocks)
                    sb.AppendLine(new string(' ', b.Indent*4) + b.ToPython());
 
            _codeBox.Text = sb.ToString();
            int lines = _codeBox.Text.Split('\n').Length;
            _codeInfo.Text = $"{_blocks.Count} block{(_blocks.Count!=1?"s":"")}  ?  {lines} line{(lines!=1?"s":"")}";
        }
 
        // ----------------------------------------------------------------
        //  ACTIONS
        // ----------------------------------------------------------------
        private void OnNew()
        {
            if (_blocks.Count>0 && MessageBox.Show(
                "Create new project?\nAll current blocks will be lost.",
                "New", MessageBoxButton.YesNo,MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _blocks.Clear(); RebuildCanvas(); UpdateCode();
            SetStatus("New project");
        }
 
        private void OnSave()
        {
            var dlg = new SaveFileDialog { Filter="PyStencyl (*.pys)|*.pys|Python (*.py)|*.py",
                DefaultExt=".pys", FileName="my_project" };
            if (dlg.ShowDialog()!=true) return;
            if (dlg.FilterIndex==2)
                File.WriteAllText(dlg.FileName, _codeBox.Text);
            else
                File.WriteAllLines(dlg.FileName, _blocks.Select(b =>
                    $"{b.Indent}|{b.Def.Id}|{string.Join(";",b.Vals.Select(kv=>$"{kv.Key}={kv.Value}"))}"));
            SetStatus($"Saved ? {dlg.FileName}");
        }
 
        private void OnLoad()
        {
            var dlg = new OpenFileDialog { Filter="PyStencyl (*.pys)|*.pys|All|*.*", DefaultExt=".pys" };
            if (dlg.ShowDialog()!=true) return;
            try
            {
                _blocks.Clear();
                foreach (var raw in File.ReadAllLines(dlg.FileName))
                {
                    var p = raw.Split('|');
                    if (p.Length<2) continue;
                    int ind = int.TryParse(p[0],out var x)?x:0;
                    var def = Lib.All.FirstOrDefault(b=>b.Id==p[1]);
                    if (def==null) continue;
                    var inst = new BInst(def){Indent=ind};
                    if (p.Length>2 && !string.IsNullOrWhiteSpace(p[2]))
                        foreach (var pair in p[2].Split(';'))
                        {
                            var kv=pair.Split('=',2);
                            if (kv.Length==2) inst.Vals[kv[0]]=kv[1];
                        }
                    _blocks.Add(inst);
                }
                RebuildCanvas(); UpdateCode();
                SetStatus($"Loaded ? {dlg.FileName}");
            }
            catch (Exception ex) { MessageBox.Show($"Load failed:\n{ex.Message}","Error",MessageBoxButton.OK,MessageBoxImage.Error); }
        }
 
        private void OnRun()
        {
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pystencyl_run.py");
            File.WriteAllText(tmp, _codeBox.Text);
            foreach (var py in new[]{"python","python3"})
            {
                try { Process.Start(new ProcessStartInfo(py,$"\"{tmp}\""){UseShellExecute=true}); SetStatus($"Running via {py}…"); return; }
                catch { }
            }
            MessageBox.Show("Python not found.\n\nInstall Python and make sure it is in your PATH.",
                "Python Missing",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
 
        private void OnExport()
        {
            var dlg = new SaveFileDialog{Filter="Python (*.py)|*.py",DefaultExt=".py",FileName="my_script"};
            if (dlg.ShowDialog()==true) { File.WriteAllText(dlg.FileName,_codeBox.Text); SetStatus($"Exported ? {dlg.FileName}"); }
        }
 
        private void OnClearAll()
        {
            if (_blocks.Count==0) return;
            if (MessageBox.Show("Clear all blocks?","Confirm",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return;
            _blocks.Clear(); RebuildCanvas(); UpdateCode(); SetStatus("Canvas cleared");
        }
 
        private void OnCopyCode()
        {
            Clipboard.SetText(_codeBox.Text);
            SetStatus("Python code copied to clipboard ?");
        }
 
        private void SetStatus(string msg) => _status.Text = msg;
 
        // ----------------------------------------------------------------
        //  VISUAL HELPERS
        // ----------------------------------------------------------------
 
        // Block gradient brush (lighter top, slightly darker bottom — like Snap!)
        private static Brush BuildBlockBrush(string hex, double h)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            byte lr = (byte)Math.Min(255, c.R+30);
            byte lg = (byte)Math.Min(255, c.G+30);
            byte lb = (byte)Math.Min(255, c.B+30);
            var light = Color.FromRgb(lr,lg,lb);
            var gb = new LinearGradientBrush
            {
                StartPoint = new Point(0,0),
                EndPoint   = new Point(0,1)
            };
            gb.GradientStops.Add(new GradientStop(light, 0));
            gb.GradientStops.Add(new GradientStop(c, 0.5));
            gb.GradientStops.Add(new GradientStop(c, 1));
            gb.Freeze();
            return gb;
        }
 
        // Canvas dot-pattern background (like Scratch/Stencyl scripting area)
        private static Brush DotPattern()
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(SB("#DDEEDE"), null, new Rect(0,0,20,20));
                dc.DrawEllipse(SB("#C0D8C0"), null, new Point(10,10), 1.2,1.2);
            }
            var bmp = new RenderTargetBitmap(20,20,96,96,PixelFormats.Pbgra32);
            bmp.Render(dv); bmp.Freeze();
            return new ImageBrush(bmp) { TileMode=TileMode.Tile, Stretch=Stretch.None,
                Viewport=new Rect(0,0,20,20), ViewportUnits=BrushMappingMode.Absolute };
        }
 
        // -- Widget factories ---------------------------------------------
 
        private static void TBtn(DockPanel d, string lbl, string col, double w, Action act)
        {
            DK(d, MkBtn(lbl,col,w,act), Dock.Left);
        }
 
        private static Border MkBtn(string lbl, string col, double w, Action act)
        {
            var b = new Border { Background=SB(col), CornerRadius=new CornerRadius(3),
                Width=w, Height=28, Margin=T(2,0,2,0), Cursor=Cursors.Hand,
                VerticalAlignment=VerticalAlignment.Center };
            var t = Txt(lbl,11,"#FFFFFF",bold:true);
            t.HorizontalAlignment=HorizontalAlignment.Center;
            t.VerticalAlignment=VerticalAlignment.Center;
            b.Child=t;
            b.MouseLeftButtonDown+=(_,_)=>act();
            Hover(b,0.75);
            return b;
        }
 
        private static Border CBt(string lbl, string col, Action act, string tip)
        {
            var b = new Border { Background=SB(col), CornerRadius=new CornerRadius(3),
                Width=24, Height=22, Margin=T(1,0,1,0), Cursor=Cursors.Hand, ToolTip=tip };
            var t = Txt(lbl,11,"#FFFFFF"); t.HorizontalAlignment=HorizontalAlignment.Center;
            t.VerticalAlignment=VerticalAlignment.Center;
            b.Child=t;
            b.MouseLeftButtonDown+=(_,_)=>act();
            Hover(b,0.75);
            return b;
        }
 
        private static TextBlock BLbl(string t) => new()
        {
            Text=t, FontFamily=new FontFamily("Verdana"), FontSize=10, FontWeight=FontWeights.Bold,
            Foreground=Brushes.White, VerticalAlignment=VerticalAlignment.Center, Margin=T(1,0,1,0)
        };
 
        private static Border PHdr(string txt) =>
            new() { Background=SB("#B0B0B6"), Padding=T(8,4,8,4),
                    Child=Txt(txt,11,"#333333",bold:true) };
 
        private static TextBlock Txt(string t, double sz, string col, bool bold=false, bool italic=false)
        {
            var tb = new TextBlock { Text=t, FontSize=sz, Foreground=SB(col),
                TextWrapping=TextWrapping.Wrap, VerticalAlignment=VerticalAlignment.Center };
            if(bold)   tb.FontWeight=FontWeights.Bold;
            if(italic) tb.FontStyle=FontStyles.Italic;
            return tb;
        }
 
        private static GridSplitter Spl() =>
            new() { Width=5, HorizontalAlignment=HorizontalAlignment.Stretch, Background=SB("#AAAAAA"), Cursor=Cursors.SizeWE };
 
        private static void Hover(Border b, double dim)
        {
            b.MouseEnter+=(_,_)=>b.Opacity=dim;
            b.MouseLeave+=(_,_)=>b.Opacity=1.0;
        }
 
        private static void DK(DockPanel d, UIElement e, Dock dock) { DockPanel.SetDock(e,dock); d.Children.Add(e); }
 
        // Grid helpers
        private static RowDefinition RH(double h)  => new(){ Height=new GridLength(h) };
        private static RowDefinition RStar()         => new(){ Height=new GridLength(1,GridUnitType.Star) };
        private static RowDefinition RAuto()         => new(){ Height=GridLength.Auto };
        private static ColumnDefinition CW(double w, double min=0, double max=double.PositiveInfinity) =>
            new(){ Width=new GridLength(w), MinWidth=min, MaxWidth=max };
        private static T At<T>(T el, int row, int col) where T:UIElement
        { Grid.SetRow(el,row); Grid.SetColumn(el,col); return el; }
        private static Thickness T(double a)                           => new(a);
        private static Thickness T(double l,double t,double r,double b)=> new(l,t,r,b);
 
        // Color helpers
        private static SolidColorBrush SB(string hex)
        {
            try { var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
            catch { return new SolidColorBrush(Colors.Gray); }
        }
 
        // Darken a hex color by ~35 %
        private static string Dk(string hex)
        {
            try { var c=(Color)ColorConverter.ConvertFromString(hex);
                return $"#{(byte)(c.R*0.65):X2}{(byte)(c.G*0.65):X2}{(byte)(c.B*0.65):X2}"; }
            catch { return hex; }
        }
    }
}
 