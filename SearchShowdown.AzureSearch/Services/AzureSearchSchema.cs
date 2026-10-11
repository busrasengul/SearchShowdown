using Microsoft.Extensions.Options;
using SearchShowdown.AzureSearch.Configuration;
using CoreFieldNames = Umbraco.Cms.Search.Core.Constants.FieldNames;

namespace SearchShowdown.AzureSearch.Services;

internal sealed record AzureField(string Name, AzureFieldValues Values, bool IsCollection, string SortName, bool Sortable);

internal sealed class AzureSearchSchema
{
    private readonly Dictionary<string, AzureSearchFieldOptions.Field> _declaredFields;

    public AzureSearchSchema(IOptions<AzureSearchFieldOptions> options)
        => _declaredFields = options.Value.Fields.ToDictionary(field => field.PropertyName, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<AzureSearchFieldOptions.Field> DeclaredFields => _declaredFields.Values;

    public bool TryGetDeclaredField(string propertyName, out AzureSearchFieldOptions.Field field)
        => _declaredFields.TryGetValue(propertyName, out field!);

    public AzureField? Resolve(string fieldName)
        => fieldName switch
        {
            CoreFieldNames.Id => new(AzureFieldNames.Key, AzureFieldValues.Keywords, false, AzureFieldNames.Key, false),
            CoreFieldNames.ContentTypeId => new(AzureFieldNames.ContentTypeId, AzureFieldValues.Keywords, false, AzureFieldNames.ContentTypeId, false),
            CoreFieldNames.ObjectType => new(AzureFieldNames.ObjectType, AzureFieldValues.Keywords, false, AzureFieldNames.ObjectType, false),
            CoreFieldNames.PathIds => new(AzureFieldNames.PathKeys, AzureFieldValues.Keywords, true, AzureFieldNames.PathKeys, false),
            CoreFieldNames.Name => new(AzureFieldNames.Name, AzureFieldValues.Keywords, false, AzureFieldNames.Name, true),
            CoreFieldNames.CreateDate => new(AzureFieldNames.CreateDate, AzureFieldValues.DateTimeOffsets, false, AzureFieldNames.CreateDate, true),
            CoreFieldNames.UpdateDate => new(AzureFieldNames.UpdateDate, AzureFieldValues.DateTimeOffsets, false, AzureFieldNames.UpdateDate, true),
            _ => _declaredFields.TryGetValue(fieldName, out AzureSearchFieldOptions.Field? field)
                ? new(AzureFieldNames.ForProperty(field), field.FieldValues, IsCollection(field), SortName(field), field.Sortable)
                : null,
        };

    public static bool IsCollection(AzureSearchFieldOptions.Field field)
        => field.FieldValues is AzureFieldValues.Keywords or AzureFieldValues.Texts || field.Sortable is false;

    private static string SortName(AzureSearchFieldOptions.Field field)
        => field.FieldValues is AzureFieldValues.Keywords
            ? AzureFieldNames.SortForKeyword(field)
            : AzureFieldNames.ForProperty(field);
}
