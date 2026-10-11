using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Gdi;
using AdvancedInstallerMcp.Lint;
using Rect = AdvancedInstallerMcp.Gdi.NativeMethods.Rect;

namespace AdvancedInstallerMcp.Render;

public sealed record RenderedFont(string Face, int Size, bool Bold, string ActualFace, bool Substituted);

// What the picture cannot show by itself (ADR 0006). Conditions with a null Result were not evaluated.
public sealed record RenderReport(
    string Dialog,
    string Title,
    int Width,
    int Height,
    int Dpi,
    IReadOnlyList<RenderedFont> Fonts,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<ConditionOutcome> Conditions,
    IReadOnlyList<string> UnresolvedProperties,
    IReadOnlyList<string> RunTimeText,
    IReadOnlyList<string> Problems);

public sealed record RenderedDialog(byte[] Png, RenderReport Report);

// Draws a dialog's client area as msiexec would show it when the dialog opens (ADR 0006): text with the same GDI
// DrawText and fonts as TextMeasurer, so a label cut off here is the label lint_dialog reports; buttons, check boxes,
// radio buttons and edit boxes from the visual style; pictures from the Binary table.
public static class DialogRenderer
{
    private static readonly HashSet<string> PictureTypes = new(StringComparer.Ordinal) { "Bitmap", "Icon" };

    private static readonly HashSet<string> EditTypes = new(StringComparer.Ordinal) { "Edit", "PathEdit", "MaskedEdit" };

    // Drawn before everything else, so what sits on top of them stays visible as it does on screen.
    private static readonly HashSet<string> BackgroundTypes = new(StringComparer.Ordinal) { "Bitmap", "Icon", "GroupBox", "Line" };

    public static RenderedDialog Render(
        DialogPackage package, Dialog dialog, int dpi, IReadOnlyDictionary<string, string> callerProperties)
    {
        var state = DialogState.Evaluate(package, dialog, callerProperties);
        using var painter = new Painter(package, state, dpi);

        var width = InstallerUnits.ToPixels(dialog.Width, dpi);
        var height = InstallerUnits.ToPixels(dialog.Height, dpi);

        // No alpha channel: GDI does not write one, so text drawn on an alpha surface can come out transparent.
        using var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(ColorTranslator.FromWin32((int)NativeMethods.GetSysColor(NativeMethods.ColorBtnFace)));

            var drawn = state.Controls
                .Where(c => c.Visible && c.Control.Width > 0 && c.Control.Height > 0)
                .OrderBy(c => BackgroundTypes.Contains(c.Control.Type) ? 0 : 1)
                .ToList();

            foreach (var control in drawn.Where(c => PictureTypes.Contains(c.Control.Type)))
            {
                painter.Picture(graphics, control.Control);
            }

            var dc = graphics.GetHdc();
            try
            {
                foreach (var control in drawn.Where(c => !PictureTypes.Contains(c.Control.Type)))
                {
                    painter.Control(dc, control);
                }
            }
            finally
            {
                graphics.ReleaseHdc(dc);
            }

            painter.ButtonPictures(graphics);
        }

        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);

        var (title, titleUnresolved) = FormattedText.ResolveForDisplay(dialog.Title, state.Properties);
        painter.Unresolved.UnionWith(titleUnresolved);

        var report = new RenderReport(
            dialog.Name,
            title,
            width,
            height,
            dpi,
            painter.Fonts,
            painter.Placeholders,
            state.Conditions,
            painter.Unresolved.Order(StringComparer.Ordinal).ToList(),
            dialog.Controls.Where(c => c.TextFromEvent).Select(c => c.Name).Order(StringComparer.Ordinal).ToList(),
            painter.Problems);

        return new RenderedDialog(png.ToArray(), report);
    }

    private sealed class Painter(DialogPackage package, DialogState state, int dpi) : IDisposable
    {
        private readonly Dictionary<(string Face, int Size, TextStyleBits Bits), FontHandle> _fonts = [];
        private readonly List<(Control Control, Rectangle Area)> _buttonPictures = [];
        private nint _buttonTheme;
        private nint _editTheme;
        private nint _comboTheme;

        public List<RenderedFont> Fonts { get; } = [];

        public List<string> Placeholders { get; } = [];

        public List<string> Problems { get; } = [];

        public HashSet<string> Unresolved { get; } = new(StringComparer.Ordinal);

        private nint ButtonTheme => _buttonTheme = _buttonTheme != 0 ? _buttonTheme : NativeMethods.OpenThemeDataForDpi(0, "Button", (uint)dpi);

        private nint EditTheme => _editTheme = _editTheme != 0 ? _editTheme : NativeMethods.OpenThemeDataForDpi(0, "Edit", (uint)dpi);

        private nint ComboTheme => _comboTheme = _comboTheme != 0 ? _comboTheme : NativeMethods.OpenThemeDataForDpi(0, "Combobox", (uint)dpi);

        public void Dispose()
        {
            foreach (var font in _fonts.Values)
            {
                font.Dispose();
            }

            foreach (var theme in new[] { _buttonTheme, _editTheme, _comboTheme }.Where(t => t != 0))
            {
                NativeMethods.CloseThemeData(theme);
            }
        }

        public void Control(nint dc, ControlState state)
        {
            var control = state.Control;
            var rect = ToRect(control.X, control.Y, control.Width, control.Height);

            switch (control.Type)
            {
                case "Text":
                    Text(dc, control, rect, state.Enabled);
                    break;
                case "PushButton":
                case "CheckBox" when control.Has(ControlAttributes.PushLike):
                    PushButton(dc, control, rect, state.Enabled);
                    break;
                case "CheckBox":
                    var value = PropertyValue(control.Property);
                    Choice(dc, NativeMethods.BpCheckBox, value.Length > 0, state.Enabled, control.Text, rect);
                    break;
                case "RadioButtonGroup":
                    RadioButtons(dc, control, rect, state.Enabled);
                    break;
                case "GroupBox":
                    GroupBox(dc, control.Text, rect, state.Enabled);
                    break;
                case "Line":
                    NativeMethods.DrawEdge(dc, ref rect, NativeMethods.EdgeEtched, NativeMethods.BfTop);
                    break;
                case "ComboBox":
                    Combo(dc, control, rect, state.Enabled);
                    break;
                case var type when EditTypes.Contains(type):
                    Edit(dc, control, rect, state.Enabled);
                    break;
                default:
                    Placeholder(dc, control, rect);
                    break;
            }
        }

        // Bitmap and Icon controls. Text names the Binary row, directly or through a property.
        public void Picture(Graphics graphics, Control control)
        {
            var area = Rectangle.FromLTRB(
                InstallerUnits.ToPixels(control.X, dpi), InstallerUnits.ToPixels(control.Y, dpi),
                InstallerUnits.ToPixels(control.X + control.Width, dpi), InstallerUnits.ToPixels(control.Y + control.Height, dpi));
            DrawPicture(graphics, control, area);
        }

        // Pictures inside buttons go on top of the themed button, after the HDC is released.
        public void ButtonPictures(Graphics graphics)
        {
            foreach (var (control, area) in _buttonPictures)
            {
                var inner = Rectangle.Inflate(area, -Scale(4), -Scale(4));
                DrawPicture(graphics, control, inner, fit: true);
            }
        }

        private void DrawPicture(Graphics graphics, Control control, Rectangle area, bool fit = false)
        {
            // A picture button's Text can carry a font prefix too ("[ManufacturerFont][InfoIconMsi]").
            var (name, unresolved) = Display(control.Text);
            var data = unresolved.Count > 0 || name.Length == 0 ? null : package.ReadBinary(name);
            if (data is null)
            {
                Problems.Add(unresolved.Count > 0
                    ? $"{control.Name}: its picture is named by [{unresolved[0]}], which the package does not set."
                    : $"{control.Name}: no Binary row '{name}' for its picture.");
                return;
            }

            try
            {
                using var stream = new MemoryStream(data);
                using var image = IsIcon(data) ? IconImage(stream, control, area) : Image.FromStream(stream);
                var target = control.Has(ControlAttributes.FixedSize) || fit ? Centered(image.Size, area, shrinkOnly: !fit) : area;
                graphics.DrawImage(image, target);
            }
            catch (Exception e) when (e is ArgumentException or ExternalException or OutOfMemoryException)
            {
                Problems.Add($"{control.Name}: the picture in Binary row '{name}' could not be decoded.");
            }
        }

        private static bool IsIcon(byte[] data) => data.Length > 4 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0;

        // IconSize16/32/48 pick the image of the icon; without them the control's size does.
        private Image IconImage(Stream stream, Control control, Rectangle area)
        {
            var sizeBits = control.Attributes & ControlAttributes.IconSize48;
            var size = sizeBits switch
            {
                ControlAttributes.IconSize16 => Scale(16),
                ControlAttributes.IconSize32 => Scale(32),
                ControlAttributes.IconSize48 => Scale(48),
                _ => Math.Min(area.Width, area.Height),
            };

            using var icon = new Icon(stream, size, size);
            return icon.ToBitmap();
        }

        private static Rectangle Centered(Size image, Rectangle area, bool shrinkOnly)
        {
            var scale = Math.Min((double)area.Width / image.Width, (double)area.Height / image.Height);
            if (shrinkOnly)
            {
                scale = Math.Min(1, scale);
            }

            var width = (int)Math.Round(image.Width * scale);
            var height = (int)Math.Round(image.Height * scale);
            return new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
        }

        private void Text(nint dc, Control control, Rect rect, bool enabled)
        {
            if (control.Has(ControlAttributes.Sunken))
            {
                NativeMethods.DrawEdge(dc, ref rect, NativeMethods.EdgeSunken, NativeMethods.BfRect);
                rect = Inset(rect, Scale(DialogLinter.SunkenBorder));
            }
            else if (!control.Has(ControlAttributes.Transparent))
            {
                Fill(dc, rect, NativeMethods.ColorBtnFace);
            }

            var format = control.Has(ControlAttributes.NoWrap)
                ? NativeMethods.DtSingleLine | NativeMethods.DtEndEllipsis
                : NativeMethods.DtWordBreak | NativeMethods.DtEditControl | NativeMethods.DtEndEllipsis;
            format |= control.Has(ControlAttributes.NoPrefix) ? NativeMethods.DtNoPrefix : NativeMethods.DtHidePrefix;
            if (control.Has(ControlAttributes.RightAligned))
            {
                format |= NativeMethods.DtRight;
            }

            DrawLabel(dc, control.Text, rect, format, enabled, styleColor: true);
        }

        private void PushButton(nint dc, Control control, Rect rect, bool enabled)
        {
            var isDefault = string.Equals(control.Name, state.DefaultControl, StringComparison.Ordinal);
            var part = !enabled ? NativeMethods.PbsDisabled : isDefault ? NativeMethods.PbsDefaulted : NativeMethods.PbsNormal;
            NativeMethods.DrawThemeBackground(ButtonTheme, dc, NativeMethods.BpPushButton, part, ref rect, 0);

            if (control.Has(ControlAttributes.Bitmap) || control.Has(ControlAttributes.Icon))
            {
                _buttonPictures.Add((control, Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)));
                return;
            }

            var format = NativeMethods.DtVCenter | NativeMethods.DtSingleLine | NativeMethods.DtHidePrefix;
            if (control.Has(ControlAttributes.ElevationShield))
            {
                // Shield and label are centred together, the label right after the shield.
                DrawLabel(dc, control.Text, Shield(dc, control, rect), format, enabled);
            }
            else
            {
                DrawLabel(dc, control.Text, rect, format | NativeMethods.DtCenter, enabled);
            }
        }

        // The UAC shield left of the label; the pair stays centred like on a real button.
        private Rect Shield(nint dc, Control control, Rect rect)
        {
            using var icon = SystemIcons.GetStockIcon(StockIconId.Shield, StockIconOptions.SmallIcon);
            var size = Scale(16);
            var gap = Scale(4);
            var (text, _) = Display(control.Text);
            var textWidth = MeasureWidth(dc, control.Text, text);
            var left = rect.Left + Math.Max(0, (rect.Right - rect.Left - size - gap - textWidth) / 2);
            var top = rect.Top + (rect.Bottom - rect.Top - size) / 2;
            NativeMethods.DrawIconEx(dc, left, top, icon.Handle, size, size, 0, 0, NativeMethods.DiNormal);
            return rect with { Left = left + size + gap, Right = Math.Max(left + size + gap, rect.Right) };
        }

        private void RadioButtons(nint dc, Control control, Rect rect, bool enabled)
        {
            // The group paints its whole rectangle, which shows as a panel on a background picture.
            Fill(dc, rect, NativeMethods.ColorBtnFace);
            if (control.Has(ControlAttributes.HasBorder))
            {
                GroupBox(dc, control.Text, rect, enabled);
            }

            if (control.Property is null)
            {
                return;
            }

            var selected = PropertyValue(control.Property);
            foreach (var button in package.RadioButtons(control.Property))
            {
                var buttonRect = ToRect(control.X + button.X, control.Y + button.Y, button.Width, button.Height);
                Choice(dc, NativeMethods.BpRadioButton, button.Value == selected, enabled, button.Text, buttonRect);
            }
        }

        // A check box or radio button: the glyph from the theme, the label beside it, centred vertically and cut at the
        // control's edges like the real control (calibration: the text area is the width minus 16 px at 96 DPI).
        private void Choice(nint dc, int part, bool isChecked, bool enabled, string text, Rect rect)
        {
            var glyphState = (isChecked, enabled) switch
            {
                (false, true) => NativeMethods.ButtonUncheckedNormal,
                (false, false) => NativeMethods.ButtonUncheckedDisabled,
                (true, true) => NativeMethods.ButtonCheckedNormal,
                (true, false) => NativeMethods.ButtonCheckedDisabled,
            };

            NativeMethods.GetThemePartSize(ButtonTheme, dc, part, glyphState, 0, NativeMethods.TsDraw, out var glyph);
            var height = rect.Bottom - rect.Top;
            var glyphTop = rect.Top + (height - glyph.Cy) / 2;
            var glyphRect = new Rect { Left = rect.Left, Top = glyphTop, Right = rect.Left + glyph.Cx, Bottom = glyphTop + glyph.Cy };
            NativeMethods.DrawThemeBackground(ButtonTheme, dc, part, glyphState, ref glyphRect, 0);

            var label = rect with { Left = rect.Left + Scale(DialogLinter.CheckBoxTextOffset) };
            if (label.Right <= label.Left)
            {
                return;
            }

            var saved = NativeMethods.SaveDC(dc);
            try
            {
                NativeMethods.IntersectClipRect(dc, label.Left, label.Top, label.Right, label.Bottom);
                var (display, _) = Display(text);
                var textHeight = MeasureHeight(dc, text, display, label.Right - label.Left);
                var top = label.Top + (label.Bottom - label.Top - textHeight) / 2;
                DrawLabel(dc, text, label with { Top = top, Bottom = top + textHeight },
                    NativeMethods.DtWordBreak | NativeMethods.DtHidePrefix, enabled, clip: false);
            }
            finally
            {
                NativeMethods.RestoreDC(dc, saved);
            }
        }

        private void GroupBox(nint dc, string text, Rect rect, bool enabled)
        {
            var (display, _) = Display(text);
            var lineHeight = LineHeight(dc, text);
            var frame = rect with { Top = rect.Top + lineHeight / 2 };
            NativeMethods.DrawThemeBackground(ButtonTheme, dc, NativeMethods.BpGroupBox, 1, ref frame, 0);
            if (display.Length == 0)
            {
                return;
            }

            var textLeft = rect.Left + Scale(8);
            var textWidth = Math.Min(MeasureWidth(dc, text, display) + Scale(4), rect.Right - textLeft);
            var caption = new Rect { Left = textLeft, Top = rect.Top, Right = textLeft + textWidth, Bottom = rect.Top + lineHeight };
            Fill(dc, caption, NativeMethods.ColorBtnFace);
            DrawLabel(dc, text, caption with { Left = caption.Left + Scale(2) },
                NativeMethods.DtSingleLine | NativeMethods.DtHidePrefix, enabled);
        }

        private void Edit(nint dc, Control control, Rect rect, bool enabled)
        {
            NativeMethods.DrawThemeBackground(EditTheme, dc, NativeMethods.EpEditText,
                enabled ? NativeMethods.EtsNormal : NativeMethods.EtsDisabled, ref rect, 0);

            var value = PropertyValue(control.Property);
            if (control.Has(ControlAttributes.Password))
            {
                value = new string('●', value.Length);
            }

            var multiLine = control.Type == "Edit" && control.Has(ControlAttributes.Multiline);
            var format = NativeMethods.DtNoPrefix | (multiLine
                ? NativeMethods.DtWordBreak | NativeMethods.DtEditControl
                : NativeMethods.DtSingleLine | NativeMethods.DtVCenter);
            DrawValue(dc, value, Inset(rect, Scale(3)), format, enabled);
        }

        private void Combo(nint dc, Control control, Rect rect, bool enabled)
        {
            rect.Bottom = Math.Min(rect.Bottom, rect.Top + LineHeight(dc, "") + Scale(LayoutRules.ComboBoxPadding));
            NativeMethods.DrawThemeBackground(EditTheme, dc, NativeMethods.EpEditText,
                enabled ? NativeMethods.EtsNormal : NativeMethods.EtsDisabled, ref rect, 0);

            var arrowWidth = Scale(17);
            var arrow = rect with { Left = Math.Max(rect.Left, rect.Right - arrowWidth - 1), Top = rect.Top + 1, Bottom = rect.Bottom - 1, Right = rect.Right - 1 };
            NativeMethods.DrawThemeBackground(ComboTheme, dc, NativeMethods.CpDropDownButtonRight, NativeMethods.CbxsNormal, ref arrow, 0);

            var text = Inset(rect with { Right = arrow.Left }, Scale(3));
            DrawValue(dc, PropertyValue(control.Property), text,
                NativeMethods.DtNoPrefix | NativeMethods.DtSingleLine | NativeMethods.DtVCenter | NativeMethods.DtEndEllipsis, enabled);
        }

        // Controls whose content comes from the install (feature trees, disk lists, progress, billboards): the frame is
        // right, the inside says what it is, so nobody takes it for the real content.
        private void Placeholder(nint dc, Control control, Rect rect)
        {
            if (LayoutRules.IsComboType(control.Type))
            {
                rect.Bottom = Math.Min(rect.Bottom, rect.Top + LineHeight(dc, "") + Scale(LayoutRules.ComboBoxPadding));
            }

            Fill(dc, rect, NativeMethods.ColorWindow);
            NativeMethods.DrawEdge(dc, ref rect, NativeMethods.EdgeSunken, NativeMethods.BfRect);
            Placeholders.Add($"{control.Name} ({control.Type})");

            DrawValue(dc, control.Type, Inset(rect, Scale(2)),
                NativeMethods.DtCenter | NativeMethods.DtVCenter | NativeMethods.DtSingleLine | NativeMethods.DtEndEllipsis,
                enabled: false);
        }

        // Control text: a {\Style} prefix picks the font, [Property] references resolve or stay visible. The style's
        // colour applies to Text controls only; buttons, check boxes and radio buttons draw their labels in the system
        // colour (Advanced Installer's white "ImageButtonLight" style shows black on a real button).
        private void DrawLabel(
            nint dc, string rawText, Rect rect, uint format, bool enabled, bool clip = true, bool styleColor = false)
        {
            var parsed = ControlText.Parse(rawText, state.Properties);
            var (text, unresolved) = FormattedText.ResolveForDisplay(parsed.DisplayText, state.Properties);
            Unresolved.UnionWith(unresolved);
            if (text.Length == 0)
            {
                return;
            }

            var style = parsed.StyleName is { } name ? package.TextStyles.GetValueOrDefault(name) : DefaultStyle();
            var color = !enabled
                ? NativeMethods.GetSysColor(NativeMethods.ColorGrayText)
                : styleColor && style?.Color is { } textColor
                    ? (uint)textColor
                    : NativeMethods.GetSysColor(NativeMethods.ColorWindowText);

            Draw(dc, FontFor(parsed.StyleName), text, rect, format, color, clip);
        }

        // Property values in edit boxes and placeholder captions: no style prefix, no references, default font.
        private void DrawValue(nint dc, string text, Rect rect, uint format, bool enabled)
        {
            if (text.Length == 0)
            {
                return;
            }

            var color = NativeMethods.GetSysColor(enabled ? NativeMethods.ColorWindowText : NativeMethods.ColorGrayText);
            Draw(dc, FontFor(null), text, rect, format, color, clip: true);
        }

        private static void Draw(nint dc, FontHandle font, string text, Rect rect, uint format, uint color, bool clip)
        {
            var previous = NativeMethods.SelectObject(dc, font.DangerousGetHandle());
            NativeMethods.SetBkMode(dc, NativeMethods.Transparent);
            NativeMethods.SetTextColor(dc, color);
            if (!clip)
            {
                format |= 0x00000100; // DT_NOCLIP: the caller has set a clip region
            }

            NativeMethods.DrawText(dc, text, text.Length, ref rect, format);
            NativeMethods.SelectObject(dc, previous);
        }

        private int MeasureWidth(nint dc, string rawText, string display)
        {
            var rect = new Rect { Right = 10_000 };
            MeasureInto(dc, rawText, display, ref rect, NativeMethods.DtSingleLine | NativeMethods.DtHidePrefix);
            return rect.Right - rect.Left;
        }

        private int MeasureHeight(nint dc, string rawText, string display, int width)
        {
            var rect = new Rect { Right = width };
            MeasureInto(dc, rawText, display, ref rect, NativeMethods.DtWordBreak | NativeMethods.DtHidePrefix);
            return rect.Bottom - rect.Top;
        }

        private int LineHeight(nint dc, string rawText)
        {
            var rect = new Rect { Right = 10_000 };
            MeasureInto(dc, rawText, "x", ref rect, NativeMethods.DtSingleLine);
            return rect.Bottom - rect.Top;
        }

        private void MeasureInto(nint dc, string rawText, string display, ref Rect rect, uint format)
        {
            var font = FontFor(ControlText.Parse(rawText, state.Properties).StyleName);
            var previous = NativeMethods.SelectObject(dc, font.DangerousGetHandle());
            NativeMethods.DrawText(dc, display.Length == 0 ? "x" : display, Math.Max(1, display.Length), ref rect,
                format | NativeMethods.DtCalcRect);
            NativeMethods.SelectObject(dc, previous);
        }

        private (string Text, IReadOnlyList<string> Unresolved) Display(string rawText) =>
            FormattedText.ResolveForDisplay(ControlText.Parse(rawText, state.Properties).DisplayText, state.Properties);

        private FontHandle FontFor(string? styleName)
        {
            var font = FontResolver.Resolve(styleName, package.TextStyles, package.DefaultUIFont);
            var key = (font.FaceName, font.Size, font.Bits);
            if (_fonts.TryGetValue(key, out var handle))
            {
                return handle;
            }

            handle = GdiFonts.Create(font, dpi);
            _fonts[key] = handle;

            var actual = TextMeasurer.Measure("x", font, dpi, 100, MeasureOptions.None).FaceName;
            Fonts.Add(new RenderedFont(font.FaceName, font.Size, font.Bits.HasFlag(TextStyleBits.Bold), actual,
                !string.Equals(actual, font.FaceName, StringComparison.OrdinalIgnoreCase)));
            return handle;
        }

        private TextStyle? DefaultStyle() =>
            package.DefaultUIFont is { } name ? package.TextStyles.GetValueOrDefault(name) : null;

        private string PropertyValue(string? property) =>
            property is null ? "" : state.Properties.GetValueOrDefault(property, "");

        private Rect ToRect(int x, int y, int width, int height) => new()
        {
            Left = InstallerUnits.ToPixels(x, dpi),
            Top = InstallerUnits.ToPixels(y, dpi),
            Right = InstallerUnits.ToPixels(x + width, dpi),
            Bottom = InstallerUnits.ToPixels(y + height, dpi),
        };

        private int Scale(int pixelsAt96) => DialogLinter.Scale(pixelsAt96, dpi);

        private static Rect Inset(Rect rect, int by) =>
            new() { Left = rect.Left + by, Top = rect.Top + by, Right = rect.Right - by, Bottom = rect.Bottom - by };

        private static void Fill(nint dc, Rect rect, int sysColor)
        {
            var brush = NativeMethods.CreateSolidBrush(NativeMethods.GetSysColor(sysColor));
            NativeMethods.FillRect(dc, ref rect, brush);
            NativeMethods.DeleteObject(brush);
        }
    }
}
