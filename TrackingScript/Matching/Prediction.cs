namespace TrackingScript.Matching;

public sealed class Prediction
{
    public double Forward { get; set; }
    public double Backward { get; set; }
    public double StraightForward { get; }
    public double StraightBackward { get; }
    public bool TurnAssisted { get; set; }

    public Prediction(double forward, double backward)
    {
        Forward = StraightForward = forward;
        Backward = StraightBackward = backward;
    }
}