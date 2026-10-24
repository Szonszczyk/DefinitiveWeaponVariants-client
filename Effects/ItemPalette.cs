namespace DefinitiveWeaponVariants.Client.Effects
{
    internal readonly struct ItemPalette
    {
        internal readonly RingColor First, Second;
        internal ItemPalette(RingColor first, RingColor second) { First = first; Second = second; }

        // Straight-alpha gradient. Opacity changes tint strength, never the CPU's transparency.
        internal void Sample(float position, float opacity, out float r, out float g, out float b, out float a)
        {
            position = System.Math.Max(0f, System.Math.Min(1f, position));
            r = (First.R + (Second.R - First.R) * position) / 255f;
            g = (First.G + (Second.G - First.G) * position) / 255f;
            b = (First.B + (Second.B - First.B) * position) / 255f;
            a = (First.A + (Second.A - First.A) * position) / 255f * opacity;
        }
    }
}
