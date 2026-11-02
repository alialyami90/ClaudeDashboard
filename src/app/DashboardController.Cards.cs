using System;

namespace WidgetScreen
{
    // Card contents follow their card's size (CardSizing; docs/design/any-screen-size.md). The Claude card picks its
    // Small, Medium or Large design from its bounds on the canvas.
    public sealed partial class DashboardController
    {
        private void FitCards()
        {
            FitStrips();
            claudeCard.Fit(Editor.Bounds("Claude"), settings.TextScale);
        }
    }
}
