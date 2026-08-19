namespace Hub.Domain.Common;

public static class DomainErrors
{
    public static class General
    {
        public static Error NotFound(string entityName) =>
            new("General.NotFound", $"'{entityName}' was not found.", ErrorType.NotFound);

        public static Error Validation(string message) =>
            new("General.Validation", message);

        public static Error NullOrEmpty(string fieldName) =>
            new("General.NullOrEmpty", $"'{fieldName}' must not be null or empty.");
    }
}
