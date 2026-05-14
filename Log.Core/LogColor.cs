namespace WpfLog.Core;

public readonly record struct LogColor(byte A, byte R, byte G, byte B)
{
    public static LogColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static LogColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    public static readonly LogColor Black = FromRgb(0, 0, 0);
    public static readonly LogColor White = FromRgb(255, 255, 255);
    public static readonly LogColor Red = FromRgb(255, 0, 0);
    public static readonly LogColor Green = FromRgb(0, 128, 0);
    public static readonly LogColor Gray = FromRgb(128, 128, 128);
    public static readonly LogColor Yellow = FromRgb(184, 134, 11);
}
