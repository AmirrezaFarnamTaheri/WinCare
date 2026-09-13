# Interface language resources

`en-US/Resources.resw` is the fallback resource set for navigation and page titles. WinUI selects resources from the user's Windows language preferences. Route tags and automation IDs remain invariant; translated navigation labels must never be used as route identifiers.

Add a language folder with the same resource keys to extend coverage. Keep accessible navigation names bound to their localized content.

The current release localizes the shell/navigation resource surface only. Dynamic command catalog descriptions, operation outcomes, and page-specific prose remain English unless a complete resource set is provided. Do not describe a language as fully supported until those user-visible surfaces and accessibility names have been validated together.

Translations must be authored or reviewed against WinCare's current workflows and terminology. Do not import unverified text from unrelated projects or historical reference material.
