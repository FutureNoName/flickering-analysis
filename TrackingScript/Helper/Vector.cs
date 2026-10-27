namespace TrackingScript.Helper;

public readonly record struct Vector(double X, double Y)
{
    public double Length => Math.Sqrt(X * X + Y * Y);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    public static Vector operator +(Vector a, Vector b) =>
        new(a.X + b.X, a.Y + b.Y);

    public static Vector operator -(Vector a, Vector b) =>
        new(a.X - b.X, a.Y - b.Y);

    public static Vector operator *(Vector vector, double factor) =>
        new(vector.X * factor, vector.Y * factor);

    public static double Distance(Vector a, Vector b) => (a - b).Length;
}
