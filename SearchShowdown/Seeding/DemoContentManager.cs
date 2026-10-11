using System.Globalization;
using System.Net;
using System.Text.Json;
using Bogus;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Core.Strings;
using Aliases = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases;

namespace SearchShowdown.Seeding;

public sealed record ShowdownProperty(string Alias, string Name, string Editor, string ContentType);

public sealed class DemoContentManager
{
    public const string CompositionAlias = "searchableContent";
    public const string ArticleAlias = "searchArticle";
    public const string ProductAlias = "searchProduct";
    public const string TopicAlias = "searchTopic";

    private const int RandomSeed = 12345;
    private const string DataTypePrefix = "Showdown - ";

    public static readonly string[] Categories =
        ["Technology", "Design", "Business", "Marketing", "Science", "Health", "Travel", "Food"];

    public static readonly string[] Brands =
        ["Northwind", "Acme", "Umbri", "Fjord", "Vanta", "Lumen", "Kettle", "Nimbus", "Orbit", "Sage", "Pixel", "Harbor"];

    public static readonly string[] Audiences = ["Beginner", "Intermediate", "Advanced"];

    public static readonly string[] Regions =
        ["UK", "Europe", "North America", "Asia Pacific", "Middle East & Africa", "Latin America"];

    public static readonly string[] Topics =
    [
        "Accessibility", "AI", "Cloud Hosting", "Content Strategy", "E-commerce",
        "Headless", "Performance", "Search", "Security", "Umbraco",
    ];

    private static readonly string[] TagPool =
    [
        "umbraco", "dotnet", "search", "cloud", "azure", "performance", "tips", "tutorial",
        "release", "community", "headless", "api", "security", "seo", "accessibility", "ux",
        "css", "javascript", "database", "caching", "hosting", "devops", "ai", "open-source",
        "migration", "editor", "content", "media", "forms", "packages",
    ];

    public static readonly ShowdownProperty[] Properties =
    [
        new("summary", "Summary", "Textarea", "Article + Product"),
        new("bodyText", "Body", "Rich text", "Article + Product"),
        new("category", "Category", "Dropdown", "Article + Product"),
        new("tags", "Tags", "Tags", "Article + Product"),
        new("audience", "Audience", "Radio button list", "Article + Product"),
        new("regions", "Regions", "Checkbox list", "Article + Product"),
        new("topic", "Topic", "Content picker", "Article + Product"),
        new("featured", "Featured", "True/false", "Article + Product"),
        new("rating", "Rating", "Integer", "Article + Product"),
        new("publishDate", "Publish date", "Date picker", "Article + Product"),
        new("author", "Author", "Textstring", "Article"),
        new("readingMinutes", "Reading time", "Slider", "Article"),
        new("sku", "SKU", "Textstring", "Product"),
        new("brand", "Brand", "Dropdown", "Product"),
        new("price", "Price", "Decimal", "Product"),
        new("stockLevel", "Stock level", "Integer", "Product"),
    ];

    private readonly IContentTypeService _contentTypeService;
    private readonly IDataTypeService _dataTypeService;
    private readonly IContentService _contentService;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly PropertyEditorCollection _propertyEditors;
    private readonly IConfigurationEditorJsonSerializer _configurationEditorJsonSerializer;

    public DemoContentManager(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IContentService contentService,
        IShortStringHelper shortStringHelper,
        PropertyEditorCollection propertyEditors,
        IConfigurationEditorJsonSerializer configurationEditorJsonSerializer)
    {
        _contentTypeService = contentTypeService;
        _dataTypeService = dataTypeService;
        _contentService = contentService;
        _shortStringHelper = shortStringHelper;
        _propertyEditors = propertyEditors;
        _configurationEditorJsonSerializer = configurationEditorJsonSerializer;
    }

    public async Task EnsureModelAsync()
    {
        IDataType textbox = await BuiltInAsync(Aliases.TextBox, "Showdown - Textstring");
        IDataType textarea = await BuiltInAsync(Aliases.TextArea, "Showdown - Textarea");
        IDataType richText = await BuiltInAsync(Aliases.RichText, "Showdown - Rich text");
        IDataType integer = await BuiltInAsync(Aliases.Integer, "Showdown - Integer");
        IDataType tags = await BuiltInAsync(Aliases.Tags, "Showdown - Tags");
        IDataType boolean = await BuiltInAsync(Aliases.Boolean, "Showdown - True/false");
        IDataType contentPicker = await BuiltInAsync(Aliases.ContentPicker, "Showdown - Content picker");
        IDataType date = await EnsureDataTypeAsync("Showdown - Publish date", Aliases.DateTime);
        IDataType price = await EnsureDataTypeAsync("Showdown - Price", Aliases.Decimal);
        IDataType readingTime = await EnsureDataTypeAsync("Showdown - Reading time", Aliases.Slider, new Dictionary<string, object>
        {
            ["enableRange"] = false,
            ["minVal"] = 1,
            ["maxVal"] = 60,
            ["step"] = 1,
            ["initVal1"] = 5,
        });
        IDataType category = await EnsureDataTypeAsync("Showdown - Category", Aliases.DropDownListFlexible, ValueList(Categories, multiple: false));
        IDataType brand = await EnsureDataTypeAsync("Showdown - Brand", Aliases.DropDownListFlexible, ValueList(Brands, multiple: false));
        IDataType audience = await EnsureDataTypeAsync("Showdown - Audience", Aliases.RadioButtonList, ValueList(Audiences));
        IDataType regions = await EnsureDataTypeAsync("Showdown - Regions", Aliases.CheckBoxList, ValueList(Regions));

        if (_contentTypeService.Get(TopicAlias) is null)
        {
            await CreateContentTypeAsync(new ContentType(_shortStringHelper, Constants.System.Root)
            {
                Alias = TopicAlias,
                Name = "Search Topic",
                Icon = "icon-tag color-purple",
                AllowedAsRoot = true,
            });
        }

        IContentType? composition = _contentTypeService.Get(CompositionAlias);
        if (composition is null)
        {
            var newComposition = new ContentType(_shortStringHelper, Constants.System.Root)
            {
                Alias = CompositionAlias,
                Name = "Searchable Content",
                Icon = "icon-search color-blue",
            };
            newComposition.AddPropertyGroup("search", "Search");
            AddProperty(newComposition, textarea, "summary");
            AddProperty(newComposition, richText, "bodyText");
            AddProperty(newComposition, category, "category");
            AddProperty(newComposition, tags, "tags");
            AddProperty(newComposition, audience, "audience");
            AddProperty(newComposition, regions, "regions");
            AddProperty(newComposition, contentPicker, "topic");
            AddProperty(newComposition, boolean, "featured");
            AddProperty(newComposition, integer, "rating");
            AddProperty(newComposition, date, "publishDate");
            await CreateContentTypeAsync(newComposition);
            composition = newComposition;
        }

        if (_contentTypeService.Get(ArticleAlias) is null)
        {
            var article = new ContentType(_shortStringHelper, Constants.System.Root)
            {
                Alias = ArticleAlias,
                Name = "Search Article",
                Icon = "icon-article color-green",
                AllowedAsRoot = true,
            };
            article.AddContentType(composition);
            article.AddPropertyGroup("details", "Details");
            AddProperty(article, textbox, "author", "details");
            AddProperty(article, readingTime, "readingMinutes", "details");
            await CreateContentTypeAsync(article);
        }

        if (_contentTypeService.Get(ProductAlias) is null)
        {
            var product = new ContentType(_shortStringHelper, Constants.System.Root)
            {
                Alias = ProductAlias,
                Name = "Search Product",
                Icon = "icon-box color-orange",
                AllowedAsRoot = true,
            };
            product.AddContentType(composition);
            product.AddPropertyGroup("details", "Details");
            AddProperty(product, textbox, "sku", "details");
            AddProperty(product, brand, "brand", "details");
            AddProperty(product, price, "price", "details");
            AddProperty(product, integer, "stockLevel", "details");
            await CreateContentTypeAsync(product);
        }
    }

    public async Task ResetModelAsync()
    {
        foreach (var alias in new[] { ProductAlias, ArticleAlias, CompositionAlias, TopicAlias })
        {
            IContentType? contentType = _contentTypeService.Get(alias);
            if (contentType is not null)
            {
                _contentTypeService.Delete(contentType);
            }
        }

        foreach (IDataType dataType in (await _dataTypeService.GetAllAsync()).Where(dataType => dataType.Name?.StartsWith(DataTypePrefix) is true))
        {
            await _dataTypeService.DeleteAsync(dataType.Key, Constants.Security.SuperUserKey);
        }
    }

    public async Task<int> GenerateAsync(int count)
    {
        await EnsureModelAsync();
        Guid[] topicKeys = EnsureTopics();

        var faker = new Faker("en") { Random = new Randomizer(RandomSeed) };
        var earliest = new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var latest = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < count; i++)
        {
            var isProduct = i % 2 == 0;
            var name = isProduct ? faker.Commerce.ProductName() : faker.Hacker.Phrase();

            IContent content = _contentService.Create(
                name.Length > 200 ? name[..200] : name,
                Constants.System.Root,
                isProduct ? ProductAlias : ArticleAlias);

            content.SetValue("summary", faker.Lorem.Sentence(faker.Random.Int(8, 18)));
            content.SetValue("bodyText", RichText(faker.Lorem.Paragraphs(faker.Random.Int(2, 4), "\n").Split('\n')));
            content.SetValue("category", JsonSerializer.Serialize(new[] { faker.PickRandom(Categories) }));
            content.SetValue("tags", JsonSerializer.Serialize(faker.PickRandom(TagPool, faker.Random.Int(2, 5)).ToArray()));
            content.SetValue("audience", faker.PickRandom(Audiences));
            content.SetValue("regions", JsonSerializer.Serialize(faker.PickRandom(Regions, faker.Random.Int(1, 3)).ToArray()));
            content.SetValue("topic", new GuidUdi(Constants.UdiEntityType.Document, faker.PickRandom(topicKeys)).ToString());
            content.SetValue("featured", faker.Random.Bool(0.1f));
            content.SetValue("rating", faker.Random.Int(1, 5));
            content.SetValue("publishDate", faker.Date.Between(earliest, latest).Date);

            if (isProduct)
            {
                content.SetValue("sku", $"SKU-{faker.Random.Int(10000, 99999)}");
                content.SetValue("brand", JsonSerializer.Serialize(new[] { faker.PickRandom(Brands) }));
                content.SetValue("price", Math.Round(faker.Random.Decimal(4.99m, 1999.99m), 2));
                content.SetValue("stockLevel", faker.Random.Bool(0.15f) ? 0 : faker.Random.Int(1, 500));
            }
            else
            {
                content.SetValue("author", faker.Name.FullName());
                content.SetValue("readingMinutes", faker.Random.Int(2, 45).ToString(CultureInfo.InvariantCulture));
            }

            _contentService.Save(content);
            _contentService.Publish(content, ["*"]);
        }

        return count;
    }

    public int Clear()
    {
        var deleted = 0;
        foreach (var alias in new[] { ArticleAlias, ProductAlias })
        {
            IContentType? contentType = _contentTypeService.Get(alias);
            if (contentType is null)
            {
                continue;
            }

            foreach (IContent item in _contentService.GetPagedOfType(contentType.Id, 0, int.MaxValue, out _, null!))
            {
                _contentService.Delete(item);
                deleted++;
            }
        }

        return deleted;
    }

    public IReadOnlyDictionary<string, long> PublishedCounts()
        => new[] { ArticleAlias, ProductAlias, TopicAlias }.ToDictionary(
            alias => alias,
            alias => _contentTypeService.Get(alias) is null ? 0L : _contentService.CountPublished(alias));

    private Guid[] EnsureTopics()
    {
        IContentType topicType = _contentTypeService.Get(TopicAlias)
                                 ?? throw new InvalidOperationException("The topic content type is missing.");

        var existing = _contentService
            .GetPagedOfType(topicType.Id, 0, int.MaxValue, out _, null!)
            .ToDictionary(topic => topic.Name ?? string.Empty, topic => topic.Key);

        foreach (var topicName in Topics.Where(topicName => existing.ContainsKey(topicName) is false))
        {
            IContent topic = _contentService.Create(topicName, Constants.System.Root, TopicAlias);
            _contentService.Save(topic);
            _contentService.Publish(topic, ["*"]);
            existing[topicName] = topic.Key;
        }

        return Topics.Select(topicName => existing[topicName]).ToArray();
    }

    private static string RichText(IEnumerable<string> paragraphs)
        => JsonSerializer.Serialize(new
        {
            markup = string.Concat(paragraphs.Select(paragraph => $"<p>{WebUtility.HtmlEncode(paragraph)}</p>")),
            blocks = (object?)null,
        });

    private static Dictionary<string, object> ValueList(IEnumerable<string> items, bool? multiple = null)
    {
        var configuration = new Dictionary<string, object> { ["items"] = items.ToList() };
        if (multiple is not null)
        {
            configuration["multiple"] = multiple.Value;
        }

        return configuration;
    }

    private async Task CreateContentTypeAsync(IContentType contentType)
    {
        Attempt<ContentTypeOperationStatus> result = await _contentTypeService.CreateAsync(contentType, Constants.Security.SuperUserKey);
        if (result.Success is false)
        {
            throw new InvalidOperationException($"Could not create content type {contentType.Alias}: {result.Result}");
        }
    }

    private async Task<IDataType> BuiltInAsync(string editorAlias, string fallbackName)
        => (await _dataTypeService.GetAllAsync()).FirstOrDefault(dataType => dataType.EditorAlias == editorAlias)
           ?? await EnsureDataTypeAsync(fallbackName, editorAlias);

    private async Task<IDataType> EnsureDataTypeAsync(string name, string editorAlias, IDictionary<string, object>? configuration = null)
    {
        IDataType? existing = (await _dataTypeService.GetAllAsync()).FirstOrDefault(dataType => dataType.Name == name);
        if (existing is not null)
        {
            return existing;
        }

        if (_propertyEditors.TryGet(editorAlias, out IDataEditor? editor) is false)
        {
            throw new InvalidOperationException($"The property editor {editorAlias} is not available.");
        }

        var dataType = new DataType(editor, _configurationEditorJsonSerializer)
        {
            Name = name,
            DatabaseType = ValueTypes.ToStorageType(editor.GetValueEditor().ValueType),
            ConfigurationData = configuration ?? new Dictionary<string, object>(),
        };

        Attempt<IDataType, DataTypeOperationStatus> result = await _dataTypeService.CreateAsync(dataType, Constants.Security.SuperUserKey);
        return result.Success
            ? result.Result
            : throw new InvalidOperationException($"Could not create data type {name}: {result.Status}");
    }

    private void AddProperty(ContentType contentType, IDataType dataType, string alias, string group = "search")
    {
        ShowdownProperty property = Properties.First(property => property.Alias == alias);
        contentType.AddPropertyType(
            new PropertyType(_shortStringHelper, dataType) { Alias = alias, Name = property.Name },
            group);
    }
}
