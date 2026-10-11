using SearchShowdown.AzureSearch.Configuration;

namespace SearchShowdown.AzureSearch.Services;

internal static class AzureFieldNames
{
    public const string Id = "id";
    public const string Key = "key";
    public const string ObjectType = "objectType";
    public const string Culture = "culture";
    public const string AccessKeys = "accessKeys";
    public const string PathKeys = "pathKeys";
    public const string ContentTypeId = "contentTypeId";
    public const string Name = "name";
    public const string CreateDate = "createDate";
    public const string UpdateDate = "updateDate";
    public const string TextsR1 = "textsR1";
    public const string TextsR2 = "textsR2";
    public const string TextsR3 = "textsR3";
    public const string Texts = "texts";

    public const string InvariantCulture = "inv";
    public const string ScoringProfile = "umbraco";

    public static readonly string[] SearchFields = [TextsR1, TextsR2, TextsR3, Texts];

    public static string ForProperty(AzureSearchFieldOptions.Field field)
        => field.FieldValues switch
        {
            AzureFieldValues.Keywords => $"k_{field.PropertyName}",
            AzureFieldValues.Integers => $"i_{field.PropertyName}",
            AzureFieldValues.Decimals => $"d_{field.PropertyName}",
            AzureFieldValues.DateTimeOffsets => $"dt_{field.PropertyName}",
            AzureFieldValues.Texts => $"x_{field.PropertyName}",
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

    public static string SortForKeyword(AzureSearchFieldOptions.Field field)
        => $"{ForProperty(field)}_sort";
}
