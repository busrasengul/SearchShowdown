namespace SearchShowdown.AzureSearch.Configuration;

public enum AzureFieldValues
{
    Keywords,
    Integers,
    Decimals,
    DateTimeOffsets,
    Texts,
}

public sealed class AzureSearchFieldOptions
{
    public Field[] Fields { get; set; } = [];

    public sealed class Field
    {
        public required string PropertyName { get; init; }

        public AzureFieldValues FieldValues { get; init; }

        public bool Facetable { get; init; }

        public bool Sortable { get; init; }
    }
}
