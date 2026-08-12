namespace RunningPlan.Cli.Intervals;

public sealed class VerificationFailedException : Exception
{
    public VerificationFailedException(VerificationReport report)
        : base("Post-sync verification detected missing or mismatched events.")
    {
        Report = report;
    }

    public VerificationReport Report { get; }
}
