# Interface language resources

`en-US/Resources.resw` is the fallback resource set for navigation and page titles.
WinUI selects resources from the user's Windows language preferences. Route tags
and automation IDs remain invariant; translated navigation labels must never be
used as route identifiers.

Add a language folder with the same resource keys to extend coverage. Keep
accessible navigation names bound to their localized content. Dynamic command
catalog descriptions, operation outcomes, and remaining page copy still require
localization; this foundation does not claim a translated application.

Do not copy translations from the WinPort archives without verifying provenance,
terminology, and their meaning in WinCare's actual workflows.
