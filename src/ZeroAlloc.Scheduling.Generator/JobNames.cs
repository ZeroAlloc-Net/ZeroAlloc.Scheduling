namespace ZeroAlloc.Scheduling.Generator;

/// <summary>
/// Names of the code generated for a job type. A type name that already ends in <c>Job</c> does
/// not get the suffix again: <c>SendWelcomeEmailJob</c> registers with
/// <c>AddSendWelcomeEmailJob()</c>, and <c>Cleanup</c> with <c>AddCleanupJob()</c>. The match is
/// ordinal and case-sensitive, so <c>ImportJOB</c> registers with <c>AddImportJOBJob()</c>.
/// </summary>
internal static class JobNames
{
    private const string Suffix = "Job";

    /// <summary>The type name without a trailing <c>Job</c>, if it has one.</summary>
    public static string Stem(string typeName)
        => typeName.EndsWith(Suffix, System.StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - Suffix.Length)
            : typeName;

    /// <summary>The generated <c>ISchedulingBuilder</c> extension, for example <c>AddCleanupJob</c>.</summary>
    public static string RegistrationMethod(string typeName) => $"Add{Stem(typeName)}{Suffix}";

    /// <summary>The generated executor class, for example <c>CleanupJobTypeExecutor</c>.</summary>
    public static string Executor(string typeName) => $"{Stem(typeName)}{Suffix}TypeExecutor";

    /// <summary>The generated recurring startup service, for example <c>CleanupRecurringStartup</c>.</summary>
    public static string RecurringStartup(string typeName) => $"{typeName}RecurringStartup";
}
