namespace DrpgServer;

public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string msg) => Write(ConsoleColor.Gray, msg);
    public static void Warn(string msg) => Write(ConsoleColor.Yellow, msg);
    public static void Error(string msg) => Write(ConsoleColor.Red, msg);

    public static void Http(string method, string path, int status) =>
        Write(status >= 400 ? ConsoleColor.DarkYellow : ConsoleColor.DarkGray, $"{method} {path} -> {status}");

    private static void Write(ConsoleColor color, string msg)
    {
        lock (Gate)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            Console.ResetColor();
        }
    }
}
