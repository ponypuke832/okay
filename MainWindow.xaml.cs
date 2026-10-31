using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Shush
{
    public partial class MainWindow : Window
    {
        Settings S;
        Engine Eng;
        CrosshairWindow cross;
        string profile;
        string page = "cards";                     // cards | crosshair | profiles | settings
        string sel = "Tropical Royale/Editing";    // explorer selection: "game/sub"
        string openGame = "Tropical Royale";       // which game folder is expanded
        bool gamesOpen = true;
        Popup popup;
        int toastId, wizIdx;
        bool tabFlip;

        readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        readonly DispatcherTimer toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
        readonly List<Action> rowRefresh = new List<Action>();
        readonly Dictionary<string, Border> caps = new Dictionary<string, Border>();
        readonly Dictionary<string, string> keyMap = new Dictionary<string, string>();

        const double RowW = 372;
        const double PopRow = RowW - 16;
        const double CardW = 320;

        static readonly string[] ThemeKeys =
        { "BgBrush", "SideBrush", "CardBrush", "RowBrush", "FieldBrush", "LineBrush", "TextBrush", "MutedBrush", "AccentBrush", "TrackBrush", "HoverBrush", "OnAccentBrush" };

        // 12 colours per theme: bg, side, card, row, field, line, text, muted, accent, track, hover, on-accent
        static readonly Dictionary<string, string[]> Themes = new Dictionary<string, string[]>
        {
            { "Forest",        new[] { "#08120C", "#0C1811", "#0F1D14", "#14251A", "#0A1510", "#1E3325", "#DCEAD9", "#6E8A73", "#9BE84B", "#1B2E21", "#17291C", "#0A160A" } },
            { "Amber",         new[] { "#0C0A08", "#141010", "#191313", "#201818", "#100C0B", "#2E2320", "#F2E9DF", "#9A8578", "#FF8A2B", "#2A1F1A", "#251B16", "#1A0E05" } },
            { "Frost",         new[] { "#F2F5F9", "#FFFFFF", "#FFFFFF", "#EAF0F7", "#F7FAFD", "#D2DCE8", "#0F1B26", "#5C7189", "#2AA7E8", "#DCE6F0", "#E3EDF6", "#FFFFFF" } },
            { "Violet",        new[] { "#0B0614", "#120A20", "#170D29", "#1E1233", "#0E0818", "#2C1A45", "#ECE3FA", "#8B7AA6", "#D946EF", "#281740", "#221439", "#FFFFFF" } },
            { "High Contrast", new[] { "#000000", "#000000", "#000000", "#101010", "#000000", "#FFFFFF", "#FFFFFF", "#C8C8C8", "#FFE600", "#303030", "#1A1A1A", "#000000" } },
        };

        static readonly Dictionary<string, string> ThemeNote = new Dictionary<string, string>
        {
            { "Forest", "Dark green background, lime-green accents - subtle and easy on the eyes during long sessions." },
            { "Amber", "Near-black background, warm orange/amber highlights - feels tactical, good for low-light rooms." },
            { "Frost", "Very light grey/white background, ice-blue accents - high contrast, good for bright rooms." },
            { "Violet", "Deep purple background, magenta/pink accents - purple-leaning instead of red." },
            { "High Contrast", "Pure black background, bright yellow/white text - accessibility / maximum readability." },
        };

        static readonly Dictionary<string, string> NavIcon = new Dictionary<string, string>
        {
            { "Editing", "\u270E" }, { "Building", "\u25A3" }, { "Utility", "\u2699" },
            { "Performance", "\u26A1" }, { "Keybinds", "\u2328" }, { "Games", "\u265F" },
            { "Crosshair", "\u29BF" }, { "Profiles", "\u263A" }, { "Settings", "\u2630" },
        };

        static readonly string[][] KeyList =
        {
            new[] { "edit", "Edit" }, new[] { "pickup", "Pick Up" },
            new[] { "slot1", "Slot 1" }, new[] { "slot2", "Slot 2" }, new[] { "slot3", "Slot 3" }, new[] { "slot4", "Slot 4" },
            new[] { "slot5", "Slot 5" }, new[] { "slot6", "Slot 6" }, new[] { "slot7", "Slot 7" },
            new[] { "wall", "Wall" }, new[] { "floor", "Floor" }, new[] { "ramp", "Ramp" }, new[] { "pyramid", "Pyramid" },
        };

        static readonly string[][] KbRows =
        {
            new[] { "ESC", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" },
            new[] { "TAB", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]" },
            new[] { "CAPS", "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'" },
            new[] { "LSHIFT", "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/", "RSHIFT" },
        };

        public MainWindow()
        {
            InitializeComponent();
            MinBtn.Content = "\uE921";
            CloseBtn.Content = "\uE8BB";
            Native.timeBeginPeriod(1);
            profile = Store.Active();
            S = Store.Load(profile);
            Input.Debug = S.Debug;
            Eng = new Engine(S);
            Eng.ToggleUi += () => Dispatcher.BeginInvoke(new Action(ToggleVisible));
            Eng.PanicEvt += () => Dispatcher.BeginInvoke(new Action(Quit));
            Eng.KeyEvent += (n, d) => Dispatcher.BeginInvoke(new Action(() => Glow(n, d)));
            Eng.CrosshairToggle += () => Dispatcher.BeginInvoke(new Action(ToggleCrosshair));
            cross = new CrosshairWindow();
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); Store.Save(profile, S); };
            ApplyUi();
            BuildAll();
            Eng.Start();
            Loaded += (s, e) => { FadeIn(Root, 12, 320, 0); ApplyCross(); };
            Toast("Macro system ready");
        }

        // ------------------------------------------------------------ small helpers
        TextBlock Txt(string text, string fg = "TextBrush", double size = 15, bool bold = false)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = size,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, fg);
            return tb;
        }

        Border Box(string bg, string border, double radius)
        {
            var b = new Border { CornerRadius = new CornerRadius(radius) };
            b.SetResourceReference(Border.BackgroundProperty, bg);
            if (border != null)
            {
                b.BorderThickness = new Thickness(1);
                b.SetResourceReference(Border.BorderBrushProperty, border);
            }
            return b;
        }

        Style St(string key) { return (Style)FindResource(key); }

        static string OptHelp(string key)
        {
            string h;
            return Specs.OptHelp.TryGetValue(key, out h) ? h : null;
        }

        FrameworkElement HelpBadge(string title, string text)
        {
            var badge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1.2),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                Cursor = Cursors.Help
            };
            badge.SetResourceReference(Border.BorderBrushProperty, "MutedBrush");
            var q = Txt("?", "MutedBrush", 11, true);
            q.HorizontalAlignment = HorizontalAlignment.Center;
            q.Margin = new Thickness(0, -1, 0, 0);
            badge.Child = q;

            var body = new StackPanel { MaxWidth = 290 };
            var ht = Txt(title, "AccentBrush", 14, true);
            ht.Margin = new Thickness(0, 0, 0, 4);
            body.Children.Add(ht);
            var bt = Txt(text, "TextBrush", 13);
            bt.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(bt);
            var box = Box("CardBrush", "AccentBrush", 10);
            box.Padding = new Thickness(12, 9, 12, 10);
            box.Child = body;
            TextElement.SetFontFamily(box, FontFamily);

            badge.ToolTip = new ToolTip
            {
                Content = box,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HasDropShadow = false,
                Placement = PlacementMode.Right,
                HorizontalOffset = 8
            };
            ToolTipService.SetInitialShowDelay(badge, 80);
            ToolTipService.SetShowDuration(badge, 60000);
            badge.MouseEnter += (s, e) =>
            {
                badge.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                q.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            };
            badge.MouseLeave += (s, e) =>
            {
                badge.SetResourceReference(Border.BorderBrushProperty, "MutedBrush");
                q.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            };
            return badge;
        }

        FrameworkElement LabelWith(string label, string help)
        {
            var t = Txt(label, "MutedBrush", 14);
            if (string.IsNullOrEmpty(help)) return t;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(t);
            sp.Children.Add(HelpBadge(label, help));
            return sp;
        }

        Color AccentColor()
        {
            var b = FindResource("AccentBrush") as SolidColorBrush;
            return b != null ? b.Color : Color.FromRgb(0x9B, 0xE8, 0x4B);
        }

        void ApplyUi()
        {
            string[] t;
            if (!Themes.TryGetValue(S.Theme, out t)) t = Themes["Forest"];
            for (int i = 0; i < ThemeKeys.Length; i++)
                Application.Current.Resources[ThemeKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(t[i]));
            StartBreathing();
            Opacity = Math.Max(0.3, S.Opacity / 100.0);
            double sc = Math.Max(0.7, S.Scale / 100.0);
            Root.LayoutTransform = new ScaleTransform(sc, sc);
            Width = 1260 * sc;
            Height = 720 * sc;
            ShowInTaskbar = !S.HideTaskbar;
        }

        // very faint pulse of the accent colour so borders / highlights feel alive.
        //
        // A brand-new brush was still coming up frozen the instant it was assigned
        // to Resources — that means some Style Setter elsewhere references
        // AccentBrush via StaticResource, and WPF freezes a Freezable baked into a
        // Style the first time that Style gets sealed (used). The brush gets swept
        // into that the moment it lands in Resources, regardless of how fresh it is.
        //
        // Fix: start the animation BEFORE the brush is exposed to anything that
        // could freeze it, not after. A Freezable with an active animation reports
        // CanFreeze = false, so once BeginAnimation succeeds here, WPF can no
        // longer seal it later even if a Style does reference it — this wins the
        // race instead of chasing which Style is responsible.
        void StartBreathing()
        {
            var cur = Application.Current.Resources["AccentBrush"] as SolidColorBrush;
            if (cur == null) return;

            var b = new SolidColorBrush(cur.Color);

            if (!S.Breathing)
            {
                b.Opacity = 1.0;
            }
            else
            {
                var a = new DoubleAnimation(1.0, 0.88, TimeSpan.FromSeconds(2.8))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                b.BeginAnimation(SolidColorBrush.OpacityProperty, a); // while unreferenced — can't be frozen yet
            }

            Application.Current.Resources["AccentBrush"] = b; // only now does anything else see it
        }

        void Changed()
        {
            Eng.RebuildBinds();
            saveTimer.Stop();
            saveTimer.Start();
            PaintCaps();
        }

        void RefreshRows()
        {
            foreach (var a in rowRefresh) { try { a(); } catch { } }
        }

        void HintUpdate()
        {
            HintText.Text = S.UiKey + " hide / show   \u00B7   " + S.PanicKey + " panic exit";
        }

        void FadeIn(UIElement el, double fromY, int ms, int delayMs)
        {
            FadeIn(el, fromY, ms, delayMs, 0);
        }

        // tab transitions: fade + slide (X comes from the page switch so it slides sideways)
        void FadeIn(UIElement el, double fromY, int ms, int delayMs, double fromX)
        {
            if (!S.Animations) { el.Opacity = 1; el.RenderTransform = null; return; }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var begin = TimeSpan.FromMilliseconds(delayMs);
            var tt = new TranslateTransform(fromX, fromY);
            el.RenderTransform = tt;
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { BeginTime = begin, EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(ms)) { BeginTime = begin, EasingFunction = ease });
            if (fromX != 0)
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(ms)) { BeginTime = begin, EasingFunction = ease });
        }

        void Toast(string msg)
        {
            toastId++;
            ToastText.Text = msg;
            ToastBox.Visibility = Visibility.Visible;
            var tt = new TranslateTransform(40, 0);
            ToastBox.RenderTransform = tt;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            ToastBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            toastTimer.Stop();
            toastTimer.Tick -= ToastTick;
            toastTimer.Tick += ToastTick;
            toastTimer.Start();
        }

        void ToastTick(object sender, EventArgs e)
        {
            toastTimer.Stop();
            int id = toastId;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
            fade.Completed += (s2, e2) => { if (id == toastId) ToastBox.Visibility = Visibility.Collapsed; };
            ToastBox.BeginAnimation(OpacityProperty, fade);
        }

        void ToggleVisible()
        {
            if (IsVisible) { CloseOptions(); Hide(); }
            else { Show(); Topmost = true; FadeIn(Root, 10, 220, 0); }
            ApplyCross();      // never paint the crosshair over our own window
        }

        void Quit()
        {
            try { Store.Save(profile, S); Eng.Stop(); if (cross != null) cross.Close(); } catch { }
            Native.timeEndPeriod(1);
            Application.Current.Shutdown();
            Environment.Exit(0);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Quit();
        }

        void LoadProfile(string name)
        {
            Eng.StopAll();
            profile = name;
            S = Store.Load(name);
            Eng.S = S;
            Eng.RebuildBinds();
            Input.Debug = S.Debug;
            ApplyUi();
            BuildAll();
            ApplyCross();
            Store.Save(name, S);
            Toast("Profile: " + name);
        }

        // ------------------------------------------------------------ window events
        void Title_Down(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
        void Min_Click(object sender, RoutedEventArgs e) { CloseOptions(); Hide(); ApplyCross(); }
        void Close_Click(object sender, RoutedEventArgs e) { Quit(); }
        void Wiz_Cancel(object sender, RoutedEventArgs e) { EndWizard("Key setup cancelled"); }

        void Start_Click(object sender, RoutedEventArgs e)
        {
            if (Eng.Running) { Eng.StopAll(); Toast("Macros stopped"); }
            else { Eng.Running = true; Toast("Macros running"); }
            RefreshRun();
        }

        void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(Store.Dir);
                Process.Start(new ProcessStartInfo { FileName = Store.Dir, UseShellExecute = true });
            }
            catch (Exception ex) { Log.E("open folder: " + ex.Message); }
        }

        void RefreshRun()
        {
            bool run = Eng.Running;
            StartText.Text = run ? "Stop Macro" : "Start Macro";
            Color target = run ? Color.FromRgb(0xEF, 0x44, 0x44) : AccentColor();
            var brush = StartBtn.Background as SolidColorBrush;
            if (brush == null || brush.IsFrozen) { brush = new SolidColorBrush(target); StartBtn.Background = brush; }
            else brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target, TimeSpan.FromMilliseconds(250)));
            var fx = StartBtn.Effect as DropShadowEffect;
            if (fx == null) { fx = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 14, Opacity = 0.5, Color = target }; StartBtn.Effect = fx; }
            fx.Color = target;
            if (run) fx.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(10, 28, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            else { fx.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null); fx.BlurRadius = 14; }
        }

        // ------------------------------------------------------------ pages
        void BuildAll()
        {
            BuildNav();
            BuildPages();
            RefreshRun();
            HintUpdate();
        }

        void NavItem(Panel host, string icon, string label, bool active, Action click, double indent = 0, double chevAngle = 0, bool glyphChev = false)
        {
            var item = Box(active ? "RowBrush" : "SideBrush", null, 12);
            item.Height = indent > 0 ? 38 : 46;
            item.Margin = new Thickness(indent, 0, 0, 5);
            item.Cursor = Cursors.Hand;
            var g = new Grid { Margin = new Thickness(12, 0, 10, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = Txt(icon ?? "", active ? "AccentBrush" : "MutedBrush", indent > 0 ? 15 : 18);
            ic.FontFamily = (FontFamily)FindResource("Symbols");
            var lb = Txt(label, active ? "AccentBrush" : "TextBrush", indent > 0 ? 14 : 16, true);
            lb.TextTrimming = TextTrimming.CharacterEllipsis;
            var ch = Txt(glyphChev ? "\u203A" : "", "MutedBrush", 20);
            if (chevAngle != 0)
            {
                ch.RenderTransformOrigin = new Point(0.5, 0.5);
                ch.RenderTransform = new RotateTransform(chevAngle);
            }
            Grid.SetColumn(lb, 1);
            Grid.SetColumn(ch, 2);
            g.Children.Add(ic);
            g.Children.Add(lb);
            g.Children.Add(ch);
            item.Child = g;
            item.MouseEnter += (s, e) => item.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            item.MouseLeave += (s, e) => item.SetResourceReference(Border.BackgroundProperty, active ? "RowBrush" : "SideBrush");
            item.MouseLeftButtonUp += (s, e) => click();
            host.Children.Add(item);
        }

        // Games > Tropical Royale > Editing / Building / Utility (+ Performance, Keybinds)
        // Games > Framebreak     > Editing / Building / Utility
        void BuildNav()
        {
            NavHost.Children.Clear();
            MiscHost.Children.Clear();

            NavItem(NavHost, NavIcon["Games"], "Games", page == "cards", () => { gamesOpen = !gamesOpen; BuildNav(); }, 0, gamesOpen ? 90 : 0, true);

            if (gamesOpen)
            {
                foreach (var game in Specs.Games)
                {
                    bool isOpen = openGame == game;
                    string g = game;
                    NavItem(NavHost, isOpen ? "\u25BE" : "\u25B8", g, isOpen, () =>
                    {
                        // clicking the game name only opens / closes the folder
                        openGame = isOpen ? null : g;
                        BuildNav();
                    }, 16, 0, false);

                    if (!isOpen) continue;
                    foreach (var sub in Specs.Subs)
                    {
                        string target = g + "/" + sub;
                        NavItem(NavHost, NavIcon[sub], sub, page == "cards" && sel == target, () =>
                        {
                            sel = target; page = "cards"; BuildNav(); BuildPages();
                        }, 34);
                    }
                    if (g == "Tropical Royale")
                    {
                        NavItem(NavHost, NavIcon["Performance"], "Performance", page == "cards" && sel == g + "/Performance", () =>
                        {
                            sel = g + "/Performance"; page = "cards"; BuildNav(); BuildPages();
                        }, 34);
                        NavItem(NavHost, NavIcon["Keybinds"], "In-game Keybinds", page == "cards" && sel == g + "/Keybinds", () =>
                        {
                            sel = g + "/Keybinds"; page = "cards"; BuildNav(); BuildPages();
                        }, 34);
                    }
                }
            }

            NavItem(NavHost, NavIcon["Crosshair"], "Crosshair", page == "crosshair", () => { page = "crosshair"; BuildNav(); BuildPages(); }, 0);
            NavItem(MiscHost, NavIcon["Profiles"], "Profiles", page == "profiles", () => { page = "profiles"; BuildNav(); BuildPages(); });
            NavItem(MiscHost, NavIcon["Settings"], "Settings", page == "settings", () => { page = "settings"; BuildNav(); BuildPages(); });
        }

        void BuildPages()
        {
            CloseOptions();
            rowRefresh.Clear();
            CardsHost.Visibility = page == "cards" ? Visibility.Visible : Visibility.Collapsed;
            SettingsHost.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
            ProfilesHost.Visibility = page == "profiles" ? Visibility.Visible : Visibility.Collapsed;
            CrossHost.Visibility = page == "crosshair" ? Visibility.Visible : Visibility.Collapsed;
            PageScroll.ScrollToTop();

            tabFlip = !tabFlip;
            double fromX = S.Animations ? (tabFlip ? 22 : -22) : 0;

            if (page == "cards") { BuildCards(); FadeIn(CardsHost, 12, 240, 0, fromX); }
            else if (page == "settings") { BuildSettings(); FadeIn(SettingsHost, 0, 240, 0, fromX); }
            else if (page == "crosshair") { BuildCrosshair(); FadeIn(CrossHost, 0, 240, 0, fromX); }
            else { BuildProfiles(); FadeIn(ProfilesHost, 0, 240, 0, fromX); }
        }

        // ------------------------------------------------------------ game feature pages
        void BuildCards()
        {
            CardsHost.Children.Clear();
            if (string.IsNullOrEmpty(sel)) return;
            var parts = sel.Split('/');
            string game = parts[0];
            string sub = parts.Length > 1 ? parts[1] : "";
            if (sub == "Performance") { CardsHost.Children.Add(PerfCard(468)); return; }
            if (sub == "Keybinds") { CardsHost.Children.Add(KeybindsCard(468)); return; }
            var list = Specs.All.Where(s => s.Game == game && s.Cat == sub).ToList();
            CardsHost.Children.Add(MakeCategoryCard(game, sub, list));
        }

        Border MakeCategoryCard(string game, string sub, List<FeatureSpec> list)
        {
            var outer = Box("CardBrush", "LineBrush", 14);
            outer.Width = CardW;
            outer.Margin = new Thickness(0, 0, 12, 12);
            outer.VerticalAlignment = VerticalAlignment.Top;
            var st = new StackPanel();
            var head = new Grid { Margin = new Thickness(18, 14, 14, 10) };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Txt(NavIcon.ContainsKey(sub) ? NavIcon[sub] : "\u25A1", "AccentBrush", 21);
            ic.FontFamily = (FontFamily)FindResource("Symbols");
            ic.Margin = new Thickness(0, 0, 10, 0);
            left.Children.Add(ic);
            left.Children.Add(Txt(sub, "TextBrush", 18, true));
            head.Children.Add(left);
            var gg = Txt(game, "MutedBrush", 12);
            gg.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(gg);
            st.Children.Add(head);

            var rows = new StackPanel { Margin = new Thickness(12, 0, 12, 8) };
            if (list.Count == 0)
            {
                var empty = Txt("This folder is empty.", "MutedBrush", 13);
                empty.Margin = new Thickness(6, 4, 0, 8);
                rows.Children.Add(empty);
            }
            foreach (var spec in list) rows.Children.Add(MacroRow(spec));
            st.Children.Add(rows);
            outer.Child = st;
            return outer;
        }

        static bool AnyPiece(FeatureCfg f)
        {
            return Specs.Pieces.Any(p => !string.IsNullOrEmpty(f.Str["bind_" + p]));
        }

        string BindText(FeatureSpec spec, FeatureCfg f)
        {
            if (Specs.IsPieceKind(spec.Kind))
            {
                int n = Specs.Pieces.Count(p => !string.IsNullOrEmpty(f.Str["bind_" + p]));
                return n == 0 ? "-" : n + (n == 1 ? " key" : " keys");
            }
            return string.IsNullOrEmpty(f.Bind) ? "-" : f.Bind;
        }

        bool HasBind(FeatureSpec spec, FeatureCfg f)
        {
            return Specs.IsPieceKind(spec.Kind) ? AnyPiece(f) : !string.IsNullOrEmpty(f.Bind);
        }

        void SetEnabled(FeatureSpec spec, FeatureCfg f, bool on)
        {
            f.Enabled = on;
            if (!on) Eng.StopFeature(spec.Id);
            Changed();
            RefreshRows();
        }

        // OFF = the key is FREE again (RebuildBinds skips disabled features, and Conflict() ignores them)
        void ClearBind(FeatureSpec spec, FeatureCfg f)
        {
            if (Specs.IsPieceKind(spec.Kind))
            {
                foreach (var p in Specs.Pieces) f.Str["bind_" + p] = "";
            }
            else f.Bind = "";
            f.Enabled = false;
            Eng.StopFeature(spec.Id);
            Changed();
            RefreshRows();
            Toast("Keybind cleared - key is free");
        }

        // one macro: name + "?" | on/off switch | key box | clear | dots menu
        Border MacroRow(FeatureSpec spec)
        {
            var f = S.Features[spec.Id];
            var row = Box("RowBrush", null, 10);
            row.Height = 48;
            row.Margin = new Thickness(0, 0, 0, 8);
            row.Padding = new Thickness(12, 0, 6, 0);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var nt = Txt(spec.Name, "TextBrush", 14);
            nt.MaxWidth = 108;
            nt.TextTrimming = TextTrimming.CharacterEllipsis;
            nameBox.Children.Add(nt);
            if (!string.IsNullOrEmpty(spec.Help)) nameBox.Children.Add(HelpBadge(spec.Name, spec.Help));
            g.Children.Add(nameBox);

            var sw = new CheckBox { Style = St("Switch"), IsChecked = f.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            sw.ToolTip = "Turn this macro on / off. Off also frees its key.";
            Action dim = () => nt.Opacity = f.Enabled ? 1.0 : 0.5;
            dim();
            sw.Click += (s, e) => SetEnabled(spec, f, sw.IsChecked == true);
            rowRefresh.Add(() => { sw.IsChecked = f.Enabled; dim(); });
            Grid.SetColumn(sw, 1);
            g.Children.Add(sw);

            Button clear = null;
            if (!spec.Auto)
            {
                var kb = new Button
                {
                    Style = St("FlatBtn"),
                    Padding = new Thickness(8, 4, 8, 4),
                    MinWidth = 52,
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Action show = () => kb.Content = BindText(spec, f);
                show();
                rowRefresh.Add(show);
                if (Specs.IsPieceKind(spec.Kind)) kb.Click += (s, e) => ShowOptions(spec.Id, kb);
                else kb.Click += (s, e) => CaptureKey(kb, () => f.Bind, v => { f.Bind = v; f.Enabled = v != ""; }, show, "trigger", null);
                Grid.SetColumn(kb, 2);
                g.Children.Add(kb);

                // "✕" clear button: removes the bind entirely, the feature stays off until you assign a key
                clear = new Button
                {
                    Style = St("WinBtn"),
                    Content = "\u2715",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 11,
                    Width = 22,
                    Height = 28,
                    Margin = new Thickness(0, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Clear this keybind"
                };
                clear.Click += (s, e) => ClearBind(spec, f);
                Grid.SetColumn(clear, 3);
                g.Children.Add(clear);
                rowRefresh.Add(() =>
                {
                    bool has = HasBind(spec, f);
                    clear.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
                    clear.Opacity = has ? 1 : 0;
                });
            }

            var dots = new Button { Style = St("WinBtn"), Content = "\u22EE", FontFamily = (FontFamily)FindResource("Symbols"), FontSize = 18, Width = 24, Height = 30 };
            dots.Click += (s, e) => ShowOptions(spec.Id, dots);
            Grid.SetColumn(dots, 4);
            g.Children.Add(dots);
            row.Child = g;
            row.MouseEnter += (s, e) => row.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            row.MouseLeave += (s, e) => row.SetResourceReference(Border.BackgroundProperty, "RowBrush");
            return row;
        }

        // ------------------------------------------------------------ the dots menu
        void CloseOptions()
        {
            if (popup != null) { popup.IsOpen = false; popup = null; }
        }

        void ShowOptions(string id, FrameworkElement anchor)
        {
            CloseOptions();
            var spec = Specs.ById[id];
            var f = S.Features[id];
            var card = Box("CardBrush", "LineBrush", 14);
            card.Padding = new Thickness(18, 14, 18, 4);
            TextElement.SetFontFamily(card, FontFamily);
            TextElement.SetFontSize(card, 14);
            card.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");

            var st = new StackPanel { Width = RowW };
            var head = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Txt(spec.Name, "TextBrush", 18, true));
            if (!string.IsNullOrEmpty(spec.Help)) title.Children.Add(HelpBadge(spec.Name, spec.Help));
            head.Children.Add(title);
            var x = new Button { Style = St("WinBtn"), Content = "\uE8BB", HorizontalAlignment = HorizontalAlignment.Right, Width = 28, Height = 28 };
            x.Click += (s, e) => CloseOptions();
            head.Children.Add(x);
            st.Children.Add(head);

            st.Children.Add(SwitchRow("Enabled", f.Enabled, v => SetEnabled(spec, f, v), PopRow, "Turn this macro on or off without losing its key - off also frees the key."));

            if (!spec.Auto && !Specs.IsPieceKind(spec.Kind))
                st.Children.Add(BindRow("Trigger key", () => f.Bind, v => { f.Bind = v; f.Enabled = v != ""; }, PopRow, "trigger", null));
            st.Children.Add(SliderRow(spec.DelayLabel, spec.DelayMin, spec.DelayMax, f.Delay, " ms", v => f.Delay = v, PopRow, null, OptHelp("delay")));
            foreach (var o in spec.Opts)
            {
                var op = o;
                string help = OptHelp(op.Key);
                if (op.Type == "slider")
                    st.Children.Add(SliderRow(op.Label, op.Min, op.Max, f.Num[op.Key], op.Unit, v => f.Num[op.Key] = v, PopRow, null, help));
                else if (op.Type == "switch")
                    st.Children.Add(SwitchRow(op.Label, f.Num[op.Key] == 1, v => f.Num[op.Key] = v ? 1 : 0, PopRow, help));
                else if (op.Type == "bind")
                    st.Children.Add(BindRow(op.Label, () => f.Str[op.Key],
                        v =>
                        {
                            bool had = AnyPiece(f);
                            f.Str[op.Key] = v;
                            bool has = AnyPiece(f);
                            if (!had && has) f.Enabled = true;
                            if (had && !has) f.Enabled = false;
                        }, PopRow, "trigger", null));
                else if (op.Type == "tiles")
                    st.Children.Add(TilesRow(op.Label, () => f.Str[op.Key], v => f.Str[op.Key] = v, PopRow));
                else
                    st.Children.Add(ChoiceRow(op.Label, op.Choices, f.Str[op.Key], v => f.Str[op.Key] = v, PopRow, help));
            }
            card.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, MaxHeight = 620, Content = st };
            popup = new Popup
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Right,
                HorizontalOffset = 10,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                Child = card
            };
            popup.IsOpen = true;
        }

        // ------------------------------------------------------------ settings page (Performance + Keybinds moved OUT)
        void BuildSettings()
        {
            SettingsHost.Children.Clear();
            double w = 468, rw = w - 56;

            var note = Txt(ThemeNote.ContainsKey(S.Theme) ? ThemeNote[S.Theme] : "", "MutedBrush", 13);
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(0, -4, 16, 14);

            SettingsHost.Children.Add(Section("Appearance", w,
                ChoiceRow("Theme", Settings.ThemeNames, S.Theme,
                    v =>
                    {
                        S.Theme = v;
                        note.Text = ThemeNote.ContainsKey(v) ? ThemeNote[v] : "";
                        Dispatcher.BeginInvoke(new Action(() => { ApplyUi(); BuildAll(); }));
                    }, rw),
                note,
                SwitchRow("Tab animations", S.Animations, v => S.Animations = v, rw, "Fade / slide when you switch between folders and pages."),
                SwitchRow("Breathing accent", S.Breathing, v => { S.Breathing = v; StartBreathing(); }, rw, "Accent borders and highlights pulse very faintly."),
                SliderRow("Window opacity", 30, 100, S.Opacity, "%", v => { S.Opacity = v; Opacity = v / 100.0; }, rw),
                SliderRow("UI size", 70, 150, S.Scale, "%", v => S.Scale = v, rw, () => ApplyUi())));

            SettingsHost.Children.Add(Section("Window", w,
                SwitchRow("Hide from taskbar", S.HideTaskbar, v => { S.HideTaskbar = v; ShowInTaskbar = !v; }, rw),
                BindRow("Show / hide UI key", () => S.UiKey, v => S.UiKey = v, rw, "ui", HintUpdate),
                BindRow("Panic key (exit)", () => S.PanicKey, v => S.PanicKey = v, rw, "panic", HintUpdate),
                SwitchRow("Only run while Roblox is focused", S.RobloxOnly, v => S.RobloxOnly = v, rw, "Macros only fire while the Roblox window is in front."),
                SwitchRow("Log every input sent (config/shush.log)", S.Debug, v => { S.Debug = v; Input.Debug = v; }, rw)));

            SettingsHost.Children.Add(Section("Reset method", w,
                ChoiceRow("Reset input", new[] { "Right Click", "Scroll Down" }, S.ResetMethod, v => S.ResetMethod = v, rw, "What Auto Reset presses to reset an edit."),
                SliderRow("Scroll reset duration", 0, 1000, S.ScrollMs, " ms", v => S.ScrollMs = v, rw),
                SliderRow("Key press duration", 1, 100, S.TapMs, " ms", v => S.TapMs = v, rw)));
        }

        // Performance - now lives at Games > Tropical Royale > Performance
        Border PerfCard(double w)
        {
            double rw = w - 56;
            var perf = Txt("", "MutedBrush", 13);
            perf.TextWrapping = TextWrapping.Wrap;
            perf.Margin = new Thickness(0, 0, 16, 14);
            Action upd = () =>
            {
                double fr = 1000.0 / Math.Max(30, S.Fps);
                perf.Text = string.Format("Frame time {0:0.0} ms. Minimums: key hold {1} ms, click hold {2} ms, grid delay {2} ms, gap {3} ms.",
                    fr, (int)Math.Ceiling(fr * 1.5), (int)Math.Ceiling(fr * 2), (int)Math.Ceiling(fr * 3));
            };
            upd();

            return Section("Performance", w,
                ChoiceRow("In-game FPS", new[] { "60", "120", "144", "165", "240", "360", "500" }, S.Fps.ToString(),
                    v => { S.Fps = int.Parse(v); upd(); }, rw, "Used by every macro that has no FPS slider of its own."),
                perf,
                SwitchRow("Enforce minimum timings", S.FpsFloor, v => S.FpsFloor = v, rw, "Raises any delay that is shorter than the game can read at your FPS. Macros with their own FPS slider always do this."),
                ButtonRow("Apply recommended timings", ApplyRecommended));
        }

        // In-game keybinds - now lives at Games > Tropical Royale > In-game Keybinds
        Border KeybindsCard(double w)
        {
            double rw = w - 56;
            var keyRows = new List<FrameworkElement>();
            keyRows.Add(ButtonRow("Key setup wizard", StartWizard));
            keyRows.Add(ButtonRow("Tropical Royale defaults", () =>
            {
                foreach (var kv in Settings.DefaultKeys) S.Keys[kv.Key] = kv.Value;
                Changed();
                BuildPages();
                Toast("Default keybinds applied");
            }));
            foreach (var kv in KeyList)
            {
                var key = kv[0];
                keyRows.Add(BindRow(kv[1], () => S.Keys[key], v => S.Keys[key] = v, rw, "macro", null));
            }
            return Section("In-game keybinds", w, keyRows.ToArray());
        }

        // ------------------------------------------------------------ crosshair page
        CrosshairCfg CurCross()
        {
            CrosshairCfg c;
            if (S.Cross != null && S.Cross.TryGetValue(S.CrosshairStyle, out c) && c != null) return c;
            return new CrosshairCfg();
        }

        void ApplyCross()
        {
            var c = CurCross();
            if (cross != null) cross.Apply(c, S.CrosshairStyle, !IsVisible);
            Eng.CrosshairKey = c.ToggleKey ?? "";
        }

        void ToggleCrosshair()
        {
            var c = CurCross();
            c.Enabled = !c.Enabled;
            ApplyCross();
            if (page == "crosshair") BuildPages();
            Toast("Crosshair " + (c.Enabled ? "shown" : "hidden"));
        }

        void BuildCrosshair()
        {
            CrossHost.Children.Clear();
            double w = 468, rw = w - 56;
            var cfg = CurCross();

            // ---- pick the shape: no dropdown, the folder shows them straight away
            var styles = new WrapPanel();
            foreach (var stName in Specs.CrossStyles)
            {
                string name = stName;
                var cc = S.Cross[name];
                bool on = S.CrosshairStyle == name;
                var tile = Box(on ? "RowBrush" : "FieldBrush", on ? "AccentBrush" : "LineBrush", 12);
                tile.Width = 138;
                tile.Height = 92;
                tile.Margin = new Thickness(0, 0, 8, 8);
                tile.Cursor = Cursors.Hand;
                var sp = new StackPanel();
                var cv = new Canvas { Width = 138, Height = 58, Background = Brushes.Transparent, IsHitTestVisible = false };
                CrosshairWindow.DrawShapes(cv, cc, name, 138, 58);
                sp.Children.Add(cv);
                var nm = Txt(name, on ? "AccentBrush" : "TextBrush", 13, true);
                nm.HorizontalAlignment = HorizontalAlignment.Center;
                nm.Margin = new Thickness(0, 2, 0, 4);
                sp.Children.Add(nm);
                tile.Child = sp;
                tile.MouseLeftButtonUp += (s, e) =>
                {
                    S.CrosshairStyle = name;
                    ApplyCross();
                    BuildPages();
                };
                styles.Children.Add(tile);
            }
            CrossHost.Children.Add(Section("Crosshairs (your own, over the game)", w, styles));

            // ---- the selected crosshair settings
            var prevCanvas = new Canvas { Width = 200, Height = 150, Background = Brushes.Transparent, IsHitTestVisible = false };
            Action repaint = () =>
            {
                CrosshairWindow.DrawShapes(prevCanvas, cfg, S.CrosshairStyle, 200, 150);
                ApplyCross();
            };

            var rows = new List<FrameworkElement>();
            rows.Add(SwitchRow("Show on screen", cfg.Enabled, v => { cfg.Enabled = v; repaint(); }, rw,
                "Master switch for this crosshair. The toggle key below also flips it while you play."));
            rows.Add(BindRow("Toggle key", () => cfg.ToggleKey, v => { cfg.ToggleKey = v; ApplyCross(); }, rw, "trigger", null));
            rows.Add(SliderRow("Show on Screen", 0, 100, cfg.Show, "%", v => { cfg.Show = v; repaint(); }, rw, null, OptHelp("show")));
            rows.Add(ColorRow(cfg, rw, repaint));
            rows.Add(SliderRow("Opacity", 0, 100, cfg.Show, "%", v => { cfg.Show = v; repaint(); }, rw, null, "Same value as Show on Screen (kept for convenience)."));
            rows.Add(SliderRow("Length", 0, 40, cfg.Length, " px", v => { cfg.Length = v; repaint(); }, rw, null, OptHelp("length")));
            rows.Add(SliderRow("Thickness", 1, 8, cfg.Thickness, " px", v => { cfg.Thickness = v; repaint(); }, rw, null, OptHelp("thickness")));
            rows.Add(SliderRow("Cap", 0, 20, cfg.Cap, " px", v => { cfg.Cap = v; repaint(); }, rw, null, OptHelp("cap")));
            rows.Add(SliderRow("Outline", 0, 6, cfg.Outline, " px", v => { cfg.Outline = v; repaint(); }, rw, null, OptHelp("outline")));
            rows.Add(SliderRow("Center Dot", 0, 12, cfg.Dot, " px", v => { cfg.Dot = v; repaint(); }, rw, null, OptHelp("dot")));
            repaint();
            CrossHost.Children.Add(Section("Appearance - " + S.CrosshairStyle, w, rows.ToArray()));

            // ---- preview
            prevCanvas.Background = null;
            var prevBox = Box("FieldBrush", "LineBrush", 12);
            prevBox.Width = 232;
            prevBox.Height = 182;
            prevBox.HorizontalAlignment = HorizontalAlignment.Left;
            prevBox.Child = prevCanvas;
            var prevWrap = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            prevWrap.Children.Add(prevBox);
            CrossHost.Children.Add(Section("Preview", w, prevWrap));
        }

        // RGB picker + the seven quick presets
        FrameworkElement ColorRow(CrosshairCfg cfg, double w, Action repaint)
        {
            var p = new StackPanel { Width = w, Margin = new Thickness(0, 0, 16, 14) };
            var head = new Grid();
            head.Children.Add(Txt("Appearance colour (RGB picker)", "MutedBrush", 14));
            var hexLabel = Txt(cfg.Color, "AccentBrush", 14, true);
            hexLabel.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(hexLabel);
            p.Children.Add(head);

            var col = CrosshairWindow.HexColor(cfg.Color, Color.FromRgb(0xFF, 0x3B, 0x30));
            var slR = new Slider { Minimum = 0, Maximum = 255, Value = col.R, Style = St("Sl"), IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(0, 8, 0, 0) };
            var slG = new Slider { Minimum = 0, Maximum = 255, Value = col.G, Style = St("Sl"), IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(0, 2, 0, 0) };
            var slB = new Slider { Minimum = 0, Maximum = 255, Value = col.B, Style = St("Sl"), IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(0, 2, 0, 8) };

            Action apply = () =>
            {
                cfg.Color = string.Format("#{0:X2}{1:X2}{2:X2}", (int)Math.Round(slR.Value), (int)Math.Round(slG.Value), (int)Math.Round(slB.Value));
                hexLabel.Text = cfg.Color;
                repaint();
            };
            slR.ValueChanged += (s, e) => apply();
            slG.ValueChanged += (s, e) => apply();
            slB.ValueChanged += (s, e) => apply();

            p.Children.Add(NamedSlider("Red", slR));
            p.Children.Add(NamedSlider("Green", slG));
            p.Children.Add(NamedSlider("Blue", slB));

            var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) };
            var presets = new[]
            {
                new[] { "Red", "#FF3B30" }, new[] { "Blue", "#2E7DFF" }, new[] { "Green", "#34C759" }, new[] { "Black", "#000000" },
                new[] { "Pink", "#FF4FA3" }, new[] { "Gray", "#9AA0A6" }, new[] { "Purple", "#B04BFF" },
            };
            foreach (var pr in presets)
            {
                string nm = pr[0], hx = pr[1];
                var b = new Button { Style = St("FlatBtn"), Content = nm, FontSize = 12, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6) };
                b.Click += (s, e) =>
                {
                    var c2 = CrosshairWindow.HexColor(hx, Colors.Red);
                    slR.Value = c2.R; slG.Value = c2.G; slB.Value = c2.B;
                    apply();
                };
                chips.Children.Add(b);
            }
            p.Children.Add(chips);
            return p;
        }

        FrameworkElement NamedSlider(string label, Slider s)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            var t = Txt(label, "MutedBrush", 12);
            t.Width = 48;
            g.Children.Add(t);
            s.Margin = new Thickness(52, 0, 0, 0);
            g.Children.Add(s);
            return g;
        }

        // ------------------------------------------------------------ profiles + live keyboard
        void BuildProfiles()
        {
            ProfilesHost.Children.Clear();
            caps.Clear();
            double w = 468, rw = w - 56;

            var nameBox = new TextBox { Style = St("Field"), Width = rw - 16, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 10) };
            var saveAs = new Button { Style = St("FlatBtn"), Content = "Save as", Margin = new Thickness(0, 0, 8, 0) };
            saveAs.Click += (s, e) =>
            {
                var n = nameBox.Text.Trim();
                foreach (var ch in Path.GetInvalidFileNameChars()) n = n.Replace(ch.ToString(), "");
                if (n == "" || n == "_state") { Toast("Enter a valid profile name"); return; }
                Store.Save(n, Store.Clone(S));
                LoadProfile(n);
            };
            var del = new Button { Style = St("FlatBtn"), Content = "Delete", Margin = new Thickness(0, 0, 8, 0) };
            del.Click += (s, e) =>
            {
                if (profile == "default") { Toast("The default profile can't be deleted"); return; }
                Store.Delete(profile);
                LoadProfile("default");
            };
            var open = new Button { Style = St("FlatBtn"), Content = "Open folder" };
            open.Click += OpenFolder_Click;
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            btns.Children.Add(saveAs);
            btns.Children.Add(del);
            btns.Children.Add(open);
            var wrap = new StackPanel { Width = rw };
            wrap.Children.Add(nameBox);
            wrap.Children.Add(btns);
            ProfilesHost.Children.Add(Section("Profiles", w,
                ChoiceRow("Active profile", Store.Profiles().ToArray(), profile,
                    v => Dispatcher.BeginInvoke(new Action(() => LoadProfile(v))), rw),
                wrap));

            // ---- live keyboard / mouse
            var kb = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var row in KbRows)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var k in row) line.Children.Add(Cap(k, KeyW(k)));
                kb.Children.Add(line);
            }
            var last = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 8) };
            last.Children.Add(Cap("LCTRL", 44));
            last.Children.Add(Cap("LALT", 40));
            last.Children.Add(Cap("SPACE", 150));
            last.Children.Add(Cap("RALT", 40));
            last.Children.Add(Cap("RCTRL", 44));
            kb.Children.Add(last);

            var mouseLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
            mouseLine.Children.Add(Cap("MOUSE1", 54));
            mouseLine.Children.Add(Cap("MOUSE2", 54));
            mouseLine.Children.Add(Cap("MOUSE3", 54));
            mouseLine.Children.Add(Cap("MOUSE4", 54));
            mouseLine.Children.Add(Cap("MOUSE5", 54));
            kb.Children.Add(mouseLine);

            var kbNote = Txt("Bound keys light up in the theme accent. Press a key to see it glow; hover a bound key for the feature and its game.", "MutedBrush", 12);
            kbNote.TextWrapping = TextWrapping.Wrap;
            kbNote.Margin = new Thickness(0, 8, 0, 4);

            ProfilesHost.Children.Add(Section("Keyboard & mouse (live)", w, kb, kbNote));
            rowRefresh.Add(PaintCaps);
            PaintCaps();

            string text = "External input macro tool for Tropical Royale and Framebreak. It listens with low-level Windows hooks and sends input with the native SendInput API. It never reads or writes game memory and never injects anything.\n\nTip: create config/about.txt next to the exe and whatever you write in it shows up here.";
            try
            {
                var path = Path.Combine(Store.Dir, "about.txt");
                if (File.Exists(path)) text = File.ReadAllText(path);
            }
            catch { }
            var about = Txt(text, "MutedBrush", 15);
            about.TextWrapping = TextWrapping.Wrap;
            about.Margin = new Thickness(0, 0, 16, 16);
            ProfilesHost.Children.Add(Section("About", w, about));
        }

        double KeyW(string name)
        {
            switch (name)
            {
                case "LSHIFT":
                case "RSHIFT": return 62;
                case "CAPS": return 56;
                case "TAB": return 46;
                default: return 28;
            }
        }

        string KeyLabel(string name)
        {
            switch (name)
            {
                case "LSHIFT": case "RSHIFT": return "Shift";
                case "LCTRL": case "RCTRL": return "Ctrl";
                case "LALT": case "RALT": return "Alt";
                case "SPACE": return "Space";
                case "CAPS": return "Caps";
                case "TAB": return "Tab";
                case "ESC": return "Esc";
                case "BACKSPACE": return "Bksp";
                case "MOUSE1": return "LMB";
                case "MOUSE2": return "RMB";
                case "MOUSE3": return "Wheel";
                case "MOUSE4": return "M4";
                case "MOUSE5": return "M5";
                default: return name;
            }
        }

        Border Cap(string name, double w)
        {
            var b = new Border
            {
                Width = w,
                Height = 32,
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(0, 0, 2, 2),
                BorderThickness = new Thickness(1),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            b.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
            b.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            var t = new TextBlock
            {
                Text = KeyLabel(name),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            b.Child = t;
            if (!string.IsNullOrEmpty(name) && !caps.ContainsKey(name)) caps[name] = b;
            return b;
        }

        void BuildKeyMap()
        {
            keyMap.Clear();
            foreach (var kv in S.Features)
            {
                if (!kv.Value.Enabled) continue;             // OFF = not highlighted, key is free
                FeatureSpec sp;
                if (!Specs.ById.TryGetValue(kv.Key, out sp) || sp.Auto) continue;
                if (Specs.IsPieceKind(sp.Kind))
                {
                    foreach (var p in Specs.Pieces)
                    {
                        string b;
                        if (kv.Value.Str.TryGetValue("bind_" + p, out b) && !string.IsNullOrEmpty(b))
                            keyMap[b] = sp.Name + " (" + p + ")\n" + sp.Game;
                    }
                }
                else if (!string.IsNullOrEmpty(kv.Value.Bind))
                    keyMap[kv.Value.Bind] = sp.Name + "\n" + sp.Game;
            }
            var cc = CurCross();
            if (cc.Enabled && !string.IsNullOrEmpty(cc.ToggleKey))
                keyMap[cc.ToggleKey] = "Crosshair (" + S.CrosshairStyle + ")\nOverlay toggle";
        }

        void PaintCaps()
        {
            BuildKeyMap();
            foreach (var kv in caps)
            {
                var b = kv.Value;
                var t = b.Child as TextBlock;
                string tip;
                if (keyMap.TryGetValue(kv.Key, out tip))
                {
                    b.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    b.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                    if (t != null) t.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
                    b.ToolTip = tip;
                    ToolTipService.SetInitialShowDelay(b, 150);
                    ToolTipService.SetShowDuration(b, 20000);
                }
                else
                {
                    b.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
                    b.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
                    if (t != null) t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                    b.ToolTip = null;
                }
            }
        }

        // press glow on the visual layout (fed by the real hook)
        void Glow(string name, bool down)
        {
            Border b;
            if (!caps.TryGetValue(name, out b)) return;
            if (down)
            {
                var sc = new ScaleTransform(1, 1);
                b.RenderTransformOrigin = new Point(0.5, 0.5);
                b.RenderTransform = sc;
                var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, 1.16, TimeSpan.FromMilliseconds(90)) { AutoReverse = true, EasingFunction = ease });
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, 1.16, TimeSpan.FromMilliseconds(90)) { AutoReverse = true, EasingFunction = ease });
                b.Effect = new DropShadowEffect { Color = AccentColor(), ShadowDepth = 0, BlurRadius = 24, Opacity = 1 };
                if (S.Animations) b.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(180)));
            }
            else b.Effect = null;
        }

        // ------------------------------------------------------------ performance preset
        void ApplyRecommended()
        {
            double fr = 1000.0 / Math.Max(30, S.Fps);
            Func<double, int> c = m => (int)Math.Ceiling(fr * m);
            int fps = Math.Min(240, Math.Max(60, S.Fps));
            S.TapMs = c(1.5) + 1;
            foreach (var id in new[] { "drag_edit", "single_edit", "double_edit", "simple_edit", "fb_drag", "fb_double" })
            {
                var f = S.Features[id];
                f.Delay = c(2.5);
                SetNum(f, "click_hold", c(2.5));
                SetNum(f, "gap", c(4));
                SetNum(f, "release_delay", c(2.5));
                SetNum(f, "step_delay", c(1));
                SetNum(f, "tile_dwell", c(1.5));
                SetNum(f, "reset_delay", c(4));
                SetNum(f, "reset_step", c(3));
            }
            foreach (var id in new[] { "fb_drag", "fb_rapid", "fb_instant", "fb_editreset" })
                SetNum(S.Features[id], "fps", fps);
            // Edit Reset stays inside its 100-250 ms window
            var er = S.Features["fb_editreset"];
            er.Delay = Math.Max(100, Math.Min(250, c(3)));
            Changed();
            BuildPages();
            Toast("Timings set for " + S.Fps + " FPS");
        }

        static void SetNum(FeatureCfg f, string key, int v) { if (f.Num.ContainsKey(key)) f.Num[key] = v; }

        // ------------------------------------------------------------ key conflicts + setup wizard
        // only ENABLED features own their key, so a turned-off feature's key can be reused
        IEnumerable<string> AllBinds()
        {
            foreach (var kv in S.Features)
            {
                if (!kv.Value.Enabled) continue;
                FeatureSpec spec;
                if (!Specs.ById.TryGetValue(kv.Key, out spec)) continue;
                if (Specs.IsPieceKind(spec.Kind))
                {
                    foreach (var p in Specs.Pieces)
                        if (!string.IsNullOrEmpty(kv.Value.Str["bind_" + p])) yield return kv.Value.Str["bind_" + p];
                }
                else if (!string.IsNullOrEmpty(kv.Value.Bind)) yield return kv.Value.Bind;
            }
        }

        string Conflict(string name, string kind)
        {
            if (kind != "ui" && name == S.UiKey) return "show/hide key";
            if (kind != "panic" && name == S.PanicKey) return "panic key";
            if ((kind == "ui" || kind == "panic") && AllBinds().Contains(name)) return "macro key";
            if (kind == "trigger" && AllBinds().Contains(name)) return "trigger for another macro";
            return null;
        }

        void StartWizard()
        {
            CloseOptions();
            wizIdx = 0;
            Wizard.Visibility = Visibility.Visible;
            FadeIn(Wizard, 10, 200, 0);
            WizardNext();
        }

        void WizardNext()
        {
            if (wizIdx >= KeyList.Length) { EndWizard("Keybinds saved"); return; }
            var k = KeyList[wizIdx];
            int my = wizIdx;
            WizTitle.Text = "Key setup   " + (wizIdx + 1) + " / " + KeyList.Length;
            WizBody.Text = "Press your in-game " + k[1] + " key now.";
            Action<string> cb = null;
            cb = name => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Wizard.Visibility != Visibility.Visible || my != wizIdx) return;
                if (name != "ESC")
                {
                    var why = Conflict(name, "macro");
                    if (why != null) { Toast("That is your " + why + " - pick another"); WizardNext(); return; }
                    S.Keys[k[0]] = name;
                }
                wizIdx++;
                WizardNext();
            }));
            Eng.Capture = cb;
        }

        void EndWizard(string msg)
        {
            Eng.Capture = null;
            Wizard.Visibility = Visibility.Collapsed;
            Changed();
            if (page == "cards") BuildPages();
            if (msg != null) Toast(msg);
        }

        // ------------------------------------------------------------ row helpers
        Border Section(string title, double w, params FrameworkElement[] rows)
        {
            var card = Box("CardBrush", "LineBrush", 14);
            card.Width = w;
            card.Margin = new Thickness(0, 0, 14, 14);
            card.VerticalAlignment = VerticalAlignment.Top;
            card.Padding = new Thickness(20, 16, 20, 6);
            var st = new StackPanel();
            var h = Txt(title, "TextBrush", 19, true);
            h.Margin = new Thickness(0, 0, 0, 16);
            st.Children.Add(h);
            foreach (var r in rows) if (r != null) st.Children.Add(r);
            card.Child = st;
            return card;
        }

        FrameworkElement ButtonRow(string label, Action click)
        {
            var b = new Button { Style = St("FlatBtn"), Content = label, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
            b.Click += (s, e) => click();
            return b;
        }

        FrameworkElement BindRow(string label, Func<string> get, Action<string> set, double w, string kind, Action after)
        {
            var g = new Grid { Width = w, Margin = new Thickness(0, 0, 16, 14) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(Txt(label, "MutedBrush", 14));
            var b = new Button { Style = St("FlatBtn"), MinWidth = 104, HorizontalAlignment = HorizontalAlignment.Right };
            Action show = () => b.Content = string.IsNullOrEmpty(get()) ? "-" : get();
            show();
            b.Click += (s, e) => CaptureKey(b, get, set, show, kind, after);
            Grid.SetColumn(b, 1);
            g.Children.Add(b);

            var clr = new Button
            {
                Style = St("WinBtn"),
                Content = "\u2715",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Width = 22,
                Height = 28,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Clear this keybind"
            };
            clr.Click += (s, e) =>
            {
                b.Content = "-";
                set("");
                Changed();
                RefreshRows();
                if (after != null) after();
            };
            Action vis = () => clr.Visibility = string.IsNullOrEmpty(get()) ? Visibility.Collapsed : Visibility.Visible;
            vis();
            b.Click += (s, e) => Dispatcher.BeginInvoke(new Action(vis), DispatcherPriority.Background);
            Grid.SetColumn(clr, 2);
            g.Children.Add(clr);
            return g;
        }

        void CaptureKey(Button b, Func<string> get, Action<string> set, Action show, string kind, Action after)
        {
            b.Content = "press a key...";
            Action<string> cb = null;
            cb = name => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (name == "ESC" && (kind == "ui" || kind == "panic")) { show(); return; }
                if (name != "ESC" && name != get())
                {
                    var why = Conflict(name, kind);
                    if (why != null) { Toast("That key is already your " + why); show(); return; }
                }
                set(name == "ESC" ? "" : name);
                show();
                Changed();
                RefreshRows();
                if (after != null) after();
            }));
            Eng.Capture = cb;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                if (Eng.Capture == cb) { Eng.Capture = null; show(); }
            };
            t.Start();
        }

        FrameworkElement SwitchRow(string label, bool val, Action<bool> set, double w, string help = null)
        {
            var g = new Grid { Width = w, Margin = new Thickness(0, 0, 16, 14) };
            g.Children.Add(LabelWith(label, help));
            var c = new CheckBox { Style = St("Switch"), IsChecked = val, HorizontalAlignment = HorizontalAlignment.Right };
            c.Click += (s, e) => { set(c.IsChecked == true); Changed(); };
            g.Children.Add(c);
            return g;
        }

        FrameworkElement SliderRow(string label, int min, int max, int val, string unit, Action<int> set, double w, Action release = null, string help = null)
        {
            var p = new StackPanel { Width = w, Margin = new Thickness(0, 0, 16, 14) };
            var head = new Grid();
            head.Children.Add(LabelWith(label, help));
            var v = Txt(val + unit, "AccentBrush", 14, true);
            v.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(v);
            p.Children.Add(head);
            var sl = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, val)),
                Style = St("Sl"),
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                Margin = new Thickness(0, 6, 0, 0)
            };
            sl.ValueChanged += (s, e) =>
            {
                int x = (int)Math.Round(sl.Value);
                v.Text = x + unit;
                set(x);
                Changed();
            };
            if (release != null)
                sl.PreviewMouseLeftButtonUp += (s, e) => Dispatcher.BeginInvoke(release, DispatcherPriority.Background);
            p.Children.Add(sl);
            return p;
        }

        FrameworkElement ChoiceRow(string label, string[] choices, string cur, Action<string> set, double w, string help = null)
        {
            var p = new StackPanel { Width = w, Margin = new Thickness(0, 0, 16, 10) };
            p.Children.Add(LabelWith(label, help));
            Panel host;
            if (choices.Length == 9) host = new UniformGrid { Columns = 3, Width = 330, HorizontalAlignment = HorizontalAlignment.Left };
            else host = new WrapPanel();
            var chips = new List<ToggleButton>();
            foreach (var c in choices)
            {
                var val = c;
                var t = new ToggleButton { Content = c, IsChecked = c == cur, Style = St("Chip") };
                t.Click += (s, e) =>
                {
                    foreach (var o in chips) o.IsChecked = ReferenceEquals(o, t);
                    set(val);
                    Changed();
                };
                chips.Add(t);
                host.Children.Add(t);
            }
            p.Children.Add(host);
            return p;
        }

        FrameworkElement TilesRow(string label, Func<string> get, Action<string> set, double w)
        {
            var p = new StackPanel { Width = w, Margin = new Thickness(0, 0, 16, 12) };
            p.Children.Add(Txt(label, "MutedBrush", 14));
            var grid = new UniformGrid { Columns = 3, Width = 174, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            string mask = get();
            if (mask == null || mask.Length != 9) mask = "000000000";
            for (int i = 0; i < 9; i++)
            {
                int idx = i;
                var t = new ToggleButton { IsChecked = mask[i] == '1', Style = St("Chip"), Margin = new Thickness(0, 0, 6, 6), Width = 52, Height = 42 };
                t.Click += (s, e) =>
                {
                    var cur = get();
                    if (cur == null || cur.Length != 9) cur = "000000000";
                    var arr = cur.ToCharArray();
                    arr[idx] = t.IsChecked == true ? '1' : '0';
                    set(new string(arr));
                    Changed();
                };
                grid.Children.Add(t);
            }
            p.Children.Add(grid);
            return p;
        }
    }
}