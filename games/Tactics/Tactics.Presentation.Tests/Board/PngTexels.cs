namespace Tactics.Presentation.Tests.Board;

/// <summary>Reads a PNG from the pack, as Godot's <c>GetData</c> gives it: tests here have no image library.</summary>
internal static class PngTexels
{
    public static (byte[] Rgba, int Width, int Height) Read(string file) => Presentation.Board.PngPixels.Read(file);
}
