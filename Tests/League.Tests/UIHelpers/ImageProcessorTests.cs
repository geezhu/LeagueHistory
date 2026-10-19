using System.Drawing;
using League.UIHelpers;

namespace League.Tests.UIHelpers;

/// <summary>
/// The item and champion icons behind the lists: the size a resize keeps, the dark border a table-sized icon is drawn
/// inside, the padding the table view adds, and the two placeholders. Every fixture is a small bitmap painted in
/// memory here, so no image file from the user's folders is read, and the assertions look at the size plus a few
/// pixels the rule is about rather than at a whole image, because the blended edge values are GDI+'s own.
/// The rules come from the checks in Tests/StatisticsChecks/ImageProcessorChecks.cs.
/// A fully opaque source (the red fixtures) reaches the middle of a scaled result as the exact source colour; a fully
/// transparent source draws nothing at all, except where a table-sized target has already painted its dark border.
/// A target larger than the source is upscaled and stretched over the whole bitmap, and a target of zero or less
/// throws, because the fallback along that path builds a bitmap of the same rejected size.
/// </summary>
public class ImageProcessorTests
{
    /// <summary>The semi-transparent black both resize paths fill the background with, as the source writes it.</summary>
    private static readonly int IconBackground = Color.FromArgb(180, 0, 0, 0).ToArgb();

    /// <summary>What <c>Graphics.Clear</c> leaves behind for a transparent fill: all four channels zero.</summary>
    private static readonly int Nothing = Color.FromArgb(0, 0, 0, 0).ToArgb();

    [Theory]
    [InlineData(20)]
    [InlineData(30)]
    public void ResizeImage_TableSizedTarget_DrawsTheIconInsideTheDarkBorder(int size)
    {
        using var source = Solid(8, 8, Color.Red);
        AssertSource(source, 8, 8, Color.Red);

        using var resized = ImageProcessor.ResizeImage(source, size, size);

        Assert.Equal(size, resized.Width);
        Assert.Equal(size, resized.Height);
        // Both sides are 30 or less, so a dark border is painted first and the icon is drawn from (1,1) inside it.
        Assert.Equal(IconBackground, Pixel(resized, 0, 0));
        Assert.Equal(IconBackground, Pixel(resized, size - 1, size - 1));
        Assert.Equal(Color.Red.ToArgb(), Pixel(resized, size / 2, size / 2));
    }

    [Theory]
    [InlineData(31, 31)]
    [InlineData(30, 31)]
    [InlineData(31, 30)]
    public void ResizeImage_TargetAboveTheTableThreshold_LeavesNoDarkBorder(int width, int height)
    {
        using var source = Solid(8, 8, Color.Red);
        AssertSource(source, 8, 8, Color.Red);

        using var resized = ImageProcessor.ResizeImage(source, width, height);

        Assert.Equal(width, resized.Width);
        Assert.Equal(height, resized.Height);
        // Either side above 30 leaves the table-sized branch, so the icon reaches the corner and no border is there.
        Assert.NotEqual(IconBackground, Pixel(resized, 0, 0));
        Assert.Equal(Color.Red.ToArgb(), Pixel(resized, width / 2, height / 2));
    }

    [Fact]
    public void ResizeImage_LargeTarget_ScaledIconFillsTheBitmap()
    {
        using var source = Solid(8, 8, Color.Red);
        AssertSource(source, 8, 8, Color.Red);

        using var resized = ImageProcessor.ResizeImage(source, 64, 64);

        Assert.Equal(64, resized.Width);
        Assert.Equal(64, resized.Height);
        Assert.Equal(Color.Red.ToArgb(), Pixel(resized, 16, 16));
        Assert.Equal(Color.Red.ToArgb(), Pixel(resized, 32, 32));
        Assert.Equal(Color.Red.ToArgb(), Pixel(resized, 48, 48));
    }

    [Fact]
    public void ResizeImage_TargetLargerAndNonSquare_StretchesTheIconOverTheWholeBitmap()
    {
        using var source = Solid(8, 8, Color.Red);
        using var empty = Solid(8, 8, Color.FromArgb(0, 0, 0, 0));
        AssertSource(source, 8, 8, Color.Red);
        AssertSource(empty, 8, 8, Color.FromArgb(0, 0, 0, 0));

        using var stretched = ImageProcessor.ResizeImage(source, 64, 16);
        using var control = ImageProcessor.ResizeImage(empty, 64, 16);

        Assert.Equal(64, stretched.Width);
        Assert.Equal(16, stretched.Height);
        Assert.Equal(Color.Red.ToArgb(), Pixel(stretched, 32, 8));
        // The control shows that a left edge nothing was drawn on stays empty; the red source covers that edge, so
        // the square icon was stretched to the target rather than letterboxed inside it.
        Assert.Equal(Nothing, Pixel(control, 0, 8));
        Assert.NotEqual(Nothing, Pixel(stretched, 0, 8));
    }

    [Fact]
    public void ResizeImage_BackgroundColourOnALargeTarget_FillsBehindTheIcon()
    {
        using var source = Solid(8, 8, Color.FromArgb(0, 0, 0, 0));
        AssertSource(source, 8, 8, Color.FromArgb(0, 0, 0, 0));

        using var resized = ImageProcessor.ResizeImage(source, 64, 64, Color.Blue);

        Assert.Equal(64, resized.Width);
        Assert.Equal(64, resized.Height);
        // A transparent source draws nothing, so every pixel is the colour the large path cleared the bitmap with.
        Assert.Equal(Color.Blue.ToArgb(), Pixel(resized, 0, 0));
        Assert.Equal(Color.Blue.ToArgb(), Pixel(resized, 32, 32));
        Assert.Equal(Color.Blue.ToArgb(), Pixel(resized, 63, 63));
    }

    [Fact]
    public void ResizeImage_MissingSource_BecomesTheRequestedBackgroundColour()
    {
        using var tinted = ImageProcessor.ResizeImage(null, 40, 30, Color.Blue);

        Assert.Equal(40, tinted.Width);
        Assert.Equal(30, tinted.Height);
        Assert.Equal(Color.Blue.ToArgb(), Pixel(tinted, 0, 0));
        Assert.Equal(Color.Blue.ToArgb(), Pixel(tinted, 39, 29));
    }

    [Fact]
    public void ResizeImage_MissingSourceWithoutAColour_StaysTransparent()
    {
        using var blank = ImageProcessor.ResizeImage(null, 40, 30);

        Assert.Equal(40, blank.Width);
        Assert.Equal(30, blank.Height);
        Assert.Equal(Nothing, Pixel(blank, 0, 0));
        Assert.Equal(Nothing, Pixel(blank, 39, 29));
    }

    [Fact]
    public void ResizeImage_MissingSourceAtATableSize_IsTransparentInsteadOfBordered()
    {
        using var source = Solid(8, 8, Color.FromArgb(0, 0, 0, 0));
        AssertSource(source, 8, 8, Color.FromArgb(0, 0, 0, 0));

        using var fromNull = ImageProcessor.ResizeImage(null, 20, 20);
        using var fromEmpty = ImageProcessor.ResizeImage(source, 20, 20);

        // A null source never reaches the table-sized branch, so it gets no dark border; an empty bitmap does.
        Assert.Equal(Nothing, Pixel(fromNull, 0, 0));
        Assert.Equal(Nothing, Pixel(fromNull, 19, 19));
        Assert.Equal(IconBackground, Pixel(fromEmpty, 0, 0));
    }

    [Fact]
    public void ResizeImage_SinglePixelSource_IsDrawnInsideTheBorder()
    {
        using var source = Solid(1, 1, Color.Red);
        AssertSource(source, 1, 1, Color.Red);

        using var resized = ImageProcessor.ResizeImage(source, 20, 20);

        Assert.Equal(20, resized.Width);
        Assert.Equal(20, resized.Height);
        Assert.Equal(IconBackground, Pixel(resized, 0, 0));
        Assert.Equal(IconBackground, Pixel(resized, 19, 19));
        // The single pixel is blurred by the upscale, so its exact value is GDI+'s; only the border is pinned here.
        Assert.NotEqual(IconBackground, Pixel(resized, 10, 10));
    }

    [Fact]
    public void ResizeImage_FullyTransparentSourceAtALargeTarget_StaysFullyTransparent()
    {
        using var source = Solid(8, 8, Color.FromArgb(0, 0, 0, 0));
        AssertSource(source, 8, 8, Color.FromArgb(0, 0, 0, 0));

        using var resized = ImageProcessor.ResizeImage(source, 64, 64);

        Assert.Equal(64, resized.Width);
        Assert.Equal(64, resized.Height);
        Assert.Equal(Nothing, Pixel(resized, 0, 0));
        Assert.Equal(Nothing, Pixel(resized, 32, 32));
        Assert.Equal(Nothing, Pixel(resized, 63, 63));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-5, 10)]
    [InlineData(40, -5)]
    public void ResizeImage_ZeroOrNegativeTarget_Throws(int width, int height)
    {
        using var source = Solid(8, 8, Color.Red);
        AssertSource(source, 8, 8, Color.Red);

        Assert.Throws<ArgumentException>(() => ImageProcessor.ResizeImage(source, width, height));
    }

    [Fact]
    public void ResizeImageForTableView_TableSizedTarget_PadsTheIconWithTwoPixelsOfBackground()
    {
        using var source = Solid(8, 8, Color.Red);
        AssertSource(source, 8, 8, Color.Red);

        using var icon = ImageProcessor.ResizeImageForTableView(source, 24, 24);

        Assert.Equal(24, icon.Width);
        Assert.Equal(24, icon.Height);
        // The padding is two pixels, so the first two rows and columns stay background and the icon starts at (2,2).
        Assert.Equal(IconBackground, Pixel(icon, 0, 0));
        Assert.Equal(IconBackground, Pixel(icon, 1, 1));
        Assert.NotEqual(IconBackground, Pixel(icon, 2, 2));
        Assert.Equal(Color.Red.ToArgb(), Pixel(icon, 12, 12));
        Assert.Equal(IconBackground, Pixel(icon, 22, 22));
        Assert.Equal(IconBackground, Pixel(icon, 23, 23));
    }

    [Fact]
    public void ResizeImageForTableView_TargetLargerThanTheSource_PadsTheScaledIcon()
    {
        using var source = Solid(8, 8, Color.Red);
        AssertSource(source, 8, 8, Color.Red);

        using var icon = ImageProcessor.ResizeImageForTableView(source, 64, 64);

        Assert.Equal(64, icon.Width);
        Assert.Equal(64, icon.Height);
        Assert.Equal(IconBackground, Pixel(icon, 0, 0));
        Assert.Equal(IconBackground, Pixel(icon, 1, 1));
        Assert.NotEqual(IconBackground, Pixel(icon, 2, 2));
        Assert.Equal(Color.Red.ToArgb(), Pixel(icon, 32, 32));
        Assert.Equal(IconBackground, Pixel(icon, 62, 62));
        Assert.Equal(IconBackground, Pixel(icon, 63, 63));
    }

    [Fact]
    public void ResizeImageForTableView_NonSquareTarget_PadsBothAxes()
    {
        using var source = Solid(8, 8, Color.Red);
        AssertSource(source, 8, 8, Color.Red);

        using var icon = ImageProcessor.ResizeImageForTableView(source, 40, 30);

        Assert.Equal(40, icon.Width);
        Assert.Equal(30, icon.Height);
        Assert.Equal(IconBackground, Pixel(icon, 0, 0));
        Assert.Equal(IconBackground, Pixel(icon, 1, 1));
        Assert.NotEqual(IconBackground, Pixel(icon, 37, 27));
        Assert.Equal(Color.Red.ToArgb(), Pixel(icon, 20, 15));
        Assert.Equal(IconBackground, Pixel(icon, 38, 28));
        Assert.Equal(IconBackground, Pixel(icon, 39, 29));
    }

    [Fact]
    public void ResizeImageForTableView_MissingSource_IsTheBackgroundAlone()
    {
        using var icon = ImageProcessor.ResizeImageForTableView(null, 24, 24);

        Assert.Equal(24, icon.Width);
        Assert.Equal(24, icon.Height);
        Assert.Equal(IconBackground, Pixel(icon, 0, 0));
        Assert.Equal(IconBackground, Pixel(icon, 12, 12));
        Assert.Equal(IconBackground, Pixel(icon, 23, 23));
    }

    [Fact]
    public void ResizeImageForTableView_FullyTransparentSource_IsTheBackgroundAlone()
    {
        using var source = Solid(8, 8, Color.FromArgb(0, 0, 0, 0));
        AssertSource(source, 8, 8, Color.FromArgb(0, 0, 0, 0));

        using var icon = ImageProcessor.ResizeImageForTableView(source, 24, 24);

        // Nothing is drawn, so this path keeps its background flat; the table-sized ResizeImage path does not.
        Assert.Equal(IconBackground, Pixel(icon, 0, 0));
        Assert.Equal(IconBackground, Pixel(icon, 2, 2));
        Assert.Equal(IconBackground, Pixel(icon, 12, 12));
        Assert.Equal(IconBackground, Pixel(icon, 23, 23));
    }

    [Fact]
    public void ResizeImageForTableView_TargetOfExactlyTwiceThePadding_DrawsNoIcon()
    {
        using var source = Solid(8, 8, Color.Red);
        AssertSource(source, 8, 8, Color.Red);

        using var icon = ImageProcessor.ResizeImageForTableView(source, 4, 4);

        Assert.Equal(4, icon.Width);
        Assert.Equal(4, icon.Height);
        // Two pixels of padding on each side leave no room at all, so the whole bitmap is the background.
        Assert.Equal(IconBackground, Pixel(icon, 0, 0));
        Assert.Equal(IconBackground, Pixel(icon, 3, 0));
        Assert.Equal(IconBackground, Pixel(icon, 2, 2));
        Assert.Equal(IconBackground, Pixel(icon, 0, 3));
        Assert.Equal(IconBackground, Pixel(icon, 3, 3));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-5, 10)]
    [InlineData(24, -5)]
    public void ResizeImageForTableView_ZeroOrNegativeTarget_Throws(int width, int height)
    {
        using var source = Solid(8, 8, Color.Red);
        AssertSource(source, 8, 8, Color.Red);

        Assert.Throws<ArgumentException>(() => ImageProcessor.ResizeImageForTableView(source, width, height));
    }

    [Fact]
    public void CreateDefaultImage_RequestedSizeAndColour_AreUsed()
    {
        using var filled = ImageProcessor.CreateDefaultImage(12, 9, Color.Green);

        Assert.Equal(12, filled.Width);
        Assert.Equal(9, filled.Height);
        Assert.Equal(Color.Green.ToArgb(), Pixel(filled, 0, 0));
        Assert.Equal(Color.Green.ToArgb(), Pixel(filled, 6, 4));
        Assert.Equal(Color.Green.ToArgb(), Pixel(filled, 11, 8));
    }

    [Fact]
    public void CreateDefaultImage_TransparentColour_LeavesEveryPixelTransparent()
    {
        using var blank = ImageProcessor.CreateDefaultImage(5, 3, Color.FromArgb(0, 0, 0, 0));

        Assert.Equal(5, blank.Width);
        Assert.Equal(3, blank.Height);
        Assert.Equal(Nothing, Pixel(blank, 0, 0));
        Assert.Equal(Nothing, Pixel(blank, 2, 1));
        Assert.Equal(Nothing, Pixel(blank, 4, 2));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 9)]
    [InlineData(9, 0)]
    [InlineData(-3, -3)]
    public void CreateDefaultImage_ZeroOrNegativeSize_Throws(int width, int height)
    {
        Assert.Throws<ArgumentException>(() => ImageProcessor.CreateDefaultImage(width, height, Color.Green));
    }

    [Fact]
    public void CreateEmptySlotImage_RequestedSize_IsUsed()
    {
        using var slot = ImageProcessor.CreateEmptySlotImage(30, 20);

        Assert.Equal(30, slot.Width);
        Assert.Equal(20, slot.Height);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(15, 10)]
    [InlineData(28, 18)]
    public void CreateEmptySlotImage_Interior_IsTheDimTranslucentFill(int x, int y)
    {
        using var slot = ImageProcessor.CreateEmptySlotImage(30, 20);
        Assert.Equal(30, slot.Width);

        Color interior = PixelChannels(slot, x, y);

        // GDI+ rounds the translucent fill, so the grey is compared with itself and only the alpha is pinned.
        Assert.Equal(30, interior.A);
        Assert.Equal(interior.R, interior.G);
        Assert.Equal(interior.G, interior.B);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(29, 0)]
    [InlineData(0, 19)]
    [InlineData(29, 19)]
    public void CreateEmptySlotImage_Border_IsLightGrayOnEveryEdge(int x, int y)
    {
        using var slot = ImageProcessor.CreateEmptySlotImage(30, 20);

        Assert.Equal(Color.LightGray.ToArgb(), Pixel(slot, x, y));
    }

    [Fact]
    public void CreateEmptySlotImage_SinglePixel_TakesTheFillBecauseTheBorderHasNoRoom()
    {
        using var slot = ImageProcessor.CreateEmptySlotImage(1, 1);
        Assert.Equal(1, slot.Width);
        Assert.Equal(1, slot.Height);

        Color only = PixelChannels(slot, 0, 0);

        // The one-pixel rectangle the border is drawn on has no width, so the fill is what is left.
        Assert.Equal(30, only.A);
        Assert.Equal(only.R, only.G);
        Assert.Equal(only.G, only.B);
        Assert.NotEqual(Color.LightGray.ToArgb(), only.ToArgb());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 9)]
    [InlineData(9, 0)]
    [InlineData(-3, -3)]
    public void CreateEmptySlotImage_ZeroOrNegativeSize_Throws(int width, int height)
    {
        Assert.Throws<ArgumentException>(() => ImageProcessor.CreateEmptySlotImage(width, height));
    }

    /// <summary>Paints a solid bitmap of the given colour; the helpers under test take any <see cref="Image"/>.</summary>
    private static Bitmap Solid(int width, int height, Color colour)
    {
        var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(colour);
        }

        return bitmap;
    }

    /// <summary>
    /// Pins the fixture before the rule is asserted: a source that is not the size and colour the test meant looks
    /// exactly like a resize that handled the source wrongly.
    /// </summary>
    private static void AssertSource(Bitmap source, int width, int height, Color colour)
    {
        Assert.Equal(width, source.Width);
        Assert.Equal(height, source.Height);
        Assert.Equal(colour.ToArgb(), Pixel(source, 0, 0));
        Assert.Equal(colour.ToArgb(), Pixel(source, width - 1, height - 1));
    }

    /// <summary>The ARGB value at one pixel of a result; only the coordinates a rule is about are ever read.</summary>
    private static int Pixel(Image image, int x, int y) => ((Bitmap)image).GetPixel(x, y).ToArgb();

    /// <summary>The channels at one pixel, for the two rules stated per channel rather than as one ARGB value.</summary>
    private static Color PixelChannels(Image image, int x, int y) => ((Bitmap)image).GetPixel(x, y);
}
