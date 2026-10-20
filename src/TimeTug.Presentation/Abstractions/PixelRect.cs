namespace TimeTug.Presentation.Abstractions;

/// <summary>A rectangle in physical (virtual-screen) pixels. Negative coordinates are normal: a monitor to the left of or above the primary one has them.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    /// <summary>True when it has no area (Windows answers an empty rectangle for an icon it cannot place).</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public double CenterX => X + (Width / 2.0);

    public double CenterY => Y + (Height / 2.0);

    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>A size in device-independent pixels (1 DIP is 1/96 inch; one pixel at 100 percent scaling).</summary>
public readonly record struct SizeInDips(double Width, double Height);
