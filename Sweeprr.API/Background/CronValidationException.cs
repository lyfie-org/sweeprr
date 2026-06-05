namespace Sweeprr.API.Background;

public sealed class CronValidationException : Exception
{
    public string Expression { get; }

    public CronValidationException(string expression)
        : base($"Invalid cron expression: '{expression}'")
    {
        Expression = expression;
    }
}
