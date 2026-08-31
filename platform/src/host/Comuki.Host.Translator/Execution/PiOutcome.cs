namespace Comuki.Host.Translator.Execution;

/// <summary>
/// Bottom line of one pi run: status (<c>success</c> / <c>failed</c> /
/// <c>cancelled</c>), wall-clock duration and the result/error text that
/// goes into the StageReport.
/// </summary>
/// <param name="Status"></param>
/// <param name="DurationMs"></param>
/// <param name="ResultText"></param>
/// <param name="ErrorText"></param>
public sealed record PiOutcome(string Status, long DurationMs, string ResultText, string ErrorText)
{
    public const string SuccessStatus = "success";

    public const string FailedStatus = "failed";

    public const string CancelledStatus = "cancelled";
}
