using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Native;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Window and desktop workspace management engine.
/// Provides virtual desktop inspection, edge snap resistance auditing, and top-level window style/transparency diagnostics.
/// Converges techniques from dm2, fancywm, marco, miro, fluxbox, jwm, and windows10DesktopManager.
/// </summary>
internal sealed partial class WindowsCommandExecutor
{
    internal static class WindowManagerHelper
    {
        public static VirtualDesktopAuditResult AuditVirtualDesktops()
        {
            var desktopGuids = new List<string>();
            string? currentDesktopGuid = null;
            bool enabled = false;

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops");
                if (key is not null)
                {
                    object? currentVal = key.GetValue("CurrentVirtualDesktop");
                    if (currentVal is byte[] currentBytes && currentBytes.Length == 16)
                    {
                        currentDesktopGuid = new Guid(currentBytes).ToString("D");
                    }
                    else if (currentVal is string currentStr && Guid.TryParse(currentStr, out Guid parsedCurrent))
                    {
                        currentDesktopGuid = parsedCurrent.ToString("D");
                    }

                    object? idsVal = key.GetValue("VirtualDesktopIDs");
                    if (idsVal is byte[] idBytes && idBytes.Length >= 16)
                    {
                        for (int offset = 0; offset + 16 <= idBytes.Length; offset += 16)
                        {
                            byte[] chunk = new byte[16];
                            Array.Copy(idBytes, offset, chunk, 0, 16);
                            desktopGuids.Add(new Guid(chunk).ToString("D"));
                        }
                    }

                    enabled = desktopGuids.Count > 0;
                }

                // Fallback check under Desktops subkey
                if (desktopGuids.Count == 0)
                {
                    using RegistryKey? desktopsKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops");
                    if (desktopsKey is not null)
                    {
                        foreach (string subName in desktopsKey.GetSubKeyNames())
                        {
                            if (Guid.TryParse(subName.Trim('{', '}'), out Guid g))
                            {
                                desktopGuids.Add(g.ToString("D"));
                            }
                        }
                        if (desktopGuids.Count > 0) enabled = true;
                    }
                }
            }
            catch (Exception)
            {
                // Non-fatal: default to standard desktop state
            }

            int count = Math.Max(1, desktopGuids.Count);
            if (currentDesktopGuid is null && desktopGuids.Count > 0)
            {
                currentDesktopGuid = desktopGuids[0];
            }

            string summary = enabled
                ? $"Virtual Desktops Active: {count} desktops registered (Current: {currentDesktopGuid ?? "Primary"})."
                : "Virtual Desktops Single Desktop: Standard primary desktop active.";

            return new VirtualDesktopAuditResult(
                DesktopCount: count,
                CurrentDesktopId: currentDesktopGuid ?? "Primary",
                DesktopIds: desktopGuids,
                VirtualDesktopsEnabled: enabled,
                Summary: summary);
        }

        public static EdgeSnapResistanceAuditResult AuditEdgeSnapResistance()
        {
            bool windowArrangementActive = true;
            bool snapFillEnabled = true;
            bool snapAssistEnabled = true;
            bool jointResizeEnabled = true;
            bool dockMovingEnabled = true;

            try
            {
                using RegistryKey? desktopKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                if (desktopKey is not null)
                {
                    string? arrangement = desktopKey.GetValue("WindowArrangementActive") as string;
                    if (arrangement == "0") windowArrangementActive = false;

                    string? dockMoving = desktopKey.GetValue("DockMoving") as string;
                    if (dockMoving == "0") dockMovingEnabled = false;
                }

                using RegistryKey? advancedKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                if (advancedKey is not null)
                {
                    object? snapFill = advancedKey.GetValue("SnapFill");
                    if (snapFill is int sf && sf == 0) snapFillEnabled = false;

                    object? snapAssist = advancedKey.GetValue("SnapAssist");
                    if (snapAssist is int sa && sa == 0) snapAssistEnabled = false;

                    object? jointResize = advancedKey.GetValue("JointResize");
                    if (jointResize is int jr && jr == 0) jointResizeEnabled = false;
                }
            }
            catch (Exception)
            {
                // Fall back to defaults
            }

            const int edgeSnapThresholdPixels = 16;
            string summary = windowArrangementActive
                ? $"Snap Assist Active: Edge snapping enabled ({edgeSnapThresholdPixels}px magnetic threshold, SnapFill: {snapFillEnabled}, Assist: {snapAssistEnabled})."
                : "Snap Assist Disabled: Window edge docking and snapping inactive.";

            return new EdgeSnapResistanceAuditResult(
                WindowArrangementActive: windowArrangementActive,
                SnapFillEnabled: snapFillEnabled,
                SnapAssistEnabled: snapAssistEnabled,
                JointResizeEnabled: jointResizeEnabled,
                DockMovingEnabled: dockMovingEnabled,
                EdgeSnapThresholdPixels: edgeSnapThresholdPixels,
                Summary: summary);
        }

        public static WindowStyleTransparencyReport ProbeWindowStylesAndTransparency(int maxWindows = 200)
        {
            var windows = new List<WindowStyleItem>();
            int topmostCount = 0;
            int layeredCount = 0;
            int framelessCount = 0;

            WindowsInterop.EnumWindows((hwnd, _) =>
            {
                if (windows.Count >= maxWindows) return false;
                if (!WindowsInterop.IsWindowVisible(hwnd)) return true;

                var sbTitle = new StringBuilder(256);
                WindowsInterop.GetWindowText(hwnd, sbTitle, 256);
                string title = sbTitle.ToString().Trim();
                if (string.IsNullOrWhiteSpace(title)) return true;

                var sbClass = new StringBuilder(128);
                WindowsInterop.GetClassName(hwnd, sbClass, 128);
                string className = sbClass.ToString().Trim();

                WindowsInterop.GetWindowRect(hwnd, out WindowsInterop.Rect rect);
                int width = Math.Max(0, rect.Right - rect.Left);
                int height = Math.Max(0, rect.Bottom - rect.Top);
                if (width <= 0 || height <= 0) return true;

                nint style = WindowsInterop.GetWindowLongPtr(hwnd, WindowsInterop.GwlStyle);
                nint exStyle = WindowsInterop.GetWindowLongPtr(hwnd, WindowsInterop.GwlExStyle);

                bool isTopmost = ((long)exStyle & WindowsInterop.WsExTopmost) != 0;
                bool isLayered = ((long)exStyle & WindowsInterop.WsExLayered) != 0;
                bool hasCaption = ((long)style & WindowsInterop.WsCaption) != 0;
                bool hasThickFrame = ((long)style & WindowsInterop.WsThickFrame) != 0;
                bool isFrameless = !hasCaption && !hasThickFrame;

                byte? alpha = null;
                if (isLayered)
                {
                    if (WindowsInterop.GetLayeredWindowAttributes(hwnd, out uint _, out byte bAlpha, out uint dwFlags))
                    {
                        if ((dwFlags & WindowsInterop.LwaAlpha) != 0)
                        {
                            alpha = bAlpha;
                        }
                    }
                }

                if (isTopmost) topmostCount++;
                if (isLayered) layeredCount++;
                if (isFrameless) framelessCount++;

                windows.Add(new WindowStyleItem(
                    Handle: hwnd.ToInt64(),
                    Title: title,
                    ClassName: className,
                    X: rect.Left,
                    Y: rect.Top,
                    Width: width,
                    Height: height,
                    IsTopmost: isTopmost,
                    IsLayered: isLayered,
                    HasCaption: hasCaption,
                    HasThickFrame: hasThickFrame,
                    IsFrameless: isFrameless,
                    Alpha: alpha));

                return true;
            }, 0);

            string summary = $"Window Style Probe: {windows.Count} visible windows ({topmostCount} topmost, {layeredCount} layered/transparent, {framelessCount} frameless).";

            return new WindowStyleTransparencyReport(
                TotalWindowsAudited: windows.Count,
                TopmostCount: topmostCount,
                LayeredCount: layeredCount,
                FramelessCount: framelessCount,
                Windows: windows,
                Summary: summary);
        }

        public static SnapBounds CalculateMagneticSnap(
            int currentX,
            int currentY,
            int width,
            int height,
            int screenWidth,
            int screenHeight,
            int snapThreshold = 16)
        {
            int snappedX = currentX;
            int snappedY = currentY;
            bool snappedLeft = false;
            bool snappedRight = false;
            bool snappedTop = false;
            bool snappedBottom = false;

            // Snap to Left screen border
            if (Math.Abs(currentX) <= snapThreshold)
            {
                snappedX = 0;
                snappedLeft = true;
            }
            // Snap to Right screen border
            else if (Math.Abs(currentX + width - screenWidth) <= snapThreshold)
            {
                snappedX = screenWidth - width;
                snappedRight = true;
            }

            // Snap to Top screen border
            if (Math.Abs(currentY) <= snapThreshold)
            {
                snappedY = 0;
                snappedTop = true;
            }
            // Snap to Bottom screen border
            else if (Math.Abs(currentY + height - screenHeight) <= snapThreshold)
            {
                snappedY = screenHeight - height;
                snappedBottom = true;
            }

            return new SnapBounds(
                X: snappedX,
                Y: snappedY,
                Width: width,
                Height: height,
                SnappedLeft: snappedLeft,
                SnappedRight: snappedRight,
                SnappedTop: snappedTop,
                SnappedBottom: snappedBottom);
        }

        public static GridBounds CalculateFractionalGrid(
            int col,
            int row,
            int colSpan,
            int rowSpan,
            int totalCols,
            int totalRows,
            int screenWidth,
            int screenHeight)
        {
            int safeCols = Math.Max(1, totalCols);
            int safeRows = Math.Max(1, totalRows);
            int colWidth = screenWidth / safeCols;
            int rowHeight = screenHeight / safeRows;

            int clampedCol = Math.Clamp(col, 0, safeCols - 1);
            int clampedRow = Math.Clamp(row, 0, safeRows - 1);
            int clampedColSpan = Math.Clamp(colSpan, 1, safeCols - clampedCol);
            int clampedRowSpan = Math.Clamp(rowSpan, 1, safeRows - clampedRow);

            int x = clampedCol * colWidth;
            int y = clampedRow * rowHeight;
            int w = clampedColSpan * colWidth;
            int h = clampedRowSpan * rowHeight;

            return new GridBounds(
                X: x,
                Y: y,
                Width: w,
                Height: h,
                Column: clampedCol,
                Row: clampedRow,
                ColumnSpan: clampedColSpan,
                RowSpan: clampedRowSpan);
        }

        public static bool SetWindowTransparency(nint hwnd, byte alpha)
        {
            if (hwnd == 0) return false;
            try
            {
                nint exStyle = WindowsInterop.GetWindowLongPtr(hwnd, WindowsInterop.GwlExStyle);
                if (((long)exStyle & WindowsInterop.WsExLayered) == 0)
                {
                    nint newExStyle = (nint)((long)exStyle | WindowsInterop.WsExLayered);
                    WindowsInterop.SetWindowLongPtr(hwnd, WindowsInterop.GwlExStyle, newExStyle);
                }
                return WindowsInterop.SetLayeredWindowAttributes(hwnd, 0, alpha, WindowsInterop.LwaAlpha);
            }
            catch
            {
                return false;
            }
        }

        public static bool ToggleFramelessStyle(nint hwnd, bool frameless)
        {
            if (hwnd == 0) return false;
            try
            {
                nint style = WindowsInterop.GetWindowLongPtr(hwnd, WindowsInterop.GwlStyle);
                nint newStyle;
                if (frameless)
                {
                    newStyle = (nint)((long)style & ~(WindowsInterop.WsCaption | WindowsInterop.WsThickFrame));
                }
                else
                {
                    newStyle = (nint)((long)style | WindowsInterop.WsCaption | WindowsInterop.WsThickFrame);
                }

                WindowsInterop.SetWindowLongPtr(hwnd, WindowsInterop.GwlStyle, newStyle);
                return WindowsInterop.SetWindowPos(
                    hwnd,
                    0,
                    0,
                    0,
                    0,
                    0,
                    WindowsInterop.SwpFrameChanged | WindowsInterop.SwpNoMove | WindowsInterop.SwpNoSize | WindowsInterop.SwpNoZOrder);
            }
            catch
            {
                return false;
            }
        }

        public static SplitPaneResult SplitPaneLayout(
            WindowPlacementRect bounds,
            SplitOrientation orientation,
            double ratio = 0.5,
            int margin = 4)
        {
            double safeRatio = Math.Clamp(ratio, 0.05, 0.95);
            int safeMargin = Math.Max(0, margin);

            if (orientation == SplitOrientation.Horizontal)
            {
                int availableWidth = Math.Max(0, bounds.Width - safeMargin);
                int firstWidth = (int)Math.Round(availableWidth * safeRatio);
                int secondWidth = availableWidth - firstWidth;

                var first = new WindowPlacementRect(bounds.X, bounds.Y, firstWidth, bounds.Height);
                var second = new WindowPlacementRect(bounds.X + firstWidth + safeMargin, bounds.Y, secondWidth, bounds.Height);
                return new SplitPaneResult(first, second, orientation, safeRatio, safeMargin);
            }
            else
            {
                int availableHeight = Math.Max(0, bounds.Height - safeMargin);
                int firstHeight = (int)Math.Round(availableHeight * safeRatio);
                int secondHeight = availableHeight - firstHeight;

                var first = new WindowPlacementRect(bounds.X, bounds.Y, bounds.Width, firstHeight);
                var second = new WindowPlacementRect(bounds.X, bounds.Y + firstHeight + safeMargin, bounds.Width, secondHeight);
                return new SplitPaneResult(first, second, orientation, safeRatio, safeMargin);
            }
        }

        public static IReadOnlyList<WindowPlacementRect> CascadePlacement(
            IReadOnlyList<WindowPlacementRect> windows,
            WindowPlacementRect workArea,
            int titlebarHeight = 32,
            int stepOffset = 30)
        {
            if (windows == null || windows.Count == 0) return Array.Empty<WindowPlacementRect>();

            var result = new List<WindowPlacementRect>(windows.Count);
            int currentX = workArea.X;
            int currentY = workArea.Y;
            int maxX = workArea.X + (workArea.Width / 2);
            int maxY = workArea.Y + (workArea.Height / 2);

            foreach (var win in windows)
            {
                if (currentX > maxX || currentY > maxY)
                {
                    currentX = workArea.X;
                    currentY = workArea.Y;
                }

                int w = Math.Min(win.Width, workArea.Width);
                int h = Math.Min(win.Height, workArea.Height);
                result.Add(new WindowPlacementRect(currentX, currentY, w, h));

                currentX += stepOffset;
                currentY += titlebarHeight;
            }

            return result;
        }

        public static WindowMagneticSnapResult CalculateWindowMagneticSnap(
            int currentX,
            int currentY,
            int width,
            int height,
            IReadOnlyList<WindowPlacementRect>? neighborWindows,
            int screenWidth,
            int screenHeight,
            int snapThreshold = 16)
        {
            int snappedX = currentX;
            int snappedY = currentY;
            bool screenLeft = false;
            bool screenRight = false;
            bool screenTop = false;
            bool screenBottom = false;
            bool windowSnapped = false;

            // Screen borders
            if (Math.Abs(currentX) <= snapThreshold)
            {
                snappedX = 0;
                screenLeft = true;
            }
            else if (Math.Abs(currentX + width - screenWidth) <= snapThreshold)
            {
                snappedX = screenWidth - width;
                screenRight = true;
            }

            if (Math.Abs(currentY) <= snapThreshold)
            {
                snappedY = 0;
                screenTop = true;
            }
            else if (Math.Abs(currentY + height - screenHeight) <= snapThreshold)
            {
                snappedY = screenHeight - height;
                screenBottom = true;
            }

            // Neighbor windows (outer edge docking and alignment)
            if (neighborWindows != null && neighborWindows.Count > 0)
            {
                foreach (var neighbor in neighborWindows)
                {
                    if (!screenLeft && !screenRight)
                    {
                        // Outer: Left to Neighbor Right
                        if (Math.Abs(snappedX - neighbor.Right) <= snapThreshold)
                        {
                            snappedX = neighbor.Right;
                            windowSnapped = true;
                        }
                        // Outer: Right to Neighbor Left
                        else if (Math.Abs(snappedX + width - neighbor.X) <= snapThreshold)
                        {
                            snappedX = neighbor.X - width;
                            windowSnapped = true;
                        }
                        // Alignment: Left to Neighbor Left
                        else if (Math.Abs(snappedX - neighbor.X) <= snapThreshold)
                        {
                            snappedX = neighbor.X;
                            windowSnapped = true;
                        }
                    }

                    if (!screenTop && !screenBottom)
                    {
                        // Outer: Top to Neighbor Bottom
                        if (Math.Abs(snappedY - neighbor.Bottom) <= snapThreshold)
                        {
                            snappedY = neighbor.Bottom;
                            windowSnapped = true;
                        }
                        // Outer: Bottom to Neighbor Top
                        else if (Math.Abs(snappedY + height - neighbor.Y) <= snapThreshold)
                        {
                            snappedY = neighbor.Y - height;
                            windowSnapped = true;
                        }
                        // Alignment: Top to Neighbor Top
                        else if (Math.Abs(snappedY - neighbor.Y) <= snapThreshold)
                        {
                            snappedY = neighbor.Y;
                            windowSnapped = true;
                        }
                    }
                }
            }

            return new WindowMagneticSnapResult(
                X: snappedX,
                Y: snappedY,
                Width: width,
                Height: height,
                SnappedScreenLeft: screenLeft,
                SnappedScreenRight: screenRight,
                SnappedScreenTop: screenTop,
                SnappedScreenBottom: screenBottom,
                SnappedToWindow: windowSnapped);
        }

        public static WindowPlacementRect CalculateUsableWorkArea(
            WindowPlacementRect totalScreen,
            IReadOnlyList<WindowPlacementRect>? reservedStruts)
        {
            if (reservedStruts == null || reservedStruts.Count == 0) return totalScreen;

            int left = totalScreen.X;
            int top = totalScreen.Y;
            int right = totalScreen.Right;
            int bottom = totalScreen.Bottom;

            foreach (var strut in reservedStruts)
            {
                // Top strut (occupies screen width at the top)
                if (strut.Y <= top && strut.Bottom > top && strut.Width >= (totalScreen.Width / 2))
                {
                    top = Math.Max(top, strut.Bottom);
                }
                // Bottom strut (occupies screen width at the bottom)
                else if (strut.Bottom >= bottom && strut.Y < bottom && strut.Width >= (totalScreen.Width / 2))
                {
                    bottom = Math.Min(bottom, strut.Y);
                }
                // Left strut (occupies screen height at the left)
                else if (strut.X <= left && strut.Right > left && strut.Height >= (totalScreen.Height / 2))
                {
                    left = Math.Max(left, strut.Right);
                }
                // Right strut (occupies screen height at the right)
                else if (strut.Right >= right && strut.X < right && strut.Height >= (totalScreen.Height / 2))
                {
                    right = Math.Min(right, strut.X);
                }
            }

            return new WindowPlacementRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        public static IReadOnlyList<WindowPlacementRect> CalculateTiledWindows(
            IReadOnlyList<WindowPlacementRect> windows,
            WindowPlacementRect workArea,
            TilingLayoutMode mode,
            int margin = 4)
        {
            if (windows == null || windows.Count == 0) return Array.Empty<WindowPlacementRect>();
            if (windows.Count == 1) return [workArea];

            int safeMargin = Math.Max(0, margin);

            switch (mode)
            {
                case TilingLayoutMode.EqualColumns:
                {
                    int count = windows.Count;
                    int totalMargins = safeMargin * (count - 1);
                    int colWidth = Math.Max(0, (workArea.Width - totalMargins) / count);
                    var result = new List<WindowPlacementRect>(count);
                    for (int i = 0; i < count; i++)
                    {
                        int x = workArea.X + i * (colWidth + safeMargin);
                        int w = (i == count - 1) ? (workArea.Right - x) : colWidth;
                        result.Add(new WindowPlacementRect(x, workArea.Y, w, workArea.Height));
                    }
                    return result;
                }

                case TilingLayoutMode.EqualRows:
                {
                    int count = windows.Count;
                    int totalMargins = safeMargin * (count - 1);
                    int rowHeight = Math.Max(0, (workArea.Height - totalMargins) / count);
                    var result = new List<WindowPlacementRect>(count);
                    for (int i = 0; i < count; i++)
                    {
                        int y = workArea.Y + i * (rowHeight + safeMargin);
                        int h = (i == count - 1) ? (workArea.Bottom - y) : rowHeight;
                        result.Add(new WindowPlacementRect(workArea.X, y, workArea.Width, h));
                    }
                    return result;
                }

                case TilingLayoutMode.BinarySplit:
                {
                    // First window gets master pane (60%), remaining split in stack pane (40%)
                    var split = SplitPaneLayout(workArea, SplitOrientation.Horizontal, 0.60, safeMargin);
                    var result = new List<WindowPlacementRect>(windows.Count) { split.FirstPane };
                    int stackCount = windows.Count - 1;
                    int totalMargins = safeMargin * (stackCount - 1);
                    int stackHeight = Math.Max(0, (split.SecondPane.Height - totalMargins) / stackCount);
                    for (int i = 0; i < stackCount; i++)
                    {
                        int y = split.SecondPane.Y + i * (stackHeight + safeMargin);
                        int h = (i == stackCount - 1) ? (split.SecondPane.Bottom - y) : stackHeight;
                        result.Add(new WindowPlacementRect(split.SecondPane.X, y, split.SecondPane.Width, h));
                    }
                    return result;
                }

                case TilingLayoutMode.Grid2x2:
                {
                    int count = Math.Min(4, windows.Count);
                    var hSplit = SplitPaneLayout(workArea, SplitOrientation.Horizontal, 0.5, safeMargin);
                    var leftV = SplitPaneLayout(hSplit.FirstPane, SplitOrientation.Vertical, 0.5, safeMargin);
                    var rightV = SplitPaneLayout(hSplit.SecondPane, SplitOrientation.Vertical, 0.5, safeMargin);
                    var cells = new[] { leftV.FirstPane, rightV.FirstPane, leftV.SecondPane, rightV.SecondPane };
                    var result = new List<WindowPlacementRect>(count);
                    for (int i = 0; i < count; i++)
                    {
                        result.Add(cells[i]);
                    }
                    return result;
                }

                case TilingLayoutMode.Cascade:
                default:
                    return CascadePlacement(windows, workArea);
            }
        }

        public static WindowPlacementRect ClampOrCenter(WindowPlacementRect window, WindowPlacementRect bounds)
        {
            int newX = window.X;
            int newY = window.Y;

            if (window.Width > bounds.Width)
            {
                newX = bounds.X + ((bounds.Width - window.Width) / 2);
            }
            else if (window.X < bounds.X)
            {
                newX = bounds.X;
            }
            else if (window.Right > bounds.Right)
            {
                newX = bounds.Right - window.Width;
            }

            if (window.Height > bounds.Height)
            {
                newY = bounds.Y + ((bounds.Height - window.Height) / 2);
            }
            else if (window.Y < bounds.Y)
            {
                newY = bounds.Y;
            }
            else if (window.Bottom > bounds.Bottom)
            {
                newY = bounds.Bottom - window.Height;
            }

            return new WindowPlacementRect(newX, newY, window.Width, window.Height);
        }

        public static WindowPlacementRect ConstrainSize(
            WindowPlacementRect window,
            int minWidth = 100,
            int maxWidth = int.MaxValue,
            int minHeight = 60,
            int maxHeight = int.MaxValue,
            int aspectRatioX = 0,
            int aspectRatioY = 0)
        {
            int w = Math.Clamp(window.Width, Math.Max(1, minWidth), Math.Max(minWidth, maxWidth));
            int h = Math.Clamp(window.Height, Math.Max(1, minHeight), Math.Max(minHeight, maxHeight));

            if (aspectRatioX > 0 && aspectRatioY > 0)
            {
                if (aspectRatioX >= aspectRatioY)
                {
                    h = (int)Math.Round((double)w * aspectRatioY / aspectRatioX);
                    if (h > maxHeight)
                    {
                        h = maxHeight;
                        w = (int)Math.Round((double)h * aspectRatioX / aspectRatioY);
                    }
                }
                else
                {
                    w = (int)Math.Round((double)h * aspectRatioX / aspectRatioY);
                    if (w > maxWidth)
                    {
                        w = maxWidth;
                        h = (int)Math.Round((double)w * aspectRatioY / aspectRatioX);
                    }
                }
            }

            return new WindowPlacementRect(window.X, window.Y, w, h);
        }

        public static WindowPlacementRect CalculateSmartColPlacement(
            WindowPlacementRect workArea,
            IReadOnlyList<WindowPlacementRect> existingWindows,
            int newWidth,
            int newHeight,
            int stepOffset = 16)
        {
            int clampedW = Math.Min(newWidth, workArea.Width);
            int clampedH = Math.Min(newHeight, workArea.Height);

            if (existingWindows == null || existingWindows.Count == 0)
            {
                return new WindowPlacementRect(workArea.X, workArea.Y, clampedW, clampedH);
            }

            int maxX = workArea.Right - clampedW;
            int maxY = workArea.Bottom - clampedH;

            for (int y = workArea.Y; y <= maxY; y += stepOffset)
            {
                for (int x = workArea.X; x <= maxX; x += stepOffset)
                {
                    var testRect = new WindowPlacementRect(x, y, clampedW, clampedH);
                    bool overlaps = false;
                    foreach (var win in existingWindows)
                    {
                        if (testRect.X < win.Right && testRect.Right > win.X &&
                            testRect.Y < win.Bottom && testRect.Bottom > win.Y)
                        {
                            overlaps = true;
                            break;
                        }
                    }

                    if (!overlaps)
                    {
                        return testRect;
                    }
                }
            }

            int cascadeIndex = existingWindows.Count % 8;
            int fallbackX = Math.Min(workArea.X + (cascadeIndex * 30), maxX);
            int fallbackY = Math.Min(workArea.Y + (cascadeIndex * 30), maxY);
            return new WindowPlacementRect(fallbackX, fallbackY, clampedW, clampedH);
        }

        public static WindowPlacementRect CycleDirectionalGrid(
            WindowPlacementRect workArea,
            DirectionalGridAction action,
            int cycleStep)
        {
            int step = Math.Abs(cycleStep) % 3;
            switch (action)
            {
                case DirectionalGridAction.Left:
                {
                    int w = step switch { 0 => workArea.Width / 2, 1 => (workArea.Width * 2) / 3, _ => workArea.Width / 3 };
                    return new WindowPlacementRect(workArea.X, workArea.Y, w, workArea.Height);
                }
                case DirectionalGridAction.Right:
                {
                    int w = step switch { 0 => workArea.Width / 2, 1 => (workArea.Width * 2) / 3, _ => workArea.Width / 3 };
                    return new WindowPlacementRect(workArea.Right - w, workArea.Y, w, workArea.Height);
                }
                case DirectionalGridAction.Top:
                {
                    int h = step switch { 0 => workArea.Height / 2, 1 => (workArea.Height * 2) / 3, _ => workArea.Height / 3 };
                    return new WindowPlacementRect(workArea.X, workArea.Y, workArea.Width, h);
                }
                case DirectionalGridAction.Bottom:
                {
                    int h = step switch { 0 => workArea.Height / 2, 1 => (workArea.Height * 2) / 3, _ => workArea.Height / 3 };
                    return new WindowPlacementRect(workArea.X, workArea.Bottom - h, workArea.Width, h);
                }
                case DirectionalGridAction.Center:
                default:
                {
                    int w = step switch { 0 => workArea.Width, 1 => (workArea.Width * 3) / 4, _ => workArea.Width / 2 };
                    int h = step switch { 0 => workArea.Height, 1 => (workArea.Height * 3) / 4, _ => workArea.Height / 2 };
                    int x = workArea.X + ((workArea.Width - w) / 2);
                    int y = workArea.Y + ((workArea.Height - h) / 2);
                    return new WindowPlacementRect(x, y, w, h);
                }
            }
        }

        public static WindowShadeResult CalculateShadedWindow(
            WindowPlacementRect currentWindow,
            bool isCurrentlyShaded,
            int unshadedHeight = 0,
            int titlebarCaptionHeight = 32)
        {
            if (isCurrentlyShaded)
            {
                int restoredHeight = unshadedHeight > titlebarCaptionHeight ? unshadedHeight : 400;
                return new WindowShadeResult(
                    new WindowPlacementRect(currentWindow.X, currentWindow.Y, currentWindow.Width, restoredHeight),
                    IsShaded: false,
                    OriginalHeight: restoredHeight);
            }
            else
            {
                int savedHeight = Math.Max(currentWindow.Height, titlebarCaptionHeight);
                return new WindowShadeResult(
                    new WindowPlacementRect(currentWindow.X, currentWindow.Y, currentWindow.Width, titlebarCaptionHeight),
                    IsShaded: true,
                    OriginalHeight: savedHeight);
            }
        }

        public static bool MatchesWindowRule(
            string? processName,
            string? className,
            string? title,
            string? rulePattern,
            WindowRuleTarget target = WindowRuleTarget.ProcessName)
        {
            if (string.IsNullOrWhiteSpace(rulePattern)) return false;

            string candidate = target switch
            {
                WindowRuleTarget.ProcessName => processName ?? string.Empty,
                WindowRuleTarget.ClassName => className ?? string.Empty,
                WindowRuleTarget.Title => title ?? string.Empty,
                _ => string.Empty
            };

            if (string.Equals(candidate, rulePattern, StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(
                    candidate,
                    rulePattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(50));
            }
            catch
            {
                return false;
            }
        }

        public static bool SetWindowCornerPreference(nint hwnd, WindowCornerPreference preference)
        {
            if (hwnd == 0) return false;
            int pref = (int)preference;
            int hr = WindowsInterop.DwmSetWindowAttribute(hwnd, WindowsInterop.DwmwaWindowCornerPreference, ref pref, sizeof(int));
            return hr == 0;
        }

        public static WindowCornerPreference? GetWindowCornerPreference(nint hwnd)
        {
            if (hwnd == 0) return null;
            int hr = WindowsInterop.DwmGetWindowAttribute(hwnd, WindowsInterop.DwmwaWindowCornerPreference, out int pref, sizeof(int));
            if (hr != 0) return null;
            return (WindowCornerPreference)pref;
        }

        public static bool SetWindowBackdropType(nint hwnd, WindowBackdropType backdrop)
        {
            if (hwnd == 0) return false;
            int type = (int)backdrop;
            int hr = WindowsInterop.DwmSetWindowAttribute(hwnd, WindowsInterop.DwmwaSystemBackdropType, ref type, sizeof(int));
            return hr == 0;
        }

        public static WindowBackdropType? GetWindowBackdropType(nint hwnd)
        {
            if (hwnd == 0) return null;
            int hr = WindowsInterop.DwmGetWindowAttribute(hwnd, WindowsInterop.DwmwaSystemBackdropType, out int type, sizeof(int));
            if (hr != 0) return null;
            return (WindowBackdropType)type;
        }

        public static bool SetWindowProcessPriority(nint hwnd, ProcessPriorityClass priority)
        {
            if (hwnd == 0) return false;
            WindowsInterop.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return false;

            nint hProcess = WindowsInterop.OpenProcess(WindowsInterop.ProcessSetInformation, false, pid);
            if (hProcess == 0) return false;

            try
            {
                uint dwPriority = priority switch
                {
                    ProcessPriorityClass.RealTime => 0x00000100,
                    ProcessPriorityClass.High => 0x00000080,
                    ProcessPriorityClass.AboveNormal => 0x00008000,
                    ProcessPriorityClass.BelowNormal => 0x00004000,
                    ProcessPriorityClass.Idle => 0x00000040,
                    _ => 0x00000020 // Normal
                };
                return WindowsInterop.SetPriorityClass(hProcess, dwPriority);
            }
            finally
            {
                WindowsInterop.CloseHandle(hProcess);
            }
        }

        public static ProcessPriorityClass? GetWindowProcessPriority(nint hwnd)
        {
            if (hwnd == 0) return null;
            WindowsInterop.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;

            nint hProcess = WindowsInterop.OpenProcess(WindowsInterop.ProcessQueryLimitedInformation, false, pid);
            if (hProcess == 0) return null;

            try
            {
                uint dwPriority = WindowsInterop.GetPriorityClass(hProcess);
                return dwPriority switch
                {
                    0x00000100 => ProcessPriorityClass.RealTime,
                    0x00000080 => ProcessPriorityClass.High,
                    0x00008000 => ProcessPriorityClass.AboveNormal,
                    0x00004000 => ProcessPriorityClass.BelowNormal,
                    0x00000040 => ProcessPriorityClass.Idle,
                    0x00000020 => ProcessPriorityClass.Normal,
                    _ => null
                };
            }
            finally
            {
                WindowsInterop.CloseHandle(hProcess);
            }
        }

        public static bool ForceActivateWindow(nint hwnd)
        {
            if (hwnd == 0) return false;
            if (WindowsInterop.SetForegroundWindow(hwnd)) return true;

            uint curThreadId = WindowsInterop.GetCurrentThreadId();
            nint foreHwnd = WindowsInterop.GetForegroundWindow();
            uint foreThreadId = foreHwnd != 0 ? WindowsInterop.GetWindowThreadProcessId(foreHwnd, out _) : 0;

            bool attached = false;
            if (foreThreadId != 0 && foreThreadId != curThreadId)
            {
                attached = WindowsInterop.AttachThreadInput(curThreadId, foreThreadId, true);
            }

            try
            {
                return WindowsInterop.SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached)
                {
                    WindowsInterop.AttachThreadInput(curThreadId, foreThreadId, false);
                }
            }
        }

        public static bool? IsWindowOnCurrentVirtualDesktop(nint hwnd)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;

            try
            {
                var vdm = (WindowsInterop.IVirtualDesktopManager)new WindowsInterop.VirtualDesktopManagerClass();
                int hr = vdm.IsWindowOnCurrentVirtualDesktop(hwnd, out bool onCurrent);
                return hr == 0 ? onCurrent : null;
            }
            catch
            {
                return null;
            }
        }

        public static Guid? GetWindowDesktopGuid(nint hwnd)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;

            try
            {
                var vdm = (WindowsInterop.IVirtualDesktopManager)new WindowsInterop.VirtualDesktopManagerClass();
                int hr = vdm.GetWindowDesktopId(hwnd, out Guid desktopId);
                return hr == 0 ? desktopId : null;
            }
            catch
            {
                return null;
            }
        }

        public static bool MoveWindowToVirtualDesktop(nint hwnd, Guid desktopId)
        {
            if (hwnd == 0 || desktopId == Guid.Empty || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            try
            {
                var vdm = (WindowsInterop.IVirtualDesktopManager)new WindowsInterop.VirtualDesktopManagerClass();
                int hr = vdm.MoveWindowToDesktop(hwnd, ref desktopId);
                return hr == 0;
            }
            catch
            {
                return false;
            }
        }

        public static IReadOnlyList<WindowPlacementRect> CalculateRatioMasterStack(
            WindowPlacementRect availableArea,
            int windowCount,
            double masterRatio = 0.618,
            int spacing = 8)
        {
            if (windowCount <= 0)
                return [];

            if (windowCount == 1)
                return [availableArea];

            masterRatio = Math.Clamp(masterRatio, 0.1, 0.9);
            spacing = Math.Max(0, spacing);

            int totalAvailableWidth = Math.Max(10, availableArea.Width - spacing);
            int masterWidth = (int)Math.Round(totalAvailableWidth * masterRatio);
            masterWidth = Math.Clamp(masterWidth, 50, Math.Max(50, totalAvailableWidth - 50));
            int stackWidth = Math.Max(50, totalAvailableWidth - masterWidth);

            var result = new List<WindowPlacementRect>(windowCount);
            // Master window
            result.Add(new WindowPlacementRect(availableArea.X, availableArea.Y, masterWidth, availableArea.Height));

            // Stack windows
            int stackCount = windowCount - 1;
            int stackX = availableArea.X + masterWidth + spacing;
            int totalSpacingY = (stackCount - 1) * spacing;
            int availableStackHeight = Math.Max(stackCount, availableArea.Height - totalSpacingY);
            int baseHeight = availableStackHeight / stackCount;
            int remainderHeight = availableStackHeight % stackCount;

            int currentY = availableArea.Y;
            for (int i = 0; i < stackCount; i++)
            {
                int h = baseHeight + (i < remainderHeight ? 1 : 0);
                result.Add(new WindowPlacementRect(stackX, currentY, stackWidth, h));
                currentY += h + spacing;
            }

            return result;
        }

        public static IReadOnlyList<WindowPlacementRect> CalculateUniformGridLayout(
            WindowPlacementRect availableArea,
            int windowCount,
            int spacing = 8)
        {
            if (windowCount <= 0)
                return [];

            if (windowCount == 1)
                return [availableArea];

            spacing = Math.Max(0, spacing);
            int cols = (int)Math.Ceiling(Math.Sqrt(windowCount));
            int rows = (int)Math.Ceiling((double)windowCount / cols);

            int totalSpacingX = (cols - 1) * spacing;
            int totalSpacingY = (rows - 1) * spacing;

            int availW = Math.Max(cols, availableArea.Width - totalSpacingX);
            int availH = Math.Max(rows, availableArea.Height - totalSpacingY);

            int baseW = availW / cols;
            int remW = availW % cols;

            int baseH = availH / rows;
            int remH = availH % rows;

            var result = new List<WindowPlacementRect>(windowCount);
            for (int i = 0; i < windowCount; i++)
            {
                int r = i / cols;
                int c = i % cols;

                int actualColW = baseW + (c < remW ? 1 : 0);
                int actualX = availableArea.X + c * (baseW + spacing) + Math.Min(c, remW);

                int cellH = baseH + (r < remH ? 1 : 0);
                int actualY = availableArea.Y + r * (baseH + spacing) + Math.Min(r, remH);

                result.Add(new WindowPlacementRect(actualX, actualY, actualColW, cellH));
            }

            return result;
        }

        public static WindowPlacementRect CalculateMinOverlapPlacement(
            WindowPlacementRect workArea,
            int width,
            int height,
            IReadOnlyList<WindowPlacementRect> existingWindows,
            int step = 32)
        {
            width = Math.Clamp(width, 50, workArea.Width);
            height = Math.Clamp(height, 50, workArea.Height);
            step = Math.Max(8, step);

            if (existingWindows == null || existingWindows.Count == 0)
            {
                return new WindowPlacementRect(workArea.X, workArea.Y, width, height);
            }

            int bestX = workArea.X;
            int bestY = workArea.Y;
            long minOverlapArea = long.MaxValue;

            int maxX = workArea.X + workArea.Width - width;
            int maxY = workArea.Y + workArea.Height - height;

            for (int y = workArea.Y; y <= maxY; y += step)
            {
                for (int x = workArea.X; x <= maxX; x += step)
                {
                    long overlap = 0;
                    var candidate = new WindowPlacementRect(x, y, width, height);

                    foreach (var other in existingWindows)
                    {
                        int overlapLeft = Math.Max(candidate.X, other.X);
                        int overlapRight = Math.Min(candidate.Right, other.Right);
                        int overlapTop = Math.Max(candidate.Y, other.Y);
                        int overlapBottom = Math.Min(candidate.Bottom, other.Bottom);

                        if (overlapRight > overlapLeft && overlapBottom > overlapTop)
                        {
                            long area = (long)(overlapRight - overlapLeft) * (overlapBottom - overlapTop);
                            overlap += area;
                        }
                    }

                    if (overlap == 0)
                    {
                        return candidate;
                    }

                    if (overlap < minOverlapArea)
                    {
                        minOverlapArea = overlap;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            return new WindowPlacementRect(bestX, bestY, width, height);
        }

        public static WindowPlacementRect CalculateWindowToWindowSnap(
            WindowPlacementRect candidate,
            IReadOnlyList<WindowPlacementRect> otherWindows,
            int snapThreshold = 16)
        {
            if (otherWindows == null || otherWindows.Count == 0)
                return candidate;

            int newX = candidate.X;
            int newY = candidate.Y;
            int minDeltaX = snapThreshold + 1;
            int minDeltaY = snapThreshold + 1;

            foreach (var other in otherWindows)
            {
                bool vertAligned = candidate.Y < other.Bottom && candidate.Bottom > other.Y;
                if (vertAligned)
                {
                    // Candidate Right -> Other Left
                    int d1 = Math.Abs(candidate.Right - other.X);
                    if (d1 <= snapThreshold && d1 < minDeltaX)
                    {
                        minDeltaX = d1;
                        newX = other.X - candidate.Width;
                    }
                    // Candidate Left -> Other Right
                    int d2 = Math.Abs(candidate.X - other.Right);
                    if (d2 <= snapThreshold && d2 < minDeltaX)
                    {
                        minDeltaX = d2;
                        newX = other.Right;
                    }
                    // Candidate Left -> Other Left
                    int d3 = Math.Abs(candidate.X - other.X);
                    if (d3 <= snapThreshold && d3 < minDeltaX)
                    {
                        minDeltaX = d3;
                        newX = other.X;
                    }
                }

                bool horizAligned = candidate.X < other.Right && candidate.Right > other.X;
                if (horizAligned)
                {
                    // Candidate Bottom -> Other Top
                    int d1 = Math.Abs(candidate.Bottom - other.Y);
                    if (d1 <= snapThreshold && d1 < minDeltaY)
                    {
                        minDeltaY = d1;
                        newY = other.Y - candidate.Height;
                    }
                    // Candidate Top -> Other Bottom
                    int d2 = Math.Abs(candidate.Y - other.Bottom);
                    if (d2 <= snapThreshold && d2 < minDeltaY)
                    {
                        minDeltaY = d2;
                        newY = other.Bottom;
                    }
                    // Candidate Top -> Other Top
                    int d3 = Math.Abs(candidate.Y - other.Y);
                    if (d3 <= snapThreshold && d3 < minDeltaY)
                    {
                        minDeltaY = d3;
                        newY = other.Y;
                    }
                }
            }

            return new WindowPlacementRect(newX, newY, candidate.Width, candidate.Height);
        }

        public static WindowPlacementRect CalculateScratchpadBounds(
            WindowPlacementRect workArea,
            double widthFraction = 0.70,
            double heightFraction = 0.65,
            ScratchpadAnchor anchor = ScratchpadAnchor.TopCenter)
        {
            widthFraction = Math.Clamp(widthFraction, 0.2, 1.0);
            heightFraction = Math.Clamp(heightFraction, 0.2, 1.0);

            int width = (int)Math.Round(workArea.Width * widthFraction);
            int height = (int)Math.Round(workArea.Height * heightFraction);

            int x = workArea.X + (workArea.Width - width) / 2;
            int y = anchor switch
            {
                ScratchpadAnchor.TopCenter => workArea.Y + 24,
                ScratchpadAnchor.BottomCenter => workArea.Bottom - height - 24,
                _ => workArea.Y + (workArea.Height - height) / 2
            };

            return new WindowPlacementRect(x, y, width, height);
        }

        public static bool ToggleWindowTopmost(nint hwnd)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            nint exStyle = WindowsInterop.GetWindowLongPtr(hwnd, WindowsInterop.GwlExStyle);
            bool isTopmost = ((uint)exStyle & WindowsInterop.WsExTopmost) != 0;
            nint insertAfter = isTopmost ? WindowsInterop.HwndNotTopmost : WindowsInterop.HwndTopmost;
            bool ok = WindowsInterop.SetWindowPos(
                hwnd,
                insertAfter,
                0, 0, 0, 0,
                WindowsInterop.SwpNoMove | WindowsInterop.SwpNoSize | WindowsInterop.SwpShowWindow);

            return ok ? !isTopmost : isTopmost;
        }

        public static byte StepWindowTransparency(nint hwnd, int deltaPercent = -10)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return 255;

            byte currentAlpha = 255;
            if (WindowsInterop.GetLayeredWindowAttributes(hwnd, out _, out byte bAlpha, out uint flags))
            {
                if ((flags & WindowsInterop.LwaAlpha) != 0)
                    currentAlpha = bAlpha;
            }

            int currentPercent = (int)Math.Round((double)currentAlpha * 100 / 255);
            int targetPercent = Math.Clamp(currentPercent + deltaPercent, 10, 100);
            byte targetAlpha = (byte)Math.Round((double)targetPercent * 255 / 100);

            SetWindowTransparency(hwnd, targetAlpha);
            return targetAlpha;
        }

        public static bool BeginWindowDrag(nint hwnd)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            WindowsInterop.ReleaseCapture();
            nint res = WindowsInterop.SendMessage(hwnd, WindowsInterop.WmSysCommand, WindowsInterop.ScMove | WindowsInterop.HtCaption, 0);
            return res == 0;
        }

        public static bool CenterWindow(nint hwnd, WindowPlacementRect? targetMonitorBounds = null)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            if (!WindowsInterop.GetWindowRect(hwnd, out var rect))
                return false;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;

            WindowPlacementRect bounds;
            if (targetMonitorBounds.HasValue)
            {
                bounds = targetMonitorBounds.Value;
            }
            else
            {
                nint hDesk = WindowsInterop.GetDesktopWindow();
                if (hDesk != 0 && WindowsInterop.GetWindowRect(hDesk, out var dRect))
                {
                    bounds = new WindowPlacementRect(dRect.Left, dRect.Top, dRect.Right - dRect.Left, dRect.Bottom - dRect.Top);
                }
                else
                {
                    bounds = new WindowPlacementRect(0, 0, 1920, 1080);
                }
            }

            int x = bounds.X + (bounds.Width - w) / 2;
            int y = bounds.Y + (bounds.Height - h) / 2;

            return WindowsInterop.SetWindowPos(hwnd, 0, x, y, w, h, WindowsInterop.SwpShowWindow | WindowsInterop.SwpNoZOrder);
        }

        public static bool BeginWindowResize(nint hwnd, WindowResizeEdge edge)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            WindowsInterop.ReleaseCapture();
            nint command = WindowsInterop.ScSize | (nint)edge;
            nint res = WindowsInterop.SendMessage(hwnd, WindowsInterop.WmSysCommand, command, 0);
            return res == 0;
        }

        public static int EnableWindowHierarchy(nint hwnd)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return 0;

            int enabledCount = 0;
            WindowsInterop.EnableWindow(hwnd, true);
            enabledCount++;

            WindowsInterop.EnumChildWindows(hwnd, (child, _) =>
            {
                if (child != 0)
                {
                    WindowsInterop.EnableWindow(child, true);
                    enabledCount++;
                }
                return true;
            }, 0);

            return enabledCount;
        }

        public static int EnableSystemMenuItems(nint hwnd)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return 0;

            nint hMenu = WindowsInterop.GetSystemMenu(hwnd, false);
            if (hMenu == 0)
                return 0;

            int count = WindowsInterop.GetMenuItemCount(hMenu);
            int enabledCount = 0;
            for (int i = 0; i < count; i++)
            {
                WindowsInterop.EnableMenuItem(hMenu, (uint)i, WindowsInterop.MfByPosition | WindowsInterop.MfEnabled);
                enabledCount++;
            }
            return enabledCount;
        }

        public static WindowPlacementRect CalculateGravityResize(
            WindowPlacementRect current,
            int newWidth,
            int newHeight,
            WindowGravity gravity)
        {
            int dw = current.Width - newWidth;
            int dh = current.Height - newHeight;

            int x = gravity switch
            {
                WindowGravity.NorthWest => current.X,
                WindowGravity.North => current.X + dw / 2,
                WindowGravity.NorthEast => current.X + dw,
                WindowGravity.West => current.X,
                WindowGravity.Center => current.X + dw / 2,
                WindowGravity.East => current.X + dw,
                WindowGravity.SouthWest => current.X,
                WindowGravity.South => current.X + dw / 2,
                WindowGravity.SouthEast => current.X + dw,
                _ => current.X
            };

            int y = gravity switch
            {
                WindowGravity.NorthWest => current.Y,
                WindowGravity.North => current.Y,
                WindowGravity.NorthEast => current.Y,
                WindowGravity.West => current.Y + dh / 2,
                WindowGravity.Center => current.Y + dh / 2,
                WindowGravity.East => current.Y + dh / 2,
                WindowGravity.SouthWest => current.Y + dh,
                WindowGravity.South => current.Y + dh,
                WindowGravity.SouthEast => current.Y + dh,
                _ => current.Y
            };

            return new WindowPlacementRect(x, y, Math.Max(1, newWidth), Math.Max(1, newHeight));
        }

        public static double CalculateKeyboardStepAcceleration(
            int repeatCount,
            double lastElapsedMs,
            double baseStep = 10.0,
            double maxStep = 100.0,
            double thresholdMs = 200.0)
        {
            if (repeatCount <= 1 || lastElapsedMs > thresholdMs)
                return baseStep;

            double accelFactor = 1.0 + Math.Min(repeatCount - 1, 10) * 0.35;
            double accelerated = baseStep * accelFactor;
            return Math.Clamp(accelerated, baseStep, maxStep);
        }

        public static WindowPlacementRect CycleWindowEdgeFraction(
            WindowPlacementRect current,
            WindowPlacementRect workArea,
            DirectionalGridAction direction)
        {
            int halfW = workArea.Width / 2;
            int thirdW = workArea.Width / 3;
            int twoThirdsW = (workArea.Width * 2) / 3;

            int halfH = workArea.Height / 2;
            int thirdH = workArea.Height / 3;
            int twoThirdsH = (workArea.Height * 2) / 3;

            switch (direction)
            {
                case DirectionalGridAction.Left:
                {
                    int w = (Math.Abs(current.Width - halfW) <= 4) ? thirdW :
                            (Math.Abs(current.Width - thirdW) <= 4) ? twoThirdsW : halfW;
                    return new WindowPlacementRect(workArea.X, workArea.Y, w, workArea.Height);
                }
                case DirectionalGridAction.Right:
                {
                    int w = (Math.Abs(current.Width - halfW) <= 4) ? thirdW :
                            (Math.Abs(current.Width - thirdW) <= 4) ? twoThirdsW : halfW;
                    return new WindowPlacementRect(workArea.Right - w, workArea.Y, w, workArea.Height);
                }
                case DirectionalGridAction.Top:
                {
                    int h = (Math.Abs(current.Height - halfH) <= 4) ? thirdH :
                            (Math.Abs(current.Height - thirdH) <= 4) ? twoThirdsH : halfH;
                    return new WindowPlacementRect(workArea.X, workArea.Y, workArea.Width, h);
                }
                case DirectionalGridAction.Bottom:
                {
                    int h = (Math.Abs(current.Height - halfH) <= 4) ? thirdH :
                            (Math.Abs(current.Height - thirdH) <= 4) ? twoThirdsH : halfH;
                    return new WindowPlacementRect(workArea.X, workArea.Bottom - h, workArea.Width, h);
                }
                default:
                    return workArea;
            }
        }

        public static WindowPlacementRect CycleWindowCenterScale(
            WindowPlacementRect current,
            WindowPlacementRect workArea)
        {
            int fullW = workArea.Width;
            int threeQuartersW = (workArea.Width * 3) / 4;
            int halfW = workArea.Width / 2;

            int targetW;
            if (Math.Abs(current.Width - fullW) <= 4)
                targetW = threeQuartersW;
            else if (Math.Abs(current.Width - threeQuartersW) <= 4)
                targetW = halfW;
            else
                targetW = fullW;

            int targetH = (workArea.Height * targetW) / workArea.Width;
            int x = workArea.X + (workArea.Width - targetW) / 2;
            int y = workArea.Y + (workArea.Height - targetH) / 2;
            return new WindowPlacementRect(x, y, targetW, targetH);
        }

        public static bool ToggleFramelessWindow(nint hwnd, bool makeFrameless)
        {
            if (hwnd == 0) return false;
            const uint targetStyles = WindowsInterop.WsCaption | WindowsInterop.WsThickFrame | WindowsInterop.WsMinimizeBox | WindowsInterop.WsMaximizeBox | WindowsInterop.WsSysMenu | WindowsInterop.WsDlgFrame;
            nint stylePtr = WindowsInterop.GetWindowLongPtr(hwnd, WindowsInterop.GwlStyle);
            long style = stylePtr.ToInt64();
            if (makeFrameless)
            {
                style &= ~targetStyles;
            }
            else
            {
                style |= (WindowsInterop.WsCaption | WindowsInterop.WsThickFrame | WindowsInterop.WsMinimizeBox | WindowsInterop.WsMaximizeBox | WindowsInterop.WsSysMenu);
            }
            WindowsInterop.SetWindowLongPtr(hwnd, WindowsInterop.GwlStyle, new nint(style));
            WindowsInterop.SetWindowPos(hwnd, 0, 0, 0, 0, 0, WindowsInterop.SwpFrameChanged | WindowsInterop.SwpNoMove | WindowsInterop.SwpNoSize | WindowsInterop.SwpNoZOrder | WindowsInterop.SwpNoOwnerZOrder);
            return true;
        }

        public static WindowPlacementRect CalculateSmartMaximizeToggle(
            WindowPlacementRect current,
            WindowPlacementRect workArea,
            int edgeThreshold = 5)
        {
            bool nearTop = current.Y <= workArea.Y + edgeThreshold;
            bool nearBottom = (current.Y + current.Height) >= (workArea.Y + workArea.Height) - edgeThreshold;
            bool nearLeft = current.X <= workArea.X + edgeThreshold;
            bool nearRight = (current.X + current.Width) >= (workArea.X + workArea.Width) - edgeThreshold;

            bool isMaximized = nearTop && nearBottom && nearLeft && nearRight;
            if (isMaximized)
            {
                int restW = (workArea.Width * 3) / 4;
                int restH = (workArea.Height * 3) / 4;
                int restX = workArea.X + (workArea.Width - restW) / 2;
                int restY = workArea.Y + (workArea.Height - restH) / 2;
                return new WindowPlacementRect(restX, restY, restW, restH);
            }

            return workArea;
        }

        public static IReadOnlyList<WindowPlacementRect> CalculateRatioTilingLayout(
            WindowPlacementRect availableArea,
            double ratio,
            int spacing,
            int windowCount)
        {
            if (windowCount <= 0) return Array.Empty<WindowPlacementRect>();
            if (windowCount == 1)
            {
                return [new WindowPlacementRect(
                    availableArea.X + spacing,
                    availableArea.Y + spacing,
                    Math.Max(1, availableArea.Width - spacing * 2),
                    Math.Max(1, availableArea.Height - spacing * 2))];
            }

            ratio = Math.Clamp(ratio, 0.1, 0.9);
            spacing = Math.Max(0, spacing);

            var result = new List<WindowPlacementRect>(windowCount);
            WindowPlacementRect currentArea = availableArea;

            for (int i = 0; i < windowCount; i++)
            {
                if (i == windowCount - 1)
                {
                    result.Add(new WindowPlacementRect(
                        currentArea.X + spacing,
                        currentArea.Y + spacing,
                        Math.Max(1, currentArea.Width - spacing * 2),
                        Math.Max(1, currentArea.Height - spacing * 2)));
                    break;
                }

                if (currentArea.Width >= currentArea.Height)
                {
                    int primaryW = Math.Max(1, (int)(currentArea.Width * ratio) - spacing / 2);
                    int remW = Math.Max(1, currentArea.Width - primaryW - spacing);
                    int h = Math.Max(1, currentArea.Height - spacing * 2);

                    result.Add(new WindowPlacementRect(currentArea.X + spacing, currentArea.Y + spacing, primaryW, h));
                    currentArea = new WindowPlacementRect(currentArea.X + primaryW + spacing, currentArea.Y, remW, currentArea.Height);
                }
                else
                {
                    int primaryH = Math.Max(1, (int)(currentArea.Height * ratio) - spacing / 2);
                    int remH = Math.Max(1, currentArea.Height - primaryH - spacing);
                    int w = Math.Max(1, currentArea.Width - spacing * 2);

                    result.Add(new WindowPlacementRect(currentArea.X + spacing, currentArea.Y + spacing, w, primaryH));
                    currentArea = new WindowPlacementRect(currentArea.X, currentArea.Y + primaryH + spacing, currentArea.Width, remH);
                }
            }

            return result;
        }

        public static (int Rows, int Columns) CalculateGridDimensions(int windowCount, int width, int height)
        {
            if (windowCount <= 0) return (1, 1);
            int a = 1;
            while (a * a < windowCount) a++;
            int b = (windowCount + a - 1) / a;

            if (width > height && a >= b)
            {
                (b, a) = (a, b);
            }

            return (a, b);
        }

        public static (WindowPlacementRect SnappedRect, WindowSnapFlags Flags) CalculateMagneticEdgeSnap(
            WindowPlacementRect window,
            WindowPlacementRect screen,
            int snapThreshold = 12)
        {
            int finalX = window.X;
            int finalY = window.Y;
            WindowSnapFlags flags = WindowSnapFlags.None;

            int screenRight = screen.X + screen.Width;
            int screenBottom = screen.Y + screen.Height;

            if (Math.Abs(window.X - screen.X) <= snapThreshold)
            {
                finalX = screen.X;
                flags |= WindowSnapFlags.Left;
            }
            else if (Math.Abs((window.X + window.Width) - screenRight) <= snapThreshold)
            {
                finalX = screenRight - window.Width;
                flags |= WindowSnapFlags.Right;
            }

            if (Math.Abs(window.Y - screen.Y) <= snapThreshold)
            {
                finalY = screen.Y;
                flags |= WindowSnapFlags.Top;
            }
            else if (Math.Abs((window.Y + window.Height) - screenBottom) <= snapThreshold)
            {
                finalY = screenBottom - window.Height;
                flags |= WindowSnapFlags.Bottom;
            }

            return (new WindowPlacementRect(finalX, finalY, window.Width, window.Height), flags);
        }

        public static double EvaluateCubicBezier(double t, double p0, double p1, double p2, double p3)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            double invT = 1.0 - t;
            return p0 * invT * invT * invT + 3.0 * p1 * invT * invT * t + 3.0 * p2 * invT * t * t + p3 * t * t * t;
        }

        public static double EvaluateCircularEasing(double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            return t < 0.5
                ? (1.0 - Math.Sqrt(1.0 - Math.Pow(2.0 * t, 2.0))) / 2.0
                : (Math.Sqrt(1.0 - Math.Pow(-2.0 * t + 2.0, 2.0)) + 1.0) / 2.0;
        }

        public static double EvaluateBounceEasing(double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            const double n1 = 7.5625;
            const double d1 = 2.75;

            if (t < 1.0 / d1)
            {
                return n1 * t * t;
            }
            if (t < 2.0 / d1)
            {
                t -= 1.5 / d1;
                return n1 * t * t + 0.75;
            }
            if (t < 2.5 / d1)
            {
                t -= 2.25 / d1;
                return n1 * t * t + 0.9375;
            }
            t -= 2.625 / d1;
            return n1 * t * t + 0.984375;
        }

        public static (WindowPlacementRect BestPlacement, ulong OverlapArea) CalculateMinimumOverlapPlacement(
            int width,
            int height,
            WindowPlacementRect workArea,
            IReadOnlyList<WindowPlacementRect> existingWindows)
        {
            width = Math.Clamp(width, 10, Math.Max(10, workArea.Width));
            height = Math.Clamp(height, 10, Math.Max(10, workArea.Height));

            if (existingWindows == null || existingWindows.Count == 0)
            {
                return (new WindowPlacementRect(workArea.X, workArea.Y, width, height), 0);
            }

            var candidates = new List<WindowPlacementRect>
            {
                // Screen corners
                new(workArea.X, workArea.Y, width, height),
                new(Math.Max(workArea.X, workArea.Right - width), workArea.Y, width, height),
                new(workArea.X, Math.Max(workArea.Y, workArea.Bottom - height), width, height),
                new(Math.Max(workArea.X, workArea.Right - width), Math.Max(workArea.Y, workArea.Bottom - height), width, height),
                // Screen center
                new(workArea.X + Math.Max(0, (workArea.Width - width) / 2), workArea.Y + Math.Max(0, (workArea.Height - height) / 2), width, height)
            };

            // Add candidates adjacent to existing windows
            foreach (var win in existingWindows)
            {
                // Right of win
                int rx = Math.Clamp(win.Right, workArea.X, Math.Max(workArea.X, workArea.Right - width));
                int ry = Math.Clamp(win.Y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height));
                candidates.Add(new WindowPlacementRect(rx, ry, width, height));

                // Left of win
                int lx = Math.Clamp(win.X - width, workArea.X, Math.Max(workArea.X, workArea.Right - width));
                int ly = Math.Clamp(win.Y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height));
                candidates.Add(new WindowPlacementRect(lx, ly, width, height));

                // Below win
                int bx = Math.Clamp(win.X, workArea.X, Math.Max(workArea.X, workArea.Right - width));
                int by = Math.Clamp(win.Bottom, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height));
                candidates.Add(new WindowPlacementRect(bx, by, width, height));

                // Above win
                int ax = Math.Clamp(win.X, workArea.X, Math.Max(workArea.X, workArea.Right - width));
                int ay = Math.Clamp(win.Y - height, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height));
                candidates.Add(new WindowPlacementRect(ax, ay, width, height));
            }

            WindowPlacementRect best = candidates[0];
            ulong minOverlap = ulong.MaxValue;

            int[] flatRects = new int[existingWindows.Count * 4];
            for (int i = 0; i < existingWindows.Count; i++)
            {
                flatRects[i * 4] = existingWindows[i].X;
                flatRects[i * 4 + 1] = existingWindows[i].Y;
                flatRects[i * 4 + 2] = existingWindows[i].Width;
                flatRects[i * 4 + 3] = existingWindows[i].Height;
            }

            foreach (var cand in candidates)
            {
                ulong overlap = 0;
                bool nativeSuccess = false;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        unsafe
                        {
                            fixed (int* pRects = flatRects)
                            {
                                ulong nativeOverlap = 0;
                                int status = WinCareCoreNative.WinCareCoreCalculateSmartOverlap(
                                    cand.X, cand.Y, cand.Width, cand.Height,
                                    pRects, (nuint)existingWindows.Count, &nativeOverlap);
                                if (status == 0)
                                {
                                    overlap = nativeOverlap;
                                    nativeSuccess = true;
                                }
                            }
                        }
                    }
                    catch
                    {
                        nativeSuccess = false;
                    }
                }

                if (!nativeSuccess)
                {
                    overlap = 0;
                    foreach (var win in existingWindows)
                    {
                        int ox = Math.Max(0, Math.Min(cand.Right, win.Right) - Math.Max(cand.X, win.X));
                        int oy = Math.Max(0, Math.Min(cand.Bottom, win.Bottom) - Math.Max(cand.Y, win.Y));
                        overlap += (ulong)ox * (ulong)oy;
                    }
                }

                if (overlap < minOverlap)
                {
                    minOverlap = overlap;
                    best = cand;
                    if (minOverlap == 0) break;
                }
            }

            return (best, minOverlap);
        }

        /// <summary>
        /// Calculates relative interactive window geometry for move and resize gestures (TinyWM).
        /// Modes: 1 = Move, 2 = Resize, 3 = Move &amp; Resize.
        /// </summary>
        public static WindowPlacementRect CalculateRelativeWindowDelta(
            int startPointerX,
            int startPointerY,
            int currentPointerX,
            int currentPointerY,
            WindowPlacementRect initialWindow,
            int mode)
        {
            int[] rect = new int[4];
            bool nativeSuccess = false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    unsafe
                    {
                        fixed (int* pRect = rect)
                        {
                            int status = WinCareCoreNative.WinCareCoreCalculateRelativeWindowDelta(
                                startPointerX, startPointerY,
                                currentPointerX, currentPointerY,
                                initialWindow.X, initialWindow.Y,
                                initialWindow.Width, initialWindow.Height,
                                (uint)mode, pRect);

                            if (status == 0)
                            {
                                nativeSuccess = true;
                            }
                        }
                    }
                }
                catch
                {
                    nativeSuccess = false;
                }
            }

            if (nativeSuccess)
            {
                return new WindowPlacementRect(rect[0], rect[1], rect[2], rect[3]);
            }

            // Managed fallback
            int xdiff = currentPointerX - startPointerX;
            int ydiff = currentPointerY - startPointerY;

            return mode switch
            {
                1 => new WindowPlacementRect(initialWindow.X + xdiff, initialWindow.Y + ydiff, Math.Max(1, initialWindow.Width), Math.Max(1, initialWindow.Height)),
                2 => new WindowPlacementRect(initialWindow.X, initialWindow.Y, Math.Max(1, initialWindow.Width + xdiff), Math.Max(1, initialWindow.Height + ydiff)),
                3 => new WindowPlacementRect(initialWindow.X + xdiff, initialWindow.Y + ydiff, Math.Max(1, initialWindow.Width + xdiff), Math.Max(1, initialWindow.Height + ydiff)),
                _ => initialWindow
            };
        }

        public static WindowPlacementRect CalculateFuzzyCascadePlacement(
            int width,
            int height,
            WindowPlacementRect workArea,
            IReadOnlyList<WindowPlacementRect> existingWindows,
            int cascadeFuzz = 15,
            int cascadeInterval = 50)
        {
            width = Math.Clamp(width, 10, Math.Max(10, workArea.Width));
            height = Math.Clamp(height, 10, Math.Max(10, workArea.Height));

            if (existingWindows == null || existingWindows.Count == 0)
            {
                return new WindowPlacementRect(workArea.X, workArea.Y, width, height);
            }

            var sorted = existingWindows
                .OrderBy(w => Math.Sqrt((double)w.X * w.X + (double)w.Y * w.Y))
                .ToList();
            int cascadeX = Math.Max(0, workArea.X);
            int cascadeY = Math.Max(0, workArea.Y);
            int cascadeStage = 0;

            for (int step = 0; step < 100; step++)
            {
                bool occupied = false;
                foreach (var w in existingWindows)
                {
                    if (Math.Abs(w.X - cascadeX) < cascadeFuzz && Math.Abs(w.Y - cascadeY) < cascadeFuzz)
                    {
                        occupied = true;
                        break;
                    }
                }

                if (!occupied)
                {
                    break;
                }

                cascadeX += cascadeInterval;
                cascadeY += cascadeInterval;

                if ((cascadeX + width > workArea.Right) || (cascadeY + height > workArea.Bottom))
                {
                    cascadeStage++;
                    cascadeX = Math.Max(0, workArea.X) + cascadeInterval * cascadeStage;
                    cascadeY = Math.Max(0, workArea.Y);

                    if (cascadeX + width > workArea.Right)
                    {
                        cascadeX = Math.Max(0, workArea.X);
                        cascadeY = Math.Max(0, workArea.Y);
                        break;
                    }
                }
            }

            return new WindowPlacementRect(cascadeX, cascadeY, width, height);
        }

        public static WindowPlacementRect EvaluateMiroStepCycle(
            WindowPlacementRect currentBounds,
            WindowPlacementRect workArea,
            MiroCycleDirection direction,
            int stepIndex)
        {
            double[] edgeFractions = [0.5, 1.0 / 3.0, 2.0 / 3.0];
            double[] fsFractions = [1.0, 0.75, 0.5];

            int edgeIdx = Math.Abs(stepIndex) % edgeFractions.Length;
            int fsIdx = Math.Abs(stepIndex) % fsFractions.Length;

            double frac = edgeFractions[edgeIdx];

            switch (direction)
            {
                case MiroCycleDirection.Left:
                {
                    int w = (int)(workArea.Width * frac);
                    return new WindowPlacementRect(workArea.X, workArea.Y, w, workArea.Height);
                }
                case MiroCycleDirection.Right:
                {
                    int w = (int)(workArea.Width * frac);
                    return new WindowPlacementRect(workArea.Right - w, workArea.Y, w, workArea.Height);
                }
                case MiroCycleDirection.Top:
                {
                    int h = (int)(workArea.Height * frac);
                    return new WindowPlacementRect(workArea.X, workArea.Y, workArea.Width, h);
                }
                case MiroCycleDirection.Bottom:
                {
                    int h = (int)(workArea.Height * frac);
                    return new WindowPlacementRect(workArea.X, workArea.Bottom - h, workArea.Width, h);
                }
                case MiroCycleDirection.TopLeft:
                {
                    int w = (int)(workArea.Width * frac);
                    int h = (int)(workArea.Height * frac);
                    return new WindowPlacementRect(workArea.X, workArea.Y, w, h);
                }
                case MiroCycleDirection.TopRight:
                {
                    int w = (int)(workArea.Width * frac);
                    int h = (int)(workArea.Height * frac);
                    return new WindowPlacementRect(workArea.Right - w, workArea.Y, w, h);
                }
                case MiroCycleDirection.BottomLeft:
                {
                    int w = (int)(workArea.Width * frac);
                    int h = (int)(workArea.Height * frac);
                    return new WindowPlacementRect(workArea.X, workArea.Bottom - h, w, h);
                }
                case MiroCycleDirection.BottomRight:
                {
                    int w = (int)(workArea.Width * frac);
                    int h = (int)(workArea.Height * frac);
                    return new WindowPlacementRect(workArea.Right - w, workArea.Bottom - h, w, h);
                }
                case MiroCycleDirection.Fullscreen:
                default:
                {
                    double fsFrac = fsFractions[fsIdx];
                    int w = (int)(workArea.Width * fsFrac);
                    int h = (int)(workArea.Height * fsFrac);
                    int x = workArea.X + (workArea.Width - w) / 2;
                    int y = workArea.Y + (workArea.Height - h) / 2;
                    return new WindowPlacementRect(x, y, w, h);
                }
            }
        }

        public static bool SetWindowOpacity(nint hwnd, int alphaPercent)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            alphaPercent = Math.Clamp(alphaPercent, 10, 100);
            byte alphaByte = (byte)(alphaPercent * 255 / 100);

            nint exStyle = WindowsInterop.GetWindowLongPtr(hwnd, WindowsInterop.GwlExStyle);
            if ((exStyle.ToInt64() & WindowsInterop.WsExLayered) == 0)
            {
                WindowsInterop.SetWindowLongPtr(hwnd, WindowsInterop.GwlExStyle, new nint(exStyle.ToInt64() | WindowsInterop.WsExLayered));
            }

            return WindowsInterop.SetLayeredWindowAttributes(hwnd, 0, alphaByte, WindowsInterop.LwaAlpha);
        }

        public static int GetWindowOpacity(nint hwnd)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return 100;

            if (WindowsInterop.GetLayeredWindowAttributes(hwnd, out _, out byte pbAlpha, out uint pdwFlags))
            {
                if ((pdwFlags & WindowsInterop.LwaAlpha) != 0)
                {
                    return (int)Math.Round(pbAlpha * 100.0 / 255.0);
                }
            }
            return 100;
        }

        public static bool AdjustWindowProcessPriority(nint hwnd, uint priorityClass)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            WindowsInterop.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return false;

            const uint ProcessSetInformation = 0x0200;
            nint hProcess = WindowsInterop.OpenProcess(ProcessSetInformation, false, pid);
            if (hProcess == 0) return false;

            try
            {
                return WindowsInterop.SetPriorityClass(hProcess, priorityClass);
            }
            finally
            {
                WindowsInterop.CloseHandle(hProcess);
            }
        }

        public static (bool Succeeded, bool IsOnCurrentDesktop, Guid DesktopId) QueryVirtualDesktopStatus(nint hwnd)
        {
            if (hwnd == 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return (false, false, Guid.Empty);

            try
            {
                var vdm = (WindowsInterop.IVirtualDesktopManager)new WindowsInterop.VirtualDesktopManagerClass();
                int hrActive = vdm.IsWindowOnCurrentVirtualDesktop(hwnd, out bool onCurrent);
                int hrId = vdm.GetWindowDesktopId(hwnd, out Guid desktopId);

                return (hrActive == 0 && hrId == 0, onCurrent, desktopId);
            }
            catch
            {
                return (false, false, Guid.Empty);
            }
        }
    }

    public enum MiroCycleDirection
    {
        Left,
        Right,
        Top,
        Bottom,
        Fullscreen,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public sealed class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private int _start;
        private int _end;
        private int _count;

        public int Capacity => _buffer.Length;
        public int Count => _count;
        public bool IsFull => _count == _buffer.Length;

        public CircularBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
            _buffer = new T[capacity];
        }

        public void Push(T item)
        {
            _buffer[_end] = item;
            _end = (_end + 1) % _buffer.Length;
            if (_count < _buffer.Length)
            {
                _count++;
            }
            else
            {
                _start = (_start + 1) % _buffer.Length;
            }
        }

        public IReadOnlyList<T> ToList()
        {
            var result = new List<T>(_count);
            for (int i = 0; i < _count; i++)
            {
                result.Add(_buffer[(_start + i) % _buffer.Length]);
            }
            return result;
        }
    }

    [Flags]
    public enum WindowSnapFlags
    {
        None = 0,
        Left = 1 << 0,
        Top = 1 << 1,
        Right = 1 << 2,
        Bottom = 1 << 3
    }

    public static class VirtualDesktopManagerGuidConstants
    {
        public const string ClsidVirtualDesktopManager = "{aa509086-5ca9-4c25-8f95-589d3c07b48a}";
        public const string IidVirtualDesktopManager = "{a5cd92ff-29be-454c-8d04-d82879fb3f1b}";
        public const int VtblSlotIsWindowOnCurrentDesktop = 3;
        public const int VtblSlotGetWindowDesktopId = 4;
        public const int VtblSlotMoveWindowToDesktop = 5;
    }

    public enum SplitOrientation
    {
        Horizontal,
        Vertical
    }

    public enum TilingLayoutMode
    {
        BinarySplit,
        EqualColumns,
        EqualRows,
        Grid2x2,
        Cascade
    }

    public enum ScratchpadAnchor
    {
        TopCenter,
        Center,
        BottomCenter
    }

    public readonly record struct WindowPlacementRect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;
    }

    public sealed record SplitPaneResult(
        WindowPlacementRect FirstPane,
        WindowPlacementRect SecondPane,
        SplitOrientation Orientation,
        double Ratio,
        int Margin);

    public sealed record WindowMagneticSnapResult(
        int X,
        int Y,
        int Width,
        int Height,
        bool SnappedScreenLeft,
        bool SnappedScreenRight,
        bool SnappedScreenTop,
        bool SnappedScreenBottom,
        bool SnappedToWindow);

    public sealed record VirtualDesktopAuditResult(
        int DesktopCount,
        string CurrentDesktopId,
        IReadOnlyList<string> DesktopIds,
        bool VirtualDesktopsEnabled,
        string Summary);

    public sealed record EdgeSnapResistanceAuditResult(
        bool WindowArrangementActive,
        bool SnapFillEnabled,
        bool SnapAssistEnabled,
        bool JointResizeEnabled,
        bool DockMovingEnabled,
        int EdgeSnapThresholdPixels,
        string Summary);

    public sealed record WindowStyleItem(
        long Handle,
        string Title,
        string ClassName,
        int X,
        int Y,
        int Width,
        int Height,
        bool IsTopmost,
        bool IsLayered,
        bool HasCaption,
        bool HasThickFrame,
        bool IsFrameless,
        byte? Alpha);

    public sealed record WindowStyleTransparencyReport(
        int TotalWindowsAudited,
        int TopmostCount,
        int LayeredCount,
        int FramelessCount,
        IReadOnlyList<WindowStyleItem> Windows,
        string Summary);

    public sealed record SnapBounds(
        int X,
        int Y,
        int Width,
        int Height,
        bool SnappedLeft,
        bool SnappedRight,
        bool SnappedTop,
        bool SnappedBottom);

    public sealed record GridBounds(
        int X,
        int Y,
        int Width,
        int Height,
        int Column,
        int Row,
        int ColumnSpan,
        int RowSpan);

    public enum DirectionalGridAction
    {
        Left,
        Right,
        Top,
        Bottom,
        Center
    }

    public enum WindowRuleTarget
    {
        ProcessName,
        ClassName,
        Title
    }

    public sealed record WindowShadeResult(
        WindowPlacementRect Bounds,
        bool IsShaded,
        int OriginalHeight);

    public enum WindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }

    public enum WindowBackdropType
    {
        Auto = 0,
        None = 1,
        Mica = 2,
        Acrylic = 3,
        MicaAlt = 4
    }

    public enum WindowResizeEdge
    {
        Left = 1,
        Right = 2,
        Top = 3,
        TopLeft = 4,
        TopRight = 5,
        Bottom = 6,
        BottomLeft = 7,
        BottomRight = 8
    }

    public enum WindowGravity
    {
        NorthWest = 1,
        North = 2,
        NorthEast = 3,
        West = 4,
        Center = 5,
        East = 6,
        SouthWest = 7,
        South = 8,
        SouthEast = 9
    }
}

