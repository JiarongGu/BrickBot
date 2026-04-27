namespace BrickBot.Modules.Vision.Models;

public sealed record VisionMatch(int X, int Y, int Width, int Height, double Confidence)
{
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
}

public sealed record FindOptions(double MinConfidence = 0.85, RegionOfInterest? Roi = null);

public sealed record RegionOfInterest(int X, int Y, int Width, int Height);

public sealed record ColorSample(int R, int G, int B);
