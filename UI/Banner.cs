namespace SlizsyChecker;

public static class Banner
{
    private static readonly string[] Art =
    {
        @"        ___",
        @"       /\_ \    __",
        @"  ____\//\ \  /\_\  ____     ____  __  __",
        @" /',__\ \ \ \ \/\ \/\_ ,`\  /',__\/\ \/\ \",
        @"/\__, `\ \_\ \_\ \ \/_/  /_/\__, `\ \ \_\ \",
        @"\/\____/ /\____\\ \_\/\____\/\____/\/`____ \",
        @" \/___/  \/____/ \/_/\/____/\/___/  `/___/> \",
        @"                                       /\___/",
        @"                                       \/__/",
    };

    public static void Show()
    {
        Term.Clear();
        Term.Blank();
        Term.Gradient(Art.Select(l => "  " + l).ToArray(), Tones.Purple, Tones.Cyan);
        string bar = new('\u2500', 14);
        Term.Line(
            ("  " + bar + "  ", Tones.Purple),
            ("made by ", Tones.Gray),
            ("slizsy", Tones.Pink),
            ("  " + bar, Tones.Cyan));
        Term.Blank();
    }
}
