using System.Reflection;
using static StatisticsCheckSupport;

/// <summary>
/// The augment icon tint. The client sends monochrome masks, so the rarity decides the colour and the renderer must
/// always hand back its own image, leaving the cached one untouched.
/// </summary>
internal static class AugmentIconRendererChecks
{
    public static void Run()
    {
        CopiesWithoutRarity();
        RarityTints();
        Console.WriteLine("PASS: augment icon copying without a rarity, rarity tints and the independence of both.");
    }

    private static void CopiesWithoutRarity()
    {
        using var source = Solid(24, 24, Color.White);

        using var plain = Render(source, null);
        Check(plain.Width == 24 && plain.Height == 24, "An augment without a rarity keeps its size");
        Check(!ReferenceEquals(plain, source), "An augment without a rarity is still a copy");
        Check(
            plain.GetPixel(12, 12).ToArgb() == Color.White.ToArgb(),
            "A copy without a rarity keeps the original colours"
        );

        using var unknown = Render(source, "ksomething");
        Check(
            unknown.GetPixel(12, 12).ToArgb() == Color.White.ToArgb(),
            "A rarity the renderer does not know is left untinted"
        );

        using var blank = Render(source, "   ");
        Check(blank.GetPixel(12, 12).ToArgb() == Color.White.ToArgb(), "A blank rarity is left untinted");
    }

    private static void RarityTints()
    {
        using var source = Solid(24, 24, Color.White);

        Check(IsNear(Render(source, "ksilver"), 112, 128, 144), "A silver augment is tinted silver");
        Check(IsNear(Render(source, "KGOLD"), 190, 140, 50), "A gold augment is tinted gold, whatever the case");
        Check(IsNear(Render(source, " kprismatic "), 156, 96, 214), "A prismatic augment is tinted violet");

        using var tinted = Render(source, "ksilver");
        Check(source.GetPixel(12, 12).ToArgb() == Color.White.ToArgb(), "The cached source image is never changed");

        var before = source.GetPixel(12, 12);
        tinted.SetPixel(12, 12, Color.Black);
        Check(
            source.GetPixel(12, 12).ToArgb() == before.ToArgb(),
            "The returned image owns its pixels, so painting on it cannot reach the cache"
        );
    }

    /// <summary>The tint sits over a fully lit pixel, so the middle of a white mask reads as the rarity colour.</summary>
    private static bool IsNear(Bitmap image, int r, int g, int b)
    {
        using (image)
        {
            Color pixel = image.GetPixel(image.Width / 2, image.Height / 2);
            return Math.Abs(pixel.R - r) <= 3 && Math.Abs(pixel.G - g) <= 3 && Math.Abs(pixel.B - b) <= 3;
        }
    }

    private static Bitmap Render(Image source, string? rarity) => (Bitmap)RenderMethod.Invoke(null, [source, rarity])!;

    private static Bitmap Solid(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private static readonly MethodInfo RenderMethod = typeof(League.FormMain)
        .Assembly.GetType("League.UIHelpers.AugmentIconRenderer")!
        .GetMethod("Render", BindingFlags.Public | BindingFlags.Static)!;
}
