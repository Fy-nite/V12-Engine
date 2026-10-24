using System;

namespace V12.Core.UI.Paper
{
    /// <summary>
    /// Palette for the Paper/Quill UI surfaces (both the engine Canvas HUD and the
    /// host editor chrome). Hosts retint this from their own theme system; keeping it
    /// in the engine assembly means the renderer never has to know about Studio.
    ///
    /// Values are straight (non-premultiplied) linear-ish 0..1 RGBA as used by
    /// <c>Prowl.Vector.Color</c>.
    /// </summary>
    public static class PaperPalette
    {
        public static Prowl.Vector.Color Canvas = FromHex("#17191d");
        public static Prowl.Vector.Color Panel = FromHex("#202329");
        public static Prowl.Vector.Color Raised = FromHex("#292d34");
        public static Prowl.Vector.Color Hover = FromHex("#353b44");
        public static Prowl.Vector.Color Selected = FromHex("#344b5d");
        public static Prowl.Vector.Color Border = FromHex("#393e47");
        public static Prowl.Vector.Color Accent = FromHex("#73b7dc");
        public static Prowl.Vector.Color Text = FromHex("#e2e5e9");
        public static Prowl.Vector.Color Muted = FromHex("#a4aab3");
        public static Prowl.Vector.Color Disabled = FromHex("#737983");
        public static Prowl.Vector.Color Danger = FromHex("#eb6b6b");

        /// <summary>Text/icon colour that reads on top of <see cref="Accent"/> (auto-contrast).</summary>
        public static Prowl.Vector.Color OnAccent = new Prowl.Vector.Color(1f, 1f, 1f, 1f);
        public static Prowl.Vector.Color Success = FromHex("#6fcf97");

        /// <summary>True when the active palette is light (dark text on light surfaces).</summary>
        public static bool IsLight;

        /// <summary>Raised after a host retints the palette so renderers can rebuild cached styles.</summary>
        public static event Action? Changed;

        public static void Set(
            Prowl.Vector.Color canvas, Prowl.Vector.Color panel, Prowl.Vector.Color raised,
            Prowl.Vector.Color hover, Prowl.Vector.Color selected, Prowl.Vector.Color border,
            Prowl.Vector.Color accent, Prowl.Vector.Color text, Prowl.Vector.Color muted,
            Prowl.Vector.Color disabled, bool isLight,
            Prowl.Vector.Color? danger = null, Prowl.Vector.Color? success = null)
        {
            Canvas = canvas; Panel = panel; Raised = raised; Hover = hover; Selected = selected;
            Border = border; Accent = accent; Text = text; Muted = muted; Disabled = disabled;
            IsLight = isLight;
            OnAccent = Luminance(accent) > 0.55f
                ? new Prowl.Vector.Color(0.07f, 0.07f, 0.09f, 1f)
                : new Prowl.Vector.Color(1f, 1f, 1f, 1f);
            if (danger.HasValue) Danger = danger.Value;
            if (success.HasValue) Success = success.Value;
            Changed?.Invoke();
        }

        /// <summary>Parse "#rrggbb" / "#rrggbbaa" into a color. Invalid input returns magenta.</summary>
        public static Prowl.Vector.Color FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return new Prowl.Vector.Color(1f, 0f, 1f, 1f);
            try
            {
                hex = hex.Trim().TrimStart('#');
                if (hex.Length == 6)
                    return new Prowl.Vector.Color(
                        Convert.ToByte(hex.Substring(0, 2), 16) / 255f,
                        Convert.ToByte(hex.Substring(2, 2), 16) / 255f,
                        Convert.ToByte(hex.Substring(4, 2), 16) / 255f,
                        1f);
                if (hex.Length == 8)
                    return new Prowl.Vector.Color(
                        Convert.ToByte(hex.Substring(0, 2), 16) / 255f,
                        Convert.ToByte(hex.Substring(2, 2), 16) / 255f,
                        Convert.ToByte(hex.Substring(4, 2), 16) / 255f,
                        Convert.ToByte(hex.Substring(6, 2), 16) / 255f);
            }
            catch { }
            return new Prowl.Vector.Color(1f, 0f, 1f, 1f);
        }

        /// <summary>Perceptual-ish luminance used to pick a readable on-accent ink.</summary>
        public static float Luminance(Prowl.Vector.Color c)
            => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;

        public static Prowl.Vector.Color WithAlpha(Prowl.Vector.Color c, float a)
            => new Prowl.Vector.Color(c.R, c.G, c.B, a);
    }
}

