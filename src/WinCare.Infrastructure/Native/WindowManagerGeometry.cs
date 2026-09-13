namespace WinCare.Infrastructure.Native;

using System;

/// <summary>
/// Window placement and relative delta geometry engine (tinywm).
/// Computes tile splits, quadrants, center floats, and interactive move/resize delta transformations.
/// </summary>
public static class WindowManagerGeometry
{
    public sealed record ScreenRect(int X, int Y, int Width, int Height);
    public sealed record WindowRect(int X, int Y, int Width, int Height);

    public enum SplitType
    {
        LeftHalf,
        RightHalf,
        TopHalf,
        BottomHalf
    }

    /// <summary>
    /// Calculates half-screen tiling rectangles.
    /// </summary>
    public static WindowRect CalculateSplit(ScreenRect screen, SplitType splitType)
    {
        ArgumentNullException.ThrowIfNull(screen);

        return splitType switch
        {
            SplitType.LeftHalf => new WindowRect(screen.X, screen.Y, screen.Width / 2, screen.Height),
            SplitType.RightHalf => new WindowRect(screen.X + (screen.Width / 2), screen.Y, screen.Width - (screen.Width / 2), screen.Height),
            SplitType.TopHalf => new WindowRect(screen.X, screen.Y, screen.Width, screen.Height / 2),
            SplitType.BottomHalf => new WindowRect(screen.X, screen.Y + (screen.Height / 2), screen.Width, screen.Height - (screen.Height / 2)),
            _ => new WindowRect(screen.X, screen.Y, screen.Width, screen.Height)
        };
    }

    /// <summary>
    /// Calculates a 2x2 grid quadrant rectangle (0: TopLeft, 1: TopRight, 2: BottomLeft, 3: BottomRight).
    /// </summary>
    public static WindowRect CalculateQuadrant(ScreenRect screen, int quadrantIndex)
    {
        ArgumentNullException.ThrowIfNull(screen);

        int halfW = screen.Width / 2;
        int halfH = screen.Height / 2;
        int remW = screen.Width - halfW;
        int remH = screen.Height - halfH;

        return (quadrantIndex % 4) switch
        {
            0 => new WindowRect(screen.X, screen.Y, halfW, halfH),
            1 => new WindowRect(screen.X + halfW, screen.Y, remW, halfH),
            2 => new WindowRect(screen.X, screen.Y + halfH, halfW, remH),
            3 => new WindowRect(screen.X + halfW, screen.Y + halfH, remW, remH),
            _ => new WindowRect(screen.X, screen.Y, halfW, halfH)
        };
    }

    /// <summary>
    /// Centers a floating window within screen bounds.
    /// </summary>
    public static WindowRect CalculateCentered(ScreenRect screen, int targetWidth, int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(screen);

        int w = Math.Clamp(targetWidth, 100, screen.Width);
        int h = Math.Clamp(targetHeight, 100, screen.Height);
        int x = screen.X + ((screen.Width - w) / 2);
        int y = screen.Y + ((screen.Height - h) / 2);

        return new WindowRect(x, y, w, h);
    }

    /// <summary>
    /// Transforms window coordinates based on mouse delta vectors.
    /// Mode: 1 = Move, 2 = Resize, 3 = Move &amp; Resize.
    /// </summary>
    public static unsafe WindowRect CalculateInteractiveDelta(
        int startX, int startY,
        int currX, int currY,
        int winX, int winY,
        int winW, int winH,
        uint mode)
    {
        int[] rect = new int[4];
        fixed (int* pRect = rect)
        {
            try
            {
                int status = WinCareCoreNative.WinCareCoreCalculateRelativeWindowDelta(
                    startX, startY, currX, currY,
                    winX, winY, winW, winH,
                    mode, pRect);

                if (status == 0)
                {
                    return new WindowRect(rect[0], rect[1], rect[2], rect[3]);
                }
            }
            catch
            {
                // Fallback to managed calculation
            }
        }

        int dx = currX - startX;
        int dy = currY - startY;

        return mode switch
        {
            1 => new WindowRect(winX + dx, winY + dy, Math.Max(1, winW), Math.Max(1, winH)),
            2 => new WindowRect(winX, winY, Math.Max(1, winW + dx), Math.Max(1, winH + dy)),
            3 => new WindowRect(winX + dx, winY + dy, Math.Max(1, winW + dx), Math.Max(1, winH + dy)),
            _ => new WindowRect(winX, winY, winW, winH)
        };
    }
}
