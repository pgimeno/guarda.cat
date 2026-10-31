using MudBlazor;

namespace GuardaCat.Client.Theming;

public static class GuardaCatTheme
{
    private static readonly string[] FontFamily = ["Segoe UI", "Helvetica Neue", "Arial", "sans-serif"];

    public static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#71415D",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#51545C",
            SecondaryContrastText = "#FFFFFF",
            Background = "#F4F4F2",
            BackgroundGray = "#F8F8F7",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#1D1D1F",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#1D1D1F",
            DrawerIcon = "#6B6B70",
            TextPrimary = "#1D1D1F",
            TextSecondary = "#6B6B70",
            TextDisabled = "#8A8D95",
            ActionDefault = "#696C75",
            ActionDisabled = "#A6A8AE",
            ActionDisabledBackground = "#F0F1F3",
            Divider = "#D9D9D6",
            LinesDefault = "#D9D9D6",
            LinesInputs = "#C5C7CE",
            TableLines = "#E5E6EA",
            TableStriped = "#FAFAFB",
            TableHover = "#F6F7F8",
            Info = "#496B8A",
            Success = "#39735A",
            Warning = "#91651F",
            Error = "#B04452",
            HoverOpacity = 0.05
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px",
            AppbarHeight = "58px"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = FontFamily,
                FontSize = "0.9375rem",
                LineHeight = "1.6"
            },
            H1 = new H1Typography
            {
                FontFamily = FontFamily,
                FontSize = "1.85rem",
                FontWeight = "700",
                LineHeight = "1.2",
                LetterSpacing = "-0.035em"
            },
            H2 = new H2Typography
            {
                FontFamily = FontFamily,
                FontSize = "1.5rem",
                FontWeight = "700",
                LineHeight = "1.3",
                LetterSpacing = "-0.025em"
            },
            H3 = new H3Typography
            {
                FontFamily = FontFamily,
                FontSize = "1.25rem",
                FontWeight = "600",
                LineHeight = "1.4",
                LetterSpacing = "-0.02em"
            },
            H4 = new H4Typography
            {
                FontFamily = FontFamily,
                FontSize = "1.125rem",
                FontWeight = "600",
                LineHeight = "1.4"
            },
            H5 = new H5Typography
            {
                FontFamily = FontFamily,
                FontSize = "1rem",
                FontWeight = "600",
                LineHeight = "1.5"
            },
            H6 = new H6Typography
            {
                FontFamily = FontFamily,
                FontSize = "0.9375rem",
                FontWeight = "600",
                LineHeight = "1.5"
            },
            Subtitle1 = new Subtitle1Typography
            {
                FontFamily = FontFamily,
                FontSize = "1rem",
                LineHeight = "1.6"
            },
            Subtitle2 = new Subtitle2Typography
            {
                FontFamily = FontFamily,
                FontSize = "0.875rem",
                FontWeight = "600",
                LineHeight = "1.5"
            },
            Body1 = new Body1Typography
            {
                FontFamily = FontFamily,
                FontSize = "0.9375rem",
                LineHeight = "1.65"
            },
            Body2 = new Body2Typography
            {
                FontFamily = FontFamily,
                FontSize = "0.875rem",
                LineHeight = "1.6"
            },
            Button = new ButtonTypography
            {
                FontFamily = FontFamily,
                FontSize = "0.875rem",
                FontWeight = "600",
                LineHeight = "1.5",
                LetterSpacing = "0",
                TextTransform = "none"
            },
            Caption = new CaptionTypography
            {
                FontFamily = FontFamily,
                FontSize = "0.75rem",
                LineHeight = "1.5"
            },
            Overline = new OverlineTypography
            {
                FontFamily = FontFamily,
                FontSize = "0.6875rem",
                FontWeight = "600",
                LineHeight = "1.6",
                LetterSpacing = "0.1em"
            }
        }
    };
}
