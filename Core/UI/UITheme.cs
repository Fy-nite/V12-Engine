namespace V12.Core.UI
{
    /// <summary>
    /// Simple, frontend-agnostic UI theme representation. Colors are stored as floats
    /// so adapters can convert to their native color types (e.g. UnityEngine.Color).
    /// </summary>
    public class UITheme
    {
        public struct ColorData
        {
            public float R;
            public float G;
            public float B;
            public float A;

            public ColorData(float r, float g, float b, float a = 1f)
            {
                R = r; G = g; B = b; A = a;
            }
        }

        // Panel / background color
        public ColorData PanelColor { get; set; } = new ColorData(0.12f, 0.12f, 0.15f, 0.9f);
        // Label / headline color
        public ColorData LabelColor { get; set; } = new ColorData(1f, 1f, 1f, 1f);
        // Input text color
        public ColorData InputTextColor { get; set; } = new ColorData(1f, 1f, 1f, 1f);
        // Accent color for buttons, sliders
        public ColorData AccentColor { get; set; } = new ColorData(0.55f, 0.8f, 1f, 1f);

        // Font sizes (frontend can interpret these in pixels or points)
        public float LabelFontSize { get; set; } = 14f;
        public float InputFontSize { get; set; } = 12f;

        public UITheme() { }
    }
}

