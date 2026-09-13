namespace WinCare.Infrastructure.Native;

using System;
using System.Collections.Generic;

/// <summary>
/// Dynamic workspace window tiling and layout algorithm engine (awesome-wm).
/// Computes non-overlapping window tile geometries across master-and-stack, fair grid, and spiral layouts.
/// </summary>
public static class WorkspaceTilingEngine
{
    public sealed record ScreenArea(int X, int Y, int Width, int Height);
    public sealed record WindowTile(int Index, int X, int Y, int Width, int Height);

    /// <summary>
    /// Computes Master-and-Stack tiling geometry: 1 primary window on the left, remaining (N-1) stacked on the right.
    /// </summary>
    public static IReadOnlyList<WindowTile> CalculateMasterAndStack(
        ScreenArea screen,
        int windowCount,
        double masterRatio = 0.55,
        int gap = 8)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var tiles = new List<WindowTile>();
        if (windowCount <= 0) return tiles;

        if (windowCount == 1)
        {
            tiles.Add(new WindowTile(0, screen.X + gap, screen.Y + gap, screen.Width - (2 * gap), screen.Height - (2 * gap)));
            return tiles;
        }

        int availableWidth = screen.Width - (3 * gap);
        int masterWidth = (int)(availableWidth * Math.Clamp(masterRatio, 0.2, 0.8));
        int stackWidth = availableWidth - masterWidth;

        // Master window
        tiles.Add(new WindowTile(0, screen.X + gap, screen.Y + gap, masterWidth, screen.Height - (2 * gap)));

        // Stack windows
        int stackCount = windowCount - 1;
        int availableHeight = screen.Height - ((stackCount + 1) * gap);
        int stackItemHeight = availableHeight / stackCount;
        int startX = screen.X + (2 * gap) + masterWidth;

        for (int i = 0; i < stackCount; i++)
        {
            int currentY = screen.Y + gap + (i * (stackItemHeight + gap));
            int currentH = (i == stackCount - 1)
                ? (screen.Y + screen.Height - gap - currentY)
                : stackItemHeight;

            tiles.Add(new WindowTile(i + 1, startX, currentY, stackWidth, Math.Max(1, currentH)));
        }

        return tiles;
    }

    /// <summary>
    /// Computes Fair Grid tiling geometry: arranges N windows evenly into rows and columns.
    /// </summary>
    public static IReadOnlyList<WindowTile> CalculateFairGrid(
        ScreenArea screen,
        int windowCount,
        int gap = 8)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var tiles = new List<WindowTile>();
        if (windowCount <= 0) return tiles;

        int cols = (int)Math.Ceiling(Math.Sqrt(windowCount));
        int rows = (int)Math.Ceiling((double)windowCount / cols);

        int totalGapW = (cols + 1) * gap;
        int totalGapH = (rows + 1) * gap;

        int colW = (screen.Width - totalGapW) / cols;
        int rowH = (screen.Height - totalGapH) / rows;

        for (int i = 0; i < windowCount; i++)
        {
            int r = i / cols;
            int c = i % cols;

            int x = screen.X + gap + (c * (colW + gap));
            int y = screen.Y + gap + (r * (rowH + gap));

            int w = (c == cols - 1) ? (screen.X + screen.Width - gap - x) : colW;
            int h = (r == rows - 1) ? (screen.Y + screen.Height - gap - y) : rowH;

            tiles.Add(new WindowTile(i, x, y, Math.Max(1, w), Math.Max(1, h)));
        }

        return tiles;
    }
}
