"""Checks the actual runtime palette, including the darker instrument surfaces."""
import unittest

from tools.verify_pill_contrast import THEME_FILE, parse_theme_colors, ratio


class VisualContrastTests(unittest.TestCase):
    def test_readable_text_on_every_workspace_surface(self):
        colors = parse_theme_colors(THEME_FILE)
        surfaces = ('PageBackgroundBrush', 'CardSurfaceBrush', 'SurfaceSecondaryBrush',
                    'HeroBackgroundBrush', 'TelemetryFrameBrush', 'NavigationRailBrush')
        for theme in ('Light', 'Dark'):
            for foreground in ('TextPrimaryBrush', 'TextSecondaryBrush'):
                for surface in surfaces:
                    with self.subTest(theme=theme, foreground=foreground, surface=surface):
                        self.assertGreaterEqual(ratio(colors[theme, foreground], colors[theme, surface]), 4.5)

    def test_primary_action_label_and_diagnostic_accent_are_readable(self):
        colors = parse_theme_colors(THEME_FILE)
        for theme in ('Light', 'Dark'):
            with self.subTest(theme=theme, role='primary action'):
                self.assertGreaterEqual(ratio(colors[theme, 'TextOnAccentBrush'], colors[theme, 'AccentBrush']), 4.5)
            for surface in ('HeroBackgroundBrush', 'SurfaceSecondaryBrush'):
                with self.subTest(theme=theme, surface=surface):
                    self.assertGreaterEqual(ratio(colors[theme, 'AccentTealBrush'], colors[theme, surface]), 4.5)
