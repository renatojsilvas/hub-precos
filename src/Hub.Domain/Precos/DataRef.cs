using Hub.Domain.Common;

namespace Hub.Domain.Precos;

public sealed record DataRef
{
    private DataRef(DateOnly value) => Value = value;

    public DateOnly Value { get; }

    public static Result<DataRef> Create(DateOnly value)
    {
        if (value == DateOnly.MinValue)
        {
            return PrecoErrors.DataRefVazia;
        }

        return new DataRef(value);
    }
}
