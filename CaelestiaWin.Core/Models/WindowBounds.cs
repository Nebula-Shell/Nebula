namespace CaelestiaWin.Core.Models;

public readonly record struct WindowBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public double CenterX => Left + (Width / 2d);

    public double CenterY => Top + (Height / 2d);

    public bool IsEmpty => Width <= 0 || Height <= 0;
}
