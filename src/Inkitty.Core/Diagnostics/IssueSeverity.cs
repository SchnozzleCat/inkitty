namespace Inkitty.Core.Diagnostics;

public enum IssueSeverity
{
    Error,
    Warning,
    Todo,
    RuntimeError,
    RuntimeWarning,
}

public static class IssueSeverityExtensions
{
    extension(IssueSeverity severity)
    {
        public bool IsError => severity is IssueSeverity.Error or IssueSeverity.RuntimeError;

        public bool IsWarning => severity is IssueSeverity.Warning or IssueSeverity.RuntimeWarning;

        public bool IsRuntime => severity is IssueSeverity.RuntimeError or IssueSeverity.RuntimeWarning;

        /// <summary>Sort rank: errors first, then warnings, then TODOs.</summary>
        public int Rank => severity switch
        {
            IssueSeverity.Error => 0,
            IssueSeverity.RuntimeError => 1,
            IssueSeverity.Warning => 2,
            IssueSeverity.RuntimeWarning => 3,
            IssueSeverity.Todo => 4,
            _ => 5,
        };
    }
}
