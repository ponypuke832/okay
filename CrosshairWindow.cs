using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shush
{
    /// <summary>
    /// A borderless, click-through overlay window that draws the crosshair
    /// over whatever's on screen. Apply() shows/hides it and redraws it from
    /// a CrosshairCfg. DrawShapes/HexColor are also used standalone (no
    /// window involved) to paint the small style-picker thumbnails and the
    /// settings-page preview canvas in MainWindow.
    /// </summary>
    public class CrosshairWindow : Window
    {
        readonly Canvas canvas = new Canvas { IsHitTestVisible = false, Background = Brushes.Transparent };

        public CrosshairWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            IsHitTestVisible = false;
            Focusable = false;

            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            canvas.Width = Width;
            canvas.Height = Height;
            Content = canvas;

            SourceInitialized += (s, e) => MakeClickThrough();

            // Created hidden; Apply() decides when it should actually show.
            Show();
            Visibility = Visibility.Collapsed;
        }

        /// <summary>Redraws from cfg and shows/hides the overlay.
        /// hideOverlay is true while the main macro window itself is
        /// hidden/minimized-equivalent, so the crosshair never paints over
        /// our own UI when it's up.</summary>
        public void Apply(CrosshairCfg cfg, string style, bool hideOverlay)
        {
            bool show = cfg != null && cfg.Enabled && !hideOverlay;
            if (show) DrawShapes(canvas, cfg, style, Width, Height);
            Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- click-through (WS_EX_TRANSPARENT | WS_EX_LAYERED) -----------
        const int GWL_EXSTYLE = -20;
        const int WS_EX_LAYERED = 0x80000;
        const int WS_EX_TRANSPARENT = 0x20;
        const int WS_EX_TOOLWINDOW = 0x80;

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        void MakeClickThrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        }

        // ---- drawing -------------------------------------------------------
        /// <summary>Paints one crosshair (Cross / X / Dot / Cross + Dot) onto
        /// canvas, centered in a w×h area. Used for the real overlay (screen
        /// size) and for small preview thumbnails alike.</summary>
        public static void DrawShapes(Canvas canvas, CrosshairCfg cfg, string style, double w, double h)
        {
            canvas.Children.Clear();
            if (cfg == null) return;

            double opacity = Math.Max(0, Math.Min(100, cfg.Show)) / 100.0;
            if (opacity <= 0) return;

            double cx = w / 2.0, cy = h / 2.0;
            var mainBrush = new SolidColorBrush(HexColor(cfg.Color, Color.FromRgb(0xFF, 0x3B, 0x30))) { Opacity = opacity };
            var outlineBrush = new SolidColorBrush(Colors.Black) { Opacity = opacity };

            double len = Math.Max(0, cfg.Length);
            double cap = Math.Max(0, cfg.Cap);
            double thick = Math.Max(1, cfg.Thickness);
            double outline = Math.Max(0, cfg.Outline);

            bool wantCross = style == "Cross" || style == "Cross + Dot";
            bool wantX = style == "X";
            bool wantDot = style == "Dot" || style == "Cross + Dot";

            if (wantCross && len > 0)
            {
                AddArm(canvas, cx, cy, 0, -1, cap, len, thick, outline, mainBrush, outlineBrush);
                AddArm(canvas, cx, cy, 0, 1, cap, len, thick, outline, mainBrush, outlineBrush);
                AddArm(canvas, cx, cy, -1, 0, cap, len, thick, outline, mainBrush, outlineBrush);
                AddArm(canvas, cx, cy, 1, 0, cap, len, thick, outline, mainBrush, outlineBrush);
            }
            if (wantX && len > 0)
            {
                const double d = 0.70710678; // 1/sqrt(2) — diagonal unit vector
                AddArm(canvas, cx, cy, -d, -d, cap, len, thick, outline, mainBrush, outlineBrush);
                AddArm(canvas, cx, cy, d, -d, cap, len, thick, outline, mainBrush, outlineBrush);
                AddArm(canvas, cx, cy, -d, d, cap, len, thick, outline, mainBrush, outlineBrush);
                AddArm(canvas, cx, cy, d, d, cap, len, thick, outline, mainBrush, outlineBrush);
            }
            if (wantDot && cfg.Dot > 0)
            {
                double r = cfg.Dot;
                if (outline > 0)
                {
                    var oDot = new Ellipse { Width = (r + outline) * 2, Height = (r + outline) * 2, Fill = outlineBrush };
                    Canvas.SetLeft(oDot, cx - r - outline);
                    Canvas.SetTop(oDot, cy - r - outline);
                    canvas.Children.Add(oDot);
                }
                var dot = new Ellipse { Width = r * 2, Height = r * 2, Fill = mainBrush };
                Canvas.SetLeft(dot, cx - r);
                Canvas.SetTop(dot, cy - r);
                canvas.Children.Add(dot);
            }
        }

        static void AddArm(Canvas canvas, double cx, double cy, double dx, double dy,
                            double cap, double len, double thick, double outline,
                            Brush mainBrush, Brush outlineBrush)
        {
            double x1 = cx + dx * cap, y1 = cy + dy * cap;
            double x2 = cx + dx * (cap + len), y2 = cy + dy * (cap + len);

            if (outline > 0)
            {
                canvas.Children.Add(new Line
                {
                    X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                    Stroke = outlineBrush,
                    StrokeThickness = thick + outline * 2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }
            canvas.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = mainBrush,
                StrokeThickness = thick,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
        }

        /// <summary>Parses "#RRGGBB" (or "#AARRGGBB"); returns fallback on
        /// anything invalid or empty, so a bad/old stored value never crashes
        /// the color picker or a repaint.</summary>
        public static Color HexColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return fallback;
                if (ColorConverter.ConvertFromString(hex) is Color c) return c;
            }
            catch { /* fall through to fallback */ }
            return fallback;
        }
    }
}
