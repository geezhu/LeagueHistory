using League.UIHelpers;
using static StatisticsCheckSupport;

/// <summary>
/// The image helpers behind the item and champion icons. They run on GDI+ only, so the checks look at the size
/// and at a few pixels of each result instead of comparing whole images.
/// </summary>
internal static class ImageProcessorChecks
{
    public static void Run()
    {
        ResizedIcons();
        TableViewIcons();
        Placeholders();
        Console.WriteLine(
            "PASS: icon resizing with and without a background, table view icons and placeholder images."
        );
    }

    private static void ResizedIcons()
    {
        using var source = Solid(8, 8, Color.Red);

        using var small = ImageProcessor.ResizeImage(source, 20, 20);
        Check(small.Width == 20 && small.Height == 20, "A table-sized icon keeps the requested size");
        Check(
            Pixel(small, 0, 0) == Color.FromArgb(180, 0, 0, 0).ToArgb(),
            "A small icon is drawn inside a dark border"
        );
        Check(Pixel(small, 10, 10) == Color.Red.ToArgb(), "The icon itself is drawn over the border");

        using var large = ImageProcessor.ResizeImage(source, 64, 64);
        Check(large.Width == 64 && large.Height == 64, "A large icon keeps the requested size");
        Check(Pixel(large, 32, 32) == Color.Red.ToArgb(), "A large icon is scaled to fill its bitmap");

        using var tinted = ImageProcessor.ResizeImage(null!, 40, 30, Color.Blue);
        Check(
            tinted.Width == 40 && Pixel(tinted, 0, 0) == Color.Blue.ToArgb(),
            "A missing icon becomes the requested background colour"
        );

        using var blank = ImageProcessor.ResizeImage(null!, 40, 30);
        Check(((Bitmap)blank).GetPixel(0, 0).A == 0, "A missing icon without a colour stays transparent");
    }

    private static void TableViewIcons()
    {
        using var source = Solid(8, 8, Color.Red);

        using var icon = ImageProcessor.ResizeImageForTableView(source, 24, 24);
        Check(icon.Width == 24 && icon.Height == 24, "A table view icon keeps the requested size");
        Check(
            Pixel(icon, 0, 0) == Color.FromArgb(180, 0, 0, 0).ToArgb(),
            "A table view icon is padded with the semi-transparent background"
        );
        Check(Pixel(icon, 12, 12) == Color.Red.ToArgb(), "The table view icon itself is drawn inside the padding");

        using var missing = ImageProcessor.ResizeImageForTableView(null!, 24, 24);
        Check(
            Pixel(missing, 12, 12) == Color.FromArgb(180, 0, 0, 0).ToArgb(),
            "A missing table view icon is the background alone"
        );
    }

    private static void Placeholders()
    {
        using var filled = ImageProcessor.CreateDefaultImage(12, 9, Color.Green);
        Check(
            filled.Width == 12 && filled.Height == 9 && Pixel(filled, 6, 4) == Color.Green.ToArgb(),
            "A default image is the requested colour and size"
        );

        using var slot = ImageProcessor.CreateEmptySlotImage(30, 20);
        Check(slot.Width == 30 && slot.Height == 20, "An empty slot keeps the requested size");
        // GDI+ rounds the translucent fill, so the grey is compared with itself rather than against a fixed value.
        var interior = ((Bitmap)slot).GetPixel(15, 10);
        Check(
            interior.A == 30 && interior.R == interior.G && interior.G == interior.B,
            "An empty slot is filled with the dim colour"
        );
        Check(Pixel(slot, 0, 0) != Pixel(slot, 15, 10), "An empty slot has a visible border");
    }

    private static Bitmap Solid(int width, int height, Color color) =>
        (Bitmap)ImageProcessor.CreateDefaultImage(width, height, color);

    private static int Pixel(Image image, int x, int y) => ((Bitmap)image).GetPixel(x, y).ToArgb();
}
