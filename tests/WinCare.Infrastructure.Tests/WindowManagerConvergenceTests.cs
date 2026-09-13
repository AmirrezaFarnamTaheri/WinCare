using System.Diagnostics;
using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class WindowManagerConvergenceTests
{
    [Fact]
    public void WindowManager_AuditVirtualDesktops_returns_valid_structure()
    {
        var result = WindowsCommandExecutor.WindowManagerHelper.AuditVirtualDesktops();

        Assert.NotNull(result);
        Assert.True(result.DesktopCount >= 1);
        Assert.False(string.IsNullOrWhiteSpace(result.CurrentDesktopId));
        Assert.NotNull(result.DesktopIds);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
    }

    [Fact]
    public void WindowManager_AuditEdgeSnapResistance_returns_valid_structure()
    {
        var result = WindowsCommandExecutor.WindowManagerHelper.AuditEdgeSnapResistance();

        Assert.NotNull(result);
        Assert.Equal(16, result.EdgeSnapThresholdPixels);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
    }

    [Fact]
    public void WindowManager_ProbeWindowStylesAndTransparency_enumerates_windows()
    {
        var report = WindowsCommandExecutor.WindowManagerHelper.ProbeWindowStylesAndTransparency(maxWindows: 50);

        Assert.NotNull(report);
        Assert.True(report.TotalWindowsAudited >= 0);
        Assert.NotNull(report.Windows);
        Assert.False(string.IsNullOrWhiteSpace(report.Summary));
    }

    [Fact]
    public void WindowManager_CalculateMagneticSnap_snaps_to_screen_borders_within_threshold()
    {
        const int screenWidth = 2560;
        const int screenHeight = 1440;
        const int windowWidth = 800;
        const int windowHeight = 600;

        // 1. Snap to Left edge (currentX = 10 <= 16 threshold)
        var leftSnap = WindowsCommandExecutor.WindowManagerHelper.CalculateMagneticSnap(
            currentX: 10,
            currentY: 400,
            width: windowWidth,
            height: windowHeight,
            screenWidth: screenWidth,
            screenHeight: screenHeight,
            snapThreshold: 16);

        Assert.True(leftSnap.SnappedLeft);
        Assert.Equal(0, leftSnap.X);
        Assert.Equal(400, leftSnap.Y);

        // 2. Snap to Right edge (currentX = 1750, width = 800 => right edge = 2550, within 10px of 2560)
        var rightSnap = WindowsCommandExecutor.WindowManagerHelper.CalculateMagneticSnap(
            currentX: 1750,
            currentY: 400,
            width: windowWidth,
            height: windowHeight,
            screenWidth: screenWidth,
            screenHeight: screenHeight,
            snapThreshold: 16);

        Assert.True(rightSnap.SnappedRight);
        Assert.Equal(screenWidth - windowWidth, rightSnap.X);

        // 3. Snap to Top edge (currentY = 8 <= 16 threshold)
        var topSnap = WindowsCommandExecutor.WindowManagerHelper.CalculateMagneticSnap(
            currentX: 500,
            currentY: 8,
            width: windowWidth,
            height: windowHeight,
            screenWidth: screenWidth,
            screenHeight: screenHeight,
            snapThreshold: 16);

        Assert.True(topSnap.SnappedTop);
        Assert.Equal(0, topSnap.Y);

        // 4. Snap to Bottom edge (currentY = 835, height = 600 => bottom = 1435, within 5px of 1440)
        var bottomSnap = WindowsCommandExecutor.WindowManagerHelper.CalculateMagneticSnap(
            currentX: 500,
            currentY: 835,
            width: windowWidth,
            height: windowHeight,
            screenWidth: screenWidth,
            screenHeight: screenHeight,
            snapThreshold: 16);

        Assert.True(bottomSnap.SnappedBottom);
        Assert.Equal(screenHeight - windowHeight, bottomSnap.Y);

        // 5. Far from edges -> no snap
        var centerNoSnap = WindowsCommandExecutor.WindowManagerHelper.CalculateMagneticSnap(
            currentX: 500,
            currentY: 500,
            width: windowWidth,
            height: windowHeight,
            screenWidth: screenWidth,
            screenHeight: screenHeight,
            snapThreshold: 16);

        Assert.False(centerNoSnap.SnappedLeft);
        Assert.False(centerNoSnap.SnappedRight);
        Assert.False(centerNoSnap.SnappedTop);
        Assert.False(centerNoSnap.SnappedBottom);
        Assert.Equal(500, centerNoSnap.X);
        Assert.Equal(500, centerNoSnap.Y);
    }

    [Fact]
    public void WindowManager_CalculateFractionalGrid_computes_exact_grid_bounds()
    {
        const int screenWidth = 1920;
        const int screenHeight = 1080;
        const int totalCols = 24;
        const int totalRows = 24;

        // Left half (columns 0..11, rows 0..23)
        var leftHalf = WindowsCommandExecutor.WindowManagerHelper.CalculateFractionalGrid(
            col: 0,
            row: 0,
            colSpan: 12,
            rowSpan: 24,
            totalCols: totalCols,
            totalRows: totalRows,
            screenWidth: screenWidth,
            screenHeight: screenHeight);

        Assert.Equal(0, leftHalf.X);
        Assert.Equal(0, leftHalf.Y);
        Assert.Equal(960, leftHalf.Width);
        Assert.Equal(1080, leftHalf.Height);

        // Right half (columns 12..23, rows 0..23)
        var rightHalf = WindowsCommandExecutor.WindowManagerHelper.CalculateFractionalGrid(
            col: 12,
            row: 0,
            colSpan: 12,
            rowSpan: 24,
            totalCols: totalCols,
            totalRows: totalRows,
            screenWidth: screenWidth,
            screenHeight: screenHeight);

        Assert.Equal(960, rightHalf.X);
        Assert.Equal(0, rightHalf.Y);
        Assert.Equal(960, rightHalf.Width);
        Assert.Equal(1080, rightHalf.Height);
    }

    [Fact]
    public void WindowManager_InteropSafety_with_zero_hwnd()
    {
        bool transResult = WindowsCommandExecutor.WindowManagerHelper.SetWindowTransparency(0, 200);
        Assert.False(transResult);

        bool frameResult = WindowsCommandExecutor.WindowManagerHelper.ToggleFramelessStyle(0, true);
        Assert.False(frameResult);
    }

    [Fact]
    public void SplitPaneLayout_horizontal_computes_exact_bounds()
    {
        var bounds = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);
        var result = WindowsCommandExecutor.WindowManagerHelper.SplitPaneLayout(
            bounds,
            WindowsCommandExecutor.SplitOrientation.Horizontal,
            ratio: 0.5,
            margin: 4);

        Assert.Equal(0, result.FirstPane.X);
        Assert.Equal(0, result.FirstPane.Y);
        Assert.Equal(958, result.FirstPane.Width);
        Assert.Equal(1080, result.FirstPane.Height);

        Assert.Equal(962, result.SecondPane.X);
        Assert.Equal(0, result.SecondPane.Y);
        Assert.Equal(958, result.SecondPane.Width);
        Assert.Equal(1080, result.SecondPane.Height);

        Assert.Equal(1920, result.FirstPane.Width + result.SecondPane.Width + result.Margin);
    }

    [Fact]
    public void SplitPaneLayout_vertical_custom_ratio_and_margin()
    {
        var bounds = new WindowsCommandExecutor.WindowPlacementRect(100, 100, 1000, 800);
        var result = WindowsCommandExecutor.WindowManagerHelper.SplitPaneLayout(
            bounds,
            WindowsCommandExecutor.SplitOrientation.Vertical,
            ratio: 0.7,
            margin: 10);

        Assert.Equal(100, result.FirstPane.X);
        Assert.Equal(100, result.FirstPane.Y);
        Assert.Equal(1000, result.FirstPane.Width);
        Assert.Equal(553, result.FirstPane.Height);

        Assert.Equal(100, result.SecondPane.X);
        Assert.Equal(663, result.SecondPane.Y);
        Assert.Equal(1000, result.SecondPane.Width);
        Assert.Equal(237, result.SecondPane.Height);

        Assert.Equal(800, result.FirstPane.Height + result.SecondPane.Height + result.Margin);
    }

    [Fact]
    public void CascadePlacement_computes_stepped_positions_and_wraps()
    {
        var workArea = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);
        var windows = new[]
        {
            new WindowsCommandExecutor.WindowPlacementRect(0, 0, 600, 400),
            new WindowsCommandExecutor.WindowPlacementRect(0, 0, 600, 400),
            new WindowsCommandExecutor.WindowPlacementRect(0, 0, 600, 400),
            new WindowsCommandExecutor.WindowPlacementRect(0, 0, 600, 400),
        };

        var placed = WindowsCommandExecutor.WindowManagerHelper.CascadePlacement(
            windows,
            workArea,
            titlebarHeight: 32,
            stepOffset: 30);

        Assert.Equal(4, placed.Count);
        Assert.Equal(0, placed[0].X);
        Assert.Equal(0, placed[0].Y);

        Assert.Equal(30, placed[1].X);
        Assert.Equal(32, placed[1].Y);

        Assert.Equal(60, placed[2].X);
        Assert.Equal(64, placed[2].Y);

        Assert.Equal(90, placed[3].X);
        Assert.Equal(96, placed[3].Y);
    }

    [Fact]
    public void CalculateWindowMagneticSnap_snaps_to_neighbor_window()
    {
        var neighbor = new WindowsCommandExecutor.WindowPlacementRect(100, 100, 400, 300); // Right: 500, Bottom: 400
        var neighbors = new[] { neighbor };

        // Moving window (width 300, height 200) located at X=510 (10px from neighbor Right), Y=105 (5px from neighbor Top)
        var snap = WindowsCommandExecutor.WindowManagerHelper.CalculateWindowMagneticSnap(
            currentX: 510,
            currentY: 105,
            width: 300,
            height: 200,
            neighborWindows: neighbors,
            screenWidth: 2560,
            screenHeight: 1440,
            snapThreshold: 16);

        Assert.True(snap.SnappedToWindow);
        Assert.Equal(500, snap.X); // Snapped to neighbor.Right
        Assert.Equal(100, snap.Y); // Snapped to neighbor.Top alignment
    }

    [Fact]
    public void CalculateUsableWorkArea_subtracts_taskbar_and_dock_struts()
    {
        var screen = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);
        var bottomTaskbar = new WindowsCommandExecutor.WindowPlacementRect(0, 1040, 1920, 40);

        var usable = WindowsCommandExecutor.WindowManagerHelper.CalculateUsableWorkArea(screen, new[] { bottomTaskbar });

        Assert.Equal(0, usable.X);
        Assert.Equal(0, usable.Y);
        Assert.Equal(1920, usable.Width);
        Assert.Equal(1040, usable.Height);
    }

    [Fact]
    public void CalculateTiledWindows_equal_columns_and_binary_split()
    {
        var workArea = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);
        var windows = new[]
        {
            new WindowsCommandExecutor.WindowPlacementRect(0, 0, 500, 500),
            new WindowsCommandExecutor.WindowPlacementRect(0, 0, 500, 500),
            new WindowsCommandExecutor.WindowPlacementRect(0, 0, 500, 500),
        };

        var cols = WindowsCommandExecutor.WindowManagerHelper.CalculateTiledWindows(
            windows,
            workArea,
            WindowsCommandExecutor.TilingLayoutMode.EqualColumns,
            margin: 4);

        Assert.Equal(3, cols.Count);
        Assert.Equal(0, cols[0].X);
        Assert.Equal(1080, cols[0].Height);
        Assert.True(cols[1].X > cols[0].Right);
        Assert.True(cols[2].X > cols[1].Right);
        Assert.Equal(1920, cols[2].Right);

        var binary = WindowsCommandExecutor.WindowManagerHelper.CalculateTiledWindows(
            windows,
            workArea,
            WindowsCommandExecutor.TilingLayoutMode.BinarySplit,
            margin: 4);

        Assert.Equal(3, binary.Count);
        Assert.Equal(0, binary[0].X);
        Assert.True(binary[0].Width > 1000); // 60% master pane
        Assert.Equal(binary[1].X, binary[2].X); // Stack pane column
    }

    [Fact]
    public void VirtualDesktopManagerGuidConstants_and_vtbl_slots()
    {
        Assert.Equal("{aa509086-5ca9-4c25-8f95-589d3c07b48a}", WindowsCommandExecutor.VirtualDesktopManagerGuidConstants.ClsidVirtualDesktopManager);
        Assert.Equal("{a5cd92ff-29be-454c-8d04-d82879fb3f1b}", WindowsCommandExecutor.VirtualDesktopManagerGuidConstants.IidVirtualDesktopManager);
        Assert.Equal(3, WindowsCommandExecutor.VirtualDesktopManagerGuidConstants.VtblSlotIsWindowOnCurrentDesktop);
        Assert.Equal(4, WindowsCommandExecutor.VirtualDesktopManagerGuidConstants.VtblSlotGetWindowDesktopId);
        Assert.Equal(5, WindowsCommandExecutor.VirtualDesktopManagerGuidConstants.VtblSlotMoveWindowToDesktop);
    }

    [Fact]
    public void ClampOrCenter_clamps_and_centers_correctly()
    {
        var bounds = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);

        // Within bounds
        var inside = new WindowsCommandExecutor.WindowPlacementRect(100, 100, 800, 600);
        var resInside = WindowsCommandExecutor.WindowManagerHelper.ClampOrCenter(inside, bounds);
        Assert.Equal(100, resInside.X);
        Assert.Equal(100, resInside.Y);

        // Offscreen negative
        var offLeft = new WindowsCommandExecutor.WindowPlacementRect(-50, -20, 800, 600);
        var resOffLeft = WindowsCommandExecutor.WindowManagerHelper.ClampOrCenter(offLeft, bounds);
        Assert.Equal(0, resOffLeft.X);
        Assert.Equal(0, resOffLeft.Y);

        // Offscreen positive
        var offRight = new WindowsCommandExecutor.WindowPlacementRect(1500, 800, 800, 600);
        var resOffRight = WindowsCommandExecutor.WindowManagerHelper.ClampOrCenter(offRight, bounds);
        Assert.Equal(1920 - 800, resOffRight.X);
        Assert.Equal(1080 - 600, resOffRight.Y);

        // Oversized window centered
        var oversized = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 2560, 1440);
        var resOversized = WindowsCommandExecutor.WindowManagerHelper.ClampOrCenter(oversized, bounds);
        Assert.Equal((1920 - 2560) / 2, resOversized.X);
        Assert.Equal((1080 - 1440) / 2, resOversized.Y);
    }

    [Fact]
    public void ConstrainSize_enforces_bounds_and_aspect_ratio()
    {
        var win = new WindowsCommandExecutor.WindowPlacementRect(10, 10, 50, 40);

        // Clamping to min bounds
        var clamped = WindowsCommandExecutor.WindowManagerHelper.ConstrainSize(win, minWidth: 200, minHeight: 150);
        Assert.Equal(200, clamped.Width);
        Assert.Equal(150, clamped.Height);

        // Aspect ratio 16:9
        var aspectWin = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1600, 1200);
        var resAspect = WindowsCommandExecutor.WindowManagerHelper.ConstrainSize(aspectWin, aspectRatioX: 16, aspectRatioY: 9);
        Assert.Equal(1600, resAspect.Width);
        Assert.Equal(900, resAspect.Height);
    }

    [Fact]
    public void CalculateSmartColPlacement_finds_first_free_non_overlapping_slot()
    {
        var workArea = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);
        var existing = new List<WindowsCommandExecutor.WindowPlacementRect>
        {
            new(0, 0, 800, 600)
        };

        var placed = WindowsCommandExecutor.WindowManagerHelper.CalculateSmartColPlacement(workArea, existing, 600, 400);

        // Verify no overlap
        bool overlaps = placed.X < existing[0].Right && placed.Right > existing[0].X &&
                        placed.Y < existing[0].Bottom && placed.Bottom > existing[0].Y;
        Assert.False(overlaps);
        Assert.Equal(600, placed.Width);
        Assert.Equal(400, placed.Height);
    }

    [Fact]
    public void CycleDirectionalGrid_cycles_fractions_and_center_modes()
    {
        var workArea = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);

        // Left cycle: 0 -> 1/2, 1 -> 2/3, 2 -> 1/3
        var left0 = WindowsCommandExecutor.WindowManagerHelper.CycleDirectionalGrid(workArea, WindowsCommandExecutor.DirectionalGridAction.Left, 0);
        Assert.Equal(0, left0.X);
        Assert.Equal(960, left0.Width);

        var left1 = WindowsCommandExecutor.WindowManagerHelper.CycleDirectionalGrid(workArea, WindowsCommandExecutor.DirectionalGridAction.Left, 1);
        Assert.Equal(0, left1.X);
        Assert.Equal((1920 * 2) / 3, left1.Width);

        var left2 = WindowsCommandExecutor.WindowManagerHelper.CycleDirectionalGrid(workArea, WindowsCommandExecutor.DirectionalGridAction.Left, 2);
        Assert.Equal(0, left2.X);
        Assert.Equal(1920 / 3, left2.Width);

        // Center cycle: 0 -> full, 1 -> 3/4, 2 -> 1/2
        var center0 = WindowsCommandExecutor.WindowManagerHelper.CycleDirectionalGrid(workArea, WindowsCommandExecutor.DirectionalGridAction.Center, 0);
        Assert.Equal(0, center0.X);
        Assert.Equal(1920, center0.Width);
        Assert.Equal(1080, center0.Height);

        var center1 = WindowsCommandExecutor.WindowManagerHelper.CycleDirectionalGrid(workArea, WindowsCommandExecutor.DirectionalGridAction.Center, 1);
        Assert.Equal((1920 * 3) / 4, center1.Width);
        Assert.Equal((1080 * 3) / 4, center1.Height);
    }

    [Fact]
    public void CalculateShadedWindow_collapses_and_restores_height()
    {
        var win = new WindowsCommandExecutor.WindowPlacementRect(50, 50, 800, 600);

        // Shade window
        var shaded = WindowsCommandExecutor.WindowManagerHelper.CalculateShadedWindow(win, isCurrentlyShaded: false, titlebarCaptionHeight: 32);
        Assert.True(shaded.IsShaded);
        Assert.Equal(32, shaded.Bounds.Height);
        Assert.Equal(600, shaded.OriginalHeight);

        // Unshade window
        var unshaded = WindowsCommandExecutor.WindowManagerHelper.CalculateShadedWindow(shaded.Bounds, isCurrentlyShaded: true, unshadedHeight: shaded.OriginalHeight);
        Assert.False(unshaded.IsShaded);
        Assert.Equal(600, unshaded.Bounds.Height);
    }

    [Fact]
    public void MatchesWindowRule_matches_wildcards_and_regex()
    {
        Assert.True(WindowsCommandExecutor.WindowManagerHelper.MatchesWindowRule(
            processName: "notepad",
            className: "Notepad",
            title: "Untitled - Notepad",
            rulePattern: "notepad",
            target: WindowsCommandExecutor.WindowRuleTarget.ProcessName));

        Assert.True(WindowsCommandExecutor.WindowManagerHelper.MatchesWindowRule(
            processName: "code",
            className: "Chrome_WidgetWin_1",
            title: "WinCare - Visual Studio Code",
            rulePattern: ".*Visual Studio Code.*",
            target: WindowsCommandExecutor.WindowRuleTarget.Title));

        Assert.False(WindowsCommandExecutor.WindowManagerHelper.MatchesWindowRule(
            processName: "explorer",
            className: "CabinetWClass",
            title: "Downloads",
            rulePattern: "^notepad$",
            target: WindowsCommandExecutor.WindowRuleTarget.ProcessName));
    }

    [Fact]
    public void WindowCornerPreference_and_BackdropType_zero_handle_safety()
    {
        Assert.False(WindowsCommandExecutor.WindowManagerHelper.SetWindowCornerPreference(0, WindowsCommandExecutor.WindowCornerPreference.Round));
        Assert.Null(WindowsCommandExecutor.WindowManagerHelper.GetWindowCornerPreference(0));

        Assert.False(WindowsCommandExecutor.WindowManagerHelper.SetWindowBackdropType(0, WindowsCommandExecutor.WindowBackdropType.Mica));
        Assert.Null(WindowsCommandExecutor.WindowManagerHelper.GetWindowBackdropType(0));
    }

    [Fact]
    public void WindowProcessPriority_and_ForceActivate_zero_handle_safety()
    {
        Assert.False(WindowsCommandExecutor.WindowManagerHelper.SetWindowProcessPriority(0, ProcessPriorityClass.High));
        Assert.Null(WindowsCommandExecutor.WindowManagerHelper.GetWindowProcessPriority(0));
        Assert.False(WindowsCommandExecutor.WindowManagerHelper.ForceActivateWindow(0));
    }

    [Fact]
    public void CalculateRatioMasterStack_single_and_multiple_windows()
    {
        var area = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);

        var single = WindowsCommandExecutor.WindowManagerHelper.CalculateRatioMasterStack(area, 1);
        Assert.Single(single);
        Assert.Equal(area, single[0]);

        var triple = WindowsCommandExecutor.WindowManagerHelper.CalculateRatioMasterStack(area, 3, masterRatio: 0.60, spacing: 10);
        Assert.Equal(3, triple.Count);

        var master = triple[0];
        Assert.Equal(0, master.X);
        Assert.Equal(0, master.Y);
        Assert.Equal(1080, master.Height);
        Assert.True(master.Width > 1000 && master.Width < 1200);

        var stack1 = triple[1];
        var stack2 = triple[2];
        Assert.Equal(master.Right + 10, stack1.X);
        Assert.Equal(master.Right + 10, stack2.X);
        Assert.Equal(0, stack1.Y);
        Assert.Equal(stack1.Bottom + 10, stack2.Y);
        Assert.Equal(1080, stack1.Height + stack2.Height + 10);
    }

    [Fact]
    public void CalculateUniformGridLayout_partitions_windows_evenly()
    {
        var area = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);
        var layout4 = WindowsCommandExecutor.WindowManagerHelper.CalculateUniformGridLayout(area, 4, spacing: 8);

        Assert.Equal(4, layout4.Count);
        // 2x2 grid: top-left, top-right, bottom-left, bottom-right
        Assert.Equal(0, layout4[0].X);
        Assert.Equal(0, layout4[0].Y);
        Assert.Equal(layout4[0].Right + 8, layout4[1].X);
        Assert.Equal(0, layout4[1].Y);

        Assert.Equal(0, layout4[2].X);
        Assert.Equal(layout4[0].Bottom + 8, layout4[2].Y);
        Assert.Equal(layout4[2].Right + 8, layout4[3].X);
        Assert.Equal(layout4[1].Bottom + 8, layout4[3].Y);
    }

    [Fact]
    public void CalculateMinOverlapPlacement_finds_best_location()
    {
        var workArea = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1000, 800);

        // With no existing windows, places at top-left
        var p1 = WindowsCommandExecutor.WindowManagerHelper.CalculateMinOverlapPlacement(workArea, 400, 300, []);
        Assert.Equal(0, p1.X);
        Assert.Equal(0, p1.Y);

        // With an existing window occupying (0, 0, 500, 500), candidate should find an unoccluded space
        var existing = new[] { new WindowsCommandExecutor.WindowPlacementRect(0, 0, 500, 500) };
        var p2 = WindowsCommandExecutor.WindowManagerHelper.CalculateMinOverlapPlacement(workArea, 400, 300, existing, step: 20);

        // Check that p2 does not overlap with existing window or has 0 overlap area
        int overlapLeft = Math.Max(p2.X, existing[0].X);
        int overlapRight = Math.Min(p2.Right, existing[0].Right);
        int overlapTop = Math.Max(p2.Y, existing[0].Y);
        int overlapBottom = Math.Min(p2.Bottom, existing[0].Bottom);

        bool hasOverlap = overlapRight > overlapLeft && overlapBottom > overlapTop;
        Assert.False(hasOverlap, "Placement should find a slot with zero overlap");
    }

    [Fact]
    public void CalculateWindowToWindowSnap_snaps_to_neighbor_edges()
    {
        var neighbor = new WindowsCommandExecutor.WindowPlacementRect(200, 200, 400, 400); // Right: 600
        var candidate = new WindowsCommandExecutor.WindowPlacementRect(608, 250, 300, 300); // 8px from neighbor.Right

        var snapped = WindowsCommandExecutor.WindowManagerHelper.CalculateWindowToWindowSnap(
            candidate,
            [neighbor],
            snapThreshold: 16);

        // Snapped Left should align directly to neighbor.Right (600)
        Assert.Equal(600, snapped.X);
        Assert.Equal(250, snapped.Y);
    }

    [Fact]
    public void CalculateScratchpadBounds_computes_centered_anchors()
    {
        var workArea = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);

        var topCenter = WindowsCommandExecutor.WindowManagerHelper.CalculateScratchpadBounds(
            workArea,
            widthFraction: 0.70,
            heightFraction: 0.60,
            WindowsCommandExecutor.ScratchpadAnchor.TopCenter);

        Assert.Equal(1344, topCenter.Width); // 1920 * 0.70
        Assert.Equal(648, topCenter.Height); // 1080 * 0.60
        Assert.Equal((1920 - 1344) / 2, topCenter.X);
        Assert.Equal(24, topCenter.Y);

        var center = WindowsCommandExecutor.WindowManagerHelper.CalculateScratchpadBounds(
            workArea,
            widthFraction: 0.50,
            heightFraction: 0.50,
            WindowsCommandExecutor.ScratchpadAnchor.Center);

        Assert.Equal(960, center.Width);
        Assert.Equal(540, center.Height);
        Assert.Equal((1920 - 960) / 2, center.X);
        Assert.Equal((1080 - 540) / 2, center.Y);
    }

    [Fact]
    public void VirtualDesktopManager_and_WindowControls_zero_handle_safety()
    {
        Assert.Null(WindowsCommandExecutor.WindowManagerHelper.IsWindowOnCurrentVirtualDesktop(0));
        Assert.Null(WindowsCommandExecutor.WindowManagerHelper.GetWindowDesktopGuid(0));
        Assert.False(WindowsCommandExecutor.WindowManagerHelper.MoveWindowToVirtualDesktop(0, Guid.NewGuid()));
        Assert.False(WindowsCommandExecutor.WindowManagerHelper.MoveWindowToVirtualDesktop(1, Guid.Empty));
        Assert.False(WindowsCommandExecutor.WindowManagerHelper.ToggleWindowTopmost(0));
        Assert.Equal(255, WindowsCommandExecutor.WindowManagerHelper.StepWindowTransparency(0, -20));
        Assert.False(WindowsCommandExecutor.WindowManagerHelper.BeginWindowDrag(0));
        Assert.False(WindowsCommandExecutor.WindowManagerHelper.CenterWindow(0));
    }

    [Fact]
    public void BeginWindowResize_and_HierarchyEnablers_zero_handle_safety()
    {
        Assert.False(WindowsCommandExecutor.WindowManagerHelper.BeginWindowResize(0, WindowsCommandExecutor.WindowResizeEdge.BottomRight));
        Assert.Equal(0, WindowsCommandExecutor.WindowManagerHelper.EnableWindowHierarchy(0));
        Assert.Equal(0, WindowsCommandExecutor.WindowManagerHelper.EnableSystemMenuItems(0));
    }

    [Fact]
    public void CalculateGravityResize_verifies_all_anchors()
    {
        // Old window: 1000x800 at (100, 100). New window: 600x400.
        // dw = 400, dh = 400.
        var current = new WindowsCommandExecutor.WindowPlacementRect(100, 100, 1000, 800);

        // NorthWest: top-left fixed
        var nw = WindowsCommandExecutor.WindowManagerHelper.CalculateGravityResize(current, 600, 400, WindowsCommandExecutor.WindowGravity.NorthWest);
        Assert.Equal(100, nw.X);
        Assert.Equal(100, nw.Y);
        Assert.Equal(600, nw.Width);
        Assert.Equal(400, nw.Height);

        // Center: centered horizontally and vertically
        var center = WindowsCommandExecutor.WindowManagerHelper.CalculateGravityResize(current, 600, 400, WindowsCommandExecutor.WindowGravity.Center);
        Assert.Equal(100 + 200, center.X); // 300
        Assert.Equal(100 + 200, center.Y); // 300

        // SouthEast: bottom-right fixed
        var se = WindowsCommandExecutor.WindowManagerHelper.CalculateGravityResize(current, 600, 400, WindowsCommandExecutor.WindowGravity.SouthEast);
        Assert.Equal(100 + 400, se.X); // 500
        Assert.Equal(100 + 400, se.Y); // 500

        // North: centered horizontally, top fixed
        var north = WindowsCommandExecutor.WindowManagerHelper.CalculateGravityResize(current, 600, 400, WindowsCommandExecutor.WindowGravity.North);
        Assert.Equal(300, north.X);
        Assert.Equal(100, north.Y);
    }

    [Fact]
    public void CalculateKeyboardStepAcceleration_ramping_behavior()
    {
        // Initial press (repeat count 1) -> baseStep
        double step1 = WindowsCommandExecutor.WindowManagerHelper.CalculateKeyboardStepAcceleration(1, 0, baseStep: 10.0, maxStep: 100.0);
        Assert.Equal(10.0, step1);

        // Elapsed time > thresholdMs -> reset to baseStep
        double stepSlow = WindowsCommandExecutor.WindowManagerHelper.CalculateKeyboardStepAcceleration(5, 300.0, baseStep: 10.0, maxStep: 100.0);
        Assert.Equal(10.0, stepSlow);

        // Repeated fast key presses ramp up velocity
        double stepFast = WindowsCommandExecutor.WindowManagerHelper.CalculateKeyboardStepAcceleration(4, 50.0, baseStep: 10.0, maxStep: 100.0);
        Assert.True(stepFast > 10.0);
        Assert.True(stepFast <= 100.0);
    }

    [Fact]
    public void CycleWindowEdgeFraction_and_CenterScale_miro_behavior()
    {
        var workArea = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1200, 800);

        // 1. Left cycle: from empty/arbitrary -> 1/2 (600)
        var startWin = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 100, 100);
        var leftHalf = WindowsCommandExecutor.WindowManagerHelper.CycleWindowEdgeFraction(startWin, workArea, WindowsCommandExecutor.DirectionalGridAction.Left);
        Assert.Equal(600, leftHalf.Width);
        Assert.Equal(0, leftHalf.X);

        // 2. Cycling again from 1/2 (600) -> 1/3 (400)
        var leftThird = WindowsCommandExecutor.WindowManagerHelper.CycleWindowEdgeFraction(leftHalf, workArea, WindowsCommandExecutor.DirectionalGridAction.Left);
        Assert.Equal(400, leftThird.Width);

        // 3. Cycling again from 1/3 (400) -> 2/3 (800)
        var leftTwoThirds = WindowsCommandExecutor.WindowManagerHelper.CycleWindowEdgeFraction(leftThird, workArea, WindowsCommandExecutor.DirectionalGridAction.Left);
        Assert.Equal(800, leftTwoThirds.Width);

        // 4. Center scale cycle: 1/1 -> 3/4 -> 1/2 -> 1/1
        var full = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1200, 800);
        var threeQuarters = WindowsCommandExecutor.WindowManagerHelper.CycleWindowCenterScale(full, workArea);
        Assert.Equal(900, threeQuarters.Width);
        Assert.Equal((1200 - 900) / 2, threeQuarters.X);

        var halfCenter = WindowsCommandExecutor.WindowManagerHelper.CycleWindowCenterScale(threeQuarters, workArea);
        Assert.Equal(600, halfCenter.Width);
        Assert.Equal((1200 - 600) / 2, halfCenter.X);
    }

    [Fact]
    public void CalculateRatioTilingLayout_subdivides_properly()
    {
        var area = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1000, 500);
        var panes = WindowsCommandExecutor.WindowManagerHelper.CalculateRatioTilingLayout(area, 0.6, 10, 3);
        Assert.Equal(3, panes.Count);
        // First pane gets vertical split (width >= height)
        Assert.Equal(10, panes[0].X);
        Assert.Equal(10, panes[0].Y);
        Assert.True(panes[0].Width > 0);
        Assert.True(panes[0].Height > 0);
    }

    [Fact]
    public void CalculateGridDimensions_optimal_matrix()
    {
        // 4 windows in wide aspect: 2x2
        var (rows4, cols4) = WindowsCommandExecutor.WindowManagerHelper.CalculateGridDimensions(4, 1920, 1080);
        Assert.Equal(2, rows4);
        Assert.Equal(2, cols4);

        // 6 windows in wide aspect: 2 rows, 3 columns
        var (rows6, cols6) = WindowsCommandExecutor.WindowManagerHelper.CalculateGridDimensions(6, 1920, 1080);
        Assert.Equal(2, rows6);
        Assert.Equal(3, cols6);
    }

    [Fact]
    public void CalculateMagneticEdgeSnap_snaps_correctly()
    {
        var screen = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);
        var winNearLeftTop = new WindowsCommandExecutor.WindowPlacementRect(8, 10, 400, 300);

        var (snapped, flags) = WindowsCommandExecutor.WindowManagerHelper.CalculateMagneticEdgeSnap(winNearLeftTop, screen, 12);
        Assert.Equal(0, snapped.X);
        Assert.Equal(0, snapped.Y);
        Assert.True((flags & WindowsCommandExecutor.WindowSnapFlags.Left) != 0);
        Assert.True((flags & WindowsCommandExecutor.WindowSnapFlags.Top) != 0);

        var winFar = new WindowsCommandExecutor.WindowPlacementRect(500, 500, 400, 300);
        var (snappedFar, flagsFar) = WindowsCommandExecutor.WindowManagerHelper.CalculateMagneticEdgeSnap(winFar, screen, 12);
        Assert.Equal(500, snappedFar.X);
        Assert.Equal(500, snappedFar.Y);
        Assert.Equal(WindowsCommandExecutor.WindowSnapFlags.None, flagsFar);
    }

    [Fact]
    public void CalculateSmartMaximizeToggle_and_Easings()
    {
        var screen = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1000, 800);
        var normalWin = new WindowsCommandExecutor.WindowPlacementRect(100, 100, 500, 400);

        // Not maximized -> toggles to screen size
        var maxToggle = WindowsCommandExecutor.WindowManagerHelper.CalculateSmartMaximizeToggle(normalWin, screen);
        Assert.Equal(1000, maxToggle.Width);
        Assert.Equal(800, maxToggle.Height);

        // Maximized -> toggles to restored centered rect
        var restoreToggle = WindowsCommandExecutor.WindowManagerHelper.CalculateSmartMaximizeToggle(screen, screen);
        Assert.True(restoreToggle.Width < 1000);
        Assert.True(restoreToggle.Height < 800);

        // Easing tests
        double circ0 = WindowsCommandExecutor.WindowManagerHelper.EvaluateCircularEasing(0.0);
        double circ1 = WindowsCommandExecutor.WindowManagerHelper.EvaluateCircularEasing(1.0);
        Assert.Equal(0.0, circ0, 3);
        Assert.Equal(1.0, circ1, 3);

        double bounce0 = WindowsCommandExecutor.WindowManagerHelper.EvaluateBounceEasing(0.0);
        double bounce1 = WindowsCommandExecutor.WindowManagerHelper.EvaluateBounceEasing(1.0);
        Assert.Equal(0.0, bounce0, 3);
        Assert.Equal(1.0, bounce1, 3);

        double bezier = WindowsCommandExecutor.WindowManagerHelper.EvaluateCubicBezier(0.5, 0.0, 0.25, 0.75, 1.0);
        Assert.True(bezier > 0.0 && bezier < 1.0);
    }

    [Fact]
    public void CircularBuffer_fifo_eviction_and_enumeration()
    {
        var buf = new WindowsCommandExecutor.CircularBuffer<int>(3);
        Assert.Equal(0, buf.Count);
        Assert.Equal(3, buf.Capacity);

        buf.Push(1);
        buf.Push(2);
        buf.Push(3);
        Assert.True(buf.IsFull);
        Assert.Equal([1, 2, 3], buf.ToList());

        // Push 4th item: should evict oldest (1)
        buf.Push(4);
        Assert.Equal([2, 3, 4], buf.ToList());
    }

    [Fact]
    public void WindowManager_CalculateMinimumOverlapPlacement_places_with_minimal_overlap()
    {
        var screen = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);
        var existing = new List<WindowsCommandExecutor.WindowPlacementRect>
        {
            new(0, 0, 800, 600)
        };

        var (placed, overlap) = WindowsCommandExecutor.WindowManagerHelper.CalculateMinimumOverlapPlacement(
            width: 800, height: 600, screen, existing);

        Assert.True(placed.X >= 0);
        Assert.True(placed.Y >= 0);
        Assert.True(placed.Right <= 1920);
        Assert.True(placed.Bottom <= 1080);
        Assert.Equal(800, placed.Width);
        Assert.Equal(600, placed.Height);

        // Placed window should avoid full overlap with the (0,0,800,600) window
        bool completelyOverlapped = placed.X == 0 && placed.Y == 0;
        Assert.False(completelyOverlapped);
    }

    [Fact]
    public void WindowManager_CalculateFuzzyCascadePlacement_cascades_and_wraps()
    {
        var screen = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);
        var existing = new List<WindowsCommandExecutor.WindowPlacementRect>
        {
            new(0, 0, 800, 600)
        };

        var cascaded = WindowsCommandExecutor.WindowManagerHelper.CalculateFuzzyCascadePlacement(
            width: 800, height: 600, screen, existing, cascadeFuzz: 15, cascadeInterval: 30);

        Assert.Equal(30, cascaded.X);
        Assert.Equal(30, cascaded.Y);
        Assert.Equal(800, cascaded.Width);
        Assert.Equal(600, cascaded.Height);

        // Test wrap-around when previous window occupies the boundary
        var nearEdge = new List<WindowsCommandExecutor.WindowPlacementRect>
        {
            new(0, 0, 800, 600),
            new(30, 30, 800, 600)
        };

        var wrapped = WindowsCommandExecutor.WindowManagerHelper.CalculateFuzzyCascadePlacement(
            width: 800, height: 600, screen, nearEdge, cascadeFuzz: 15, cascadeInterval: 30);

        Assert.Equal(60, wrapped.X);
        Assert.Equal(60, wrapped.Y);
    }

    [Fact]
    public void WindowManager_EvaluateMiroStepCycle_cycles_fractions_and_fullscreen_steps()
    {
        var screen = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 1920, 1080);
        var dummyCurrent = new WindowsCommandExecutor.WindowPlacementRect(0, 0, 800, 600);

        // Left direction: 1/2 -> 1/3 -> 2/3
        var step0 = WindowsCommandExecutor.WindowManagerHelper.EvaluateMiroStepCycle(
            dummyCurrent, screen, WindowsCommandExecutor.MiroCycleDirection.Left, stepIndex: 0);
        Assert.Equal(0, step0.X);
        Assert.Equal(960, step0.Width);
        Assert.Equal(1080, step0.Height);

        var step1 = WindowsCommandExecutor.WindowManagerHelper.EvaluateMiroStepCycle(
            dummyCurrent, screen, WindowsCommandExecutor.MiroCycleDirection.Left, stepIndex: 1);
        Assert.Equal(0, step1.X);
        Assert.Equal(640, step1.Width);

        var step2 = WindowsCommandExecutor.WindowManagerHelper.EvaluateMiroStepCycle(
            dummyCurrent, screen, WindowsCommandExecutor.MiroCycleDirection.Left, stepIndex: 2);
        Assert.Equal(0, step2.X);
        Assert.Equal(1280, step2.Width);

        // Center / Fullscreen: 1.0 -> 3/4 -> 1/2
        var center0 = WindowsCommandExecutor.WindowManagerHelper.EvaluateMiroStepCycle(
            dummyCurrent, screen, WindowsCommandExecutor.MiroCycleDirection.Fullscreen, stepIndex: 0);
        Assert.Equal(1920, center0.Width);
        Assert.Equal(1080, center0.Height);

        var center1 = WindowsCommandExecutor.WindowManagerHelper.EvaluateMiroStepCycle(
            dummyCurrent, screen, WindowsCommandExecutor.MiroCycleDirection.Fullscreen, stepIndex: 1);
        Assert.Equal(1440, center1.Width);
        Assert.Equal(810, center1.Height);

        var center2 = WindowsCommandExecutor.WindowManagerHelper.EvaluateMiroStepCycle(
            dummyCurrent, screen, WindowsCommandExecutor.MiroCycleDirection.Fullscreen, stepIndex: 2);
        Assert.Equal(960, center2.Width);
        Assert.Equal(540, center2.Height);
    }

    [Fact]
    public void WindowManager_Opacity_Priority_and_VirtualDesktopStatus_handle_invalid_hwnd_safely()
    {
        // Null / invalid hwnd must fail-closed and return safe defaults
        Assert.False(WindowsCommandExecutor.WindowManagerHelper.SetWindowOpacity(0, 80));
        Assert.Equal(100, WindowsCommandExecutor.WindowManagerHelper.GetWindowOpacity(0));
        Assert.False(WindowsCommandExecutor.WindowManagerHelper.AdjustWindowProcessPriority(0, 0x00000020u));

        var vdStatus = WindowsCommandExecutor.WindowManagerHelper.QueryVirtualDesktopStatus(0);
        Assert.False(vdStatus.Succeeded);
        Assert.False(vdStatus.IsOnCurrentDesktop);
        Assert.Equal(Guid.Empty, vdStatus.DesktopId);
    }
}
