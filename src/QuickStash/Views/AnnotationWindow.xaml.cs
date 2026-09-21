using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QuickStash.Views;

/// <summary>
/// Draw on a screenshot: pen, highlighter, arrow, eraser, colors, sizes, undo/redo.
/// This window is inherently view-level (it drives an InkCanvas); loading and saving go through the DrawingService,
/// passed in as <see cref="SaveRequested"/> / <see cref="RevertRequested"/>.
/// </summary>
public partial class AnnotationWindow : Window
{
    private enum Tool { Pen, Highlighter, Arrow, Eraser }

    private static readonly (string Name, Color Color)[] Palette =
    {
        ("Red", Color.FromRgb(0xFF, 0x3B, 0x30)),
        ("Yellow", Color.FromRgb(0xFF, 0xD6, 0x0A)),
        ("Green", Color.FromRgb(0x34, 0xC7, 0x59)),
        ("Blue", Color.FromRgb(0x1A, 0x9F, 0xFF)),
        ("White", Colors.White),
        ("Black", Color.FromRgb(0x11, 0x11, 0x11)),
    };

    private sealed record InkAction(StrokeCollection Added, StrokeCollection Removed);

    private readonly BitmapSource _image;
    private readonly Stack<InkAction> _undo = new();
    private readonly Stack<InkAction> _redo = new();
    private readonly DispatcherTimer _escapeWindow = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _tracking = true;
    private bool _dirty;
    private Tool _tool = Tool.Pen;
    private Color _color = Palette[0].Color;
    private double _sizeFactor = 1.0;
    private Point _arrowStart;
    private Stroke? _arrowPreview;

    public AnnotationWindow(BitmapSource image, StrokeCollection strokes, string subtitle, bool hasDrawing)
    {
        InitializeComponent();
        _image = image;
        Picture.Source = image;
        ImageHost.Width = image.PixelWidth;
        ImageHost.Height = image.PixelHeight;
        Subtitle.Text = subtitle;
        RevertButton.Visibility = hasDrawing ? Visibility.Visible : Visibility.Collapsed;

        Ink.Strokes = strokes;
        Ink.Strokes.StrokesChanged += OnStrokesChanged;

        BuildSwatches();
        ApplyTool();
        UpdateUndoButtons();

        PreviewKeyDown += OnWindowKeyDown;
        StateChanged += (_, _) =>
        {
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
        };
        _escapeWindow.Tick += (_, _) =>
        {
            _escapeWindow.Stop();
            Status.Text = string.Empty;
        };
        SizeToWorkArea();
    }

    /// <summary>Raised with the image and the final strokes when the user saves.</summary>
    public event EventHandler<StrokeCollection>? SaveRequested;

    /// <summary>Raised when the user wants the untouched screenshot back.</summary>
    public event EventHandler? RevertRequested;

    public BitmapSource Image => _image;

    private void SizeToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, area.Width * 0.85);
        Height = Math.Max(MinHeight, area.Height * 0.88);
    }

    // ───────── Tools ─────────

    /// <summary>Stroke width in image pixels, scaled to the screenshot so a "medium" line looks the same on 1080p and 4K.</summary>
    private double Thickness => Math.Max(3.0, _image.PixelWidth / 240.0) * _sizeFactor;

    private void BuildSwatches()
    {
        for (int i = 0; i < Palette.Length; i++)
        {
            var (name, color) = Palette[i];
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                GroupName = "Color",
                Background = new SolidColorBrush(color),
                ToolTip = $"{name} ({i + 1})",
                IsChecked = i == 0,
                Tag = color,
            };
            swatch.Checked += (s, _) =>
            {
                _color = (Color)((RadioButton)s!).Tag;
                ApplyTool();
            };
            Swatches.Children.Add(swatch);
        }
    }

    private void OnToolChanged(object sender, RoutedEventArgs e)
    {
        _tool = sender == HighlighterTool ? Tool.Highlighter
              : sender == ArrowTool ? Tool.Arrow
              : sender == EraserTool ? Tool.Eraser
              : Tool.Pen;
        ApplyTool();
    }

    private void OnSizeChanged(object sender, RoutedEventArgs e)
    {
        _sizeFactor = sender == SizeSmall ? 0.55 : sender == SizeLarge ? 2.0 : 1.0;
        ApplyTool();
    }

    private void ApplyTool()
    {
        if (Ink is null) return; // during InitializeComponent
        switch (_tool)
        {
            case Tool.Pen:
                Ink.EditingMode = InkCanvasEditingMode.Ink;
                Ink.DefaultDrawingAttributes = PenAttributes();
                Ink.Cursor = Cursors.Pen;
                break;
            case Tool.Highlighter:
                Ink.EditingMode = InkCanvasEditingMode.Ink;
                Ink.DefaultDrawingAttributes = new DrawingAttributes
                {
                    Color = _color,
                    Width = Thickness * 4,
                    Height = Thickness * 4,
                    IsHighlighter = true,
                    StylusTip = StylusTip.Ellipse,
                    FitToCurve = true,
                    IgnorePressure = true,
                };
                Ink.Cursor = Cursors.Pen;
                break;
            case Tool.Arrow:
                Ink.EditingMode = InkCanvasEditingMode.None; // drawn by hand in the mouse handlers
                Ink.Cursor = Cursors.Cross;
                break;
            case Tool.Eraser:
                Ink.EditingMode = InkCanvasEditingMode.EraseByStroke;
                break;
        }
    }

    private DrawingAttributes PenAttributes() => new()
    {
        Color = _color,
        Width = Thickness,
        Height = Thickness,
        StylusTip = StylusTip.Ellipse,
        FitToCurve = true,
        IgnorePressure = true,
    };

    // ───────── Arrow tool ─────────
    // One stroke that goes start → tip → one barb → tip → other barb, so it undoes/erases as a single item.

    private void OnInkMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool != Tool.Arrow) return;
        _arrowStart = e.GetPosition(Ink);
        Ink.CaptureMouse();
        e.Handled = true;
    }

    private void OnInkMouseMove(object sender, MouseEventArgs e)
    {
        if (_tool != Tool.Arrow || !Ink.IsMouseCaptured) return;
        _tracking = false;
        if (_arrowPreview is not null) Ink.Strokes.Remove(_arrowPreview);
        _arrowPreview = MakeArrow(_arrowStart, e.GetPosition(Ink));
        if (_arrowPreview is not null) Ink.Strokes.Add(_arrowPreview);
        _tracking = true;
    }

    private void OnInkMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_tool != Tool.Arrow || !Ink.IsMouseCaptured) return;
        Ink.ReleaseMouseCapture();
        _tracking = false;
        if (_arrowPreview is not null) Ink.Strokes.Remove(_arrowPreview);
        _arrowPreview = null;
        _tracking = true;

        var arrow = MakeArrow(_arrowStart, e.GetPosition(Ink));
        if (arrow is not null) Ink.Strokes.Add(arrow); // tracked → one undo step
        e.Handled = true;
    }

    private Stroke? MakeArrow(Point start, Point tip)
    {
        Vector shaft = tip - start;
        if (shaft.Length < 4) return null;
        double headLength = Math.Min(shaft.Length * 0.45, Thickness * 4.5 + 10);
        shaft.Normalize();
        Vector back = -shaft * headLength;
        Point left = tip + Rotate(back, 28);
        Point right = tip + Rotate(back, -28);

        var points = new StylusPointCollection(new[] { start, tip, left, tip, right }.Select(p => new StylusPoint(p.X, p.Y)));
        var attributes = PenAttributes();
        attributes.FitToCurve = false;
        return new Stroke(points, attributes);
    }

    private static Vector Rotate(Vector v, double degrees)
    {
        double r = degrees * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        return new Vector(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    // ───────── Undo / redo ─────────

    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (!_tracking) return;
        _undo.Push(new InkAction(new StrokeCollection(e.Added), new StrokeCollection(e.Removed)));
        _redo.Clear();
        _dirty = true;
        UpdateUndoButtons();
    }

    private void Undo()
    {
        if (!_undo.TryPop(out var action)) return;
        Apply(action, reverse: true);
        _redo.Push(action);
    }

    private void Redo()
    {
        if (!_redo.TryPop(out var action)) return;
        Apply(action, reverse: false);
        _undo.Push(action);
    }

    private void Apply(InkAction action, bool reverse)
    {
        _tracking = false;
        var remove = reverse ? action.Added : action.Removed;
        var add = reverse ? action.Removed : action.Added;
        foreach (var stroke in remove) if (Ink.Strokes.Contains(stroke)) Ink.Strokes.Remove(stroke);
        foreach (var stroke in add) if (!Ink.Strokes.Contains(stroke)) Ink.Strokes.Add(stroke);
        _tracking = true;
        _dirty = true;
        UpdateUndoButtons();
    }

    private void UpdateUndoButtons()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => Undo();
    private void OnRedoClick(object sender, RoutedEventArgs e) => Redo();
    private void OnClearClick(object sender, RoutedEventArgs e) => Ink.Strokes.Clear(); // tracked → undoable

    // ───────── Keyboard ─────────

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool handled = true;
        switch (e.Key)
        {
            case Key.Z when mods == ModifierKeys.Control: Undo(); break;
            case Key.Z when mods == (ModifierKeys.Control | ModifierKeys.Shift): Redo(); break;
            case Key.Y when mods == ModifierKeys.Control: Redo(); break;
            case Key.S when mods == ModifierKeys.Control: Save(); break;
            case Key.P when mods == ModifierKeys.None: PenTool.IsChecked = true; break;
            case Key.H when mods == ModifierKeys.None: HighlighterTool.IsChecked = true; break;
            case Key.A when mods == ModifierKeys.None: ArrowTool.IsChecked = true; break;
            case Key.E when mods == ModifierKeys.None: EraserTool.IsChecked = true; break;
            case Key.OemOpenBrackets when mods == ModifierKeys.None: (SizeMedium.IsChecked == true ? SizeSmall : SizeMedium).IsChecked = true; break;
            case Key.OemCloseBrackets when mods == ModifierKeys.None: (SizeMedium.IsChecked == true ? SizeLarge : SizeMedium).IsChecked = true; break;
            case >= Key.D1 and <= Key.D6 when mods == ModifierKeys.None:
                ((RadioButton)Swatches.Children[e.Key - Key.D1]).IsChecked = true;
                break;
            case Key.Escape: Cancel(); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    // ───────── Save / cancel ─────────

    private void OnSaveClick(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        SaveRequested?.Invoke(this, Ink.Strokes);
        _dirty = false;
        Close();
    }

    private void OnRevertClick(object sender, RoutedEventArgs e)
    {
        RevertRequested?.Invoke(this, EventArgs.Empty);
        _dirty = false;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancel();

    /// <summary>Esc/close: with unsaved strokes, the first press only warns (so a stray Esc never loses a drawing).</summary>
    private void Cancel()
    {
        if (_dirty && !_escapeWindow.IsEnabled)
        {
            Status.Text = "Unsaved drawing: press Esc again to discard, or Ctrl+S to save";
            _escapeWindow.Start();
            return;
        }
        _dirty = false;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Alt+F4 / taskbar close get the same protection as Esc.
        if (_dirty && !_escapeWindow.IsEnabled)
        {
            e.Cancel = true;
            Status.Text = "Unsaved drawing: close again to discard, or Ctrl+S to save";
            _escapeWindow.Start();
            return;
        }
        base.OnClosing(e);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
