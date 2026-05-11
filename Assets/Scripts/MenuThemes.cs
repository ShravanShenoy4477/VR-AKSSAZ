using UnityEngine;
using TMPro;

/// <summary>
/// Shared UI palettes so clue cards and HUD/help menus stay visually consistent.
/// </summary>
public static class MenuThemes
{
    public static class Typography
    {
        public const FontStyles Header = FontStyles.Bold;
        public const FontStyles Body = FontStyles.Normal;
        public const FontStyles Emphasis = FontStyles.Bold;
    }

    public static class Clue
    {
        public static readonly Color Background = new Color(0.96f, 0.93f, 0.84f, 0.98f);
        public static readonly Color Header = new Color(0.20f, 0.14f, 0.08f, 1.00f);
        public static readonly Color Ink = new Color(0.12f, 0.10f, 0.08f, 1.00f);
        public static readonly Color InkMuted = new Color(0.45f, 0.32f, 0.14f, 0.80f);
        public static readonly Color Sketch = new Color(0.25f, 0.18f, 0.10f, 0.90f);
        public static readonly Color Prompt = new Color(1.00f, 0.92f, 0.40f, 1.00f);
        public static readonly Color Button = new Color(0.25f, 0.18f, 0.10f, 1.00f);
        public static readonly Color ButtonText = new Color(0.92f, 0.87f, 0.75f, 1.00f);

        public static readonly Color Globe = new Color(0.22f, 0.45f, 0.70f, 0.85f);
        public static readonly Color CdOuter = new Color(0.55f, 0.55f, 0.60f, 0.90f);
        public static readonly Color CdInner = new Color(0.30f, 0.30f, 0.35f, 0.85f);
        public static readonly Color CdHole = new Color(0.80f, 0.75f, 0.65f, 1.00f);
    }

    public static class Hud
    {
        public static readonly Color Background = new Color(0.07f, 0.08f, 0.13f, 0.97f);
        public static readonly Color Header = new Color(0.11f, 0.08f, 0.28f, 1.00f);
        public static readonly Color AccentPrimary = new Color(0.50f, 0.25f, 0.90f, 1.00f);
        public static readonly Color AccentSecondary = new Color(0.10f, 0.62f, 0.68f, 1.00f);
        public static readonly Color Warning = new Color(0.85f, 0.18f, 0.18f, 1.00f);
        public static readonly Color TimerGold = new Color(1.00f, 0.84f, 0.18f, 1.00f);
        public static readonly Color TextPrimary = new Color(0.94f, 0.94f, 1.00f, 1.00f);
        public static readonly Color TextSecondary = new Color(0.70f, 0.88f, 1.00f, 1.00f);
        public static readonly Color TextDanger = new Color(1.00f, 0.70f, 0.70f, 1.00f);
    }

    public static class Completion
    {
        public static readonly Color Background = new Color(0.08f, 0.07f, 0.12f, 0.97f);
        public static readonly Color Header = new Color(0.17f, 0.10f, 0.28f, 1.00f);
        public static readonly Color AccentPrimary = new Color(0.92f, 0.76f, 0.30f, 1.00f);
        public static readonly Color AccentSuccess = new Color(0.28f, 0.82f, 0.52f, 1.00f);
        public static readonly Color AccentInfo = new Color(0.35f, 0.62f, 0.95f, 1.00f);
        public static readonly Color AccentDanger = new Color(0.86f, 0.28f, 0.30f, 1.00f);
        public static readonly Color Button = new Color(0.18f, 0.58f, 0.64f, 1.00f);
        public static readonly Color TextPrimary = new Color(0.94f, 0.94f, 1.00f, 1.00f);
        public static readonly Color TextSecondary = new Color(0.76f, 0.85f, 0.97f, 1.00f);
    }
}
