namespace SlizsyChecker;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            await new App().RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Term.Line(("  [!] ", Tones.Red), (ex.Message, Tones.White));
            Input.Pause();
            return 1;
        }
    }
}
