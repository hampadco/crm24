using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Services;

/// <summary>سند OpenAPI فقط از ماژول‌ها و فیلدهای مستاجر جاری.</summary>
public class TenantOpenApiBuilder
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly MetadataService _metadata;
    private readonly CrmDbContext _db;

    public TenantOpenApiBuilder(MetadataService metadata, CrmDbContext db)
    {
        _metadata = metadata;
        _db = db;
    }

    public async Task<string> BuildJsonAsync(string tenantName, string origin)
    {
        var modules = (await _metadata.GetActiveModulesAsync())
            .OrderBy(m => m.IsChildModule)
            .ThenBy(m => m.SortOrder)
            .ThenBy(m => m.PluralLabel)
            .ToList();

        var fields = new Dictionary<int, IReadOnlyList<FieldDef>>();
        foreach (var module in modules)
            fields[module.Id] = await _metadata.GetFieldsAsync(module.Id);

        var lineOwners = await _db.FieldBlocks.AsNoTracking()
            .Where(b => b.Kind == BlockKind.LineItems && b.LineModuleName != null && b.LineModuleName != "")
            .Select(b => new { b.ModuleId, b.LineModuleName })
            .ToListAsync();
        var lineByParent = lineOwners
            .GroupBy(x => x.ModuleId)
            .ToDictionary(g => g.Key, g => g.First().LineModuleName!);

        var byName = modules.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        var paths = new JsonObject();
        var schemas = new JsonObject();
        var tags = new JsonArray
        {
            Tag("اتصال", "اطلاعات کلید و فهرست ماژول‌های همین سازمان"),
            Tag("فرم عمومی", "ساختن رکورد از مرورگر با کلید عمومی و دامنهٔ مجاز")
        };

        Add(paths, "/me", "get", Op("me", "اتصال", "وضعیت کلید", "نام، نوع و محدودهٔ کلیدی که در Authorize گذاشته‌اید.", null, null, Ok(SchemaRef("KeyInfo"))));
        Add(paths, "/modules", "get", Op("modules", "اتصال", "ماژول‌های در دسترس کلید", "فقط ماژول‌هایی که این کلید اجازهٔ خواندن یا نوشتن‌شان را دارد.", null, null, Ok(SchemaRef("ModuleList"))));

        foreach (var module in modules)
        {
            var tag = module.PluralLabel;
            tags.Add(Tag(tag, module.IsChildModule
                ? $"ماژول فرزند {module.Name}. داده فقط متعلق به همین سازمان است."
                : $"ماژول {module.Name}. داده فقط متعلق به همین سازمان است."));

            var writable = WritableFields(fields[module.Id]).ToList();
            IReadOnlyList<FieldDef>? lineFields = null;
            if (lineByParent.TryGetValue(module.Id, out var lineName) && byName.TryGetValue(lineName, out var lineModule))
                lineFields = WritableFields(fields[lineModule.Id]).Where(f => !string.Equals(f.LookupModule, module.Name, StringComparison.OrdinalIgnoreCase)).ToList();

            var writeName = SchemaName(module.Name, "Write");
            var recordName = SchemaName(module.Name, "Record");
            var listName = SchemaName(module.Name, "List");
            schemas[writeName] = WriteSchema(module, writable, lineFields);
            schemas[recordName] = RecordSchema(writeName);
            schemas[listName] = ListSchema(recordName);

            var idParams = new JsonArray { IdParam() };
            Add(paths, $"/{module.Name}", "get", Op(
                $"list_{module.Name}", tag, $"فهرست {module.PluralLabel}",
                "جستجو، صفحه‌بندی، مرتب‌سازی و فیلتر. فیلتر ستونی: filter[field][op]=value و op یکی از contains، equals، notequals، startswith، isempty، isnotempty.",
                ListParams(), null, Ok(SchemaRef(listName))));
            Add(paths, $"/{module.Name}", "post", Op(
                $"create_{module.Name}", tag, $"ساختن {module.SingularLabel}",
                "فیلد مخفی نادیده گرفته می‌شود و فیلد محاسباتی پذیرفته نمی‌شود. برای سند، lines سطرها را جایگزین می‌کند و شماره سند اگر خالی باشد ساخته می‌شود.",
                HeaderParams(), Body(writeName), Created(SchemaRef(recordName))));
            Add(paths, $"/{module.Name}/{{id}}", "get", Op(
                $"get_{module.Name}", tag, $"خواندن یک {module.SingularLabel}", null, With(idParams, ExpandParam()), null, Ok(SchemaRef(recordName)), NotFound()));
            Add(paths, $"/{module.Name}/{{id}}", "put", Op(
                $"put_{module.Name}", tag, $"جایگزینی {module.SingularLabel}", null, With(idParams), Body(writeName), Ok(SchemaRef(recordName)), NotFound()));
            Add(paths, $"/{module.Name}/{{id}}", "patch", Op(
                $"patch_{module.Name}", tag, $"ویرایش بخشی از {module.SingularLabel}", "فقط فیلدهای ارسالی عوض می‌شوند.", With(idParams), Body(writeName), Ok(SchemaRef(recordName)), NotFound()));
            Add(paths, $"/{module.Name}/{{id}}", "delete", Op(
                $"delete_{module.Name}", tag, $"حذف {module.SingularLabel}", null, With(idParams), null, new JsonObject { ["204"] = new JsonObject { ["description"] = "حذف شد." } }, NotFound()));
            Add(paths, $"/{module.Name}/upsert", "post", Op(
                $"upsert_{module.Name}", tag, $"همگام‌سازی {module.SingularLabel}",
                "اگر فیلد match پیدا شود رکورد ویرایش می‌شود، وگرنه ساخته می‌شود. برای تلفن شکل‌های مختلف شماره یکی حساب می‌شوند.",
                With(HeaderParams(), MatchParam(writable)), Body(writeName), Ok(SchemaRef("UpsertResult"))));
            Add(paths, $"/{module.Name}/batch", "post", Op(
                $"batch_{module.Name}", tag, $"دستهٔ {module.PluralLabel}", "حداکثر ۱۰۰ مورد. نتیجهٔ هر مورد جداست.", HeaderParams(), BatchBody(writeName), Ok(SchemaRef("BatchResult"))));
            Add(paths, $"/{module.Name}/{{id}}/notes", "post", Op(
                $"note_{module.Name}", tag, $"یادداشت روی {module.SingularLabel}", null, With(idParams), Body("NoteWrite"), Ok(SchemaRef("IdResult")), NotFound()));
            Add(paths, $"/modules/{module.Name}/schema", "get", Op(
                $"schema_{module.Name}", tag, $"ساختار {module.PluralLabel}", "فیلدهای قابل ارسال همین سازمان.", null, null, Ok(SchemaRef("ModuleSchema"))));

            if (module.Name.Equals("leads", StringComparison.OrdinalIgnoreCase))
            {
                Add(paths, "/leads/{id}/convert", "post", Op(
                    "convert_lead", tag, "تبدیل سرنخ", "سرنخ به مخاطب و در صورت نیاز فرصت فروش تبدیل می‌شود.", With(idParams), Body("LeadConvert"), Ok(SchemaRef("LeadConvertResult")), NotFound()));
            }

            if (!module.IsChildModule)
            {
                Add(paths, $"/public/{module.Name}", "post", Op(
                    $"public_{module.Name}", "فرم عمومی", $"فرم عمومی {module.SingularLabel}",
                    "فقط کلید عمومی، فقط از Origin مجاز، و فقط ساختن. پاسخ فقط شناسه است. اگر _hp پر باشد رکورد ساخته نمی‌شود.",
                    null, Body(writeName), Created(SchemaRef("IdResult"))));
            }
        }

        schemas["KeyInfo"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = Props(
                ("name", StringProp("نام کلید")),
                ("kind", StringProp("secret یا public")),
                ("prefix", StringProp("ابتدای کلید")),
                ("scopes", StringProp("محدودهٔ دسترسی")),
                ("canRead", BoolProp()),
                ("canWrite", BoolProp()),
                ("actAsUserId", IntProp("مالک رکوردهای ساخته‌شده")))
        };
        schemas["ModuleList"] = new JsonObject
        {
            ["type"] = "array",
            ["items"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = Props(
                    ("name", StringProp()),
                    ("singularLabel", StringProp()),
                    ("pluralLabel", StringProp()),
                    ("read", BoolProp()),
                    ("write", BoolProp()))
            }
        };
        schemas["ModuleSchema"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = true };
        schemas["NoteWrite"] = new JsonObject
        {
            ["type"] = "object",
            ["required"] = Strings("body"),
            ["properties"] = Props(("body", StringProp("متن یادداشت")))
        };
        schemas["LeadConvert"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = Props(
                ("contactId", IntProp("اگر مخاطب از قبل وجود دارد")),
                ("createOpportunity", BoolProp()))
        };
        schemas["LeadConvertResult"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = Props(
                ("contactId", IntProp()),
                ("organizationId", IntProp()),
                ("opportunityId", IntProp()))
        };
        schemas["IdResult"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = Props(("id", IntProp()))
        };
        schemas["UpsertResult"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = Props(("created", BoolProp()), ("record", new JsonObject { ["type"] = "object" }))
        };
        schemas["BatchResult"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = Props(("results", new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object" } }))
        };

        var doc = new JsonObject
        {
            ["openapi"] = "3.0.3",
            ["info"] = new JsonObject
            {
                ["title"] = $"API سازمان {tenantName}",
                ["version"] = "v1",
                ["description"] = $"""
                    این سند فقط برای سازمان «{tenantName}» ساخته شده است.
                    هر درخواست با کلید همین سازمان اجرا می‌شود و به دادهٔ سازمان دیگری دسترسی ندارد.
                    از صفحهٔ یکپارچگی‌ها یک کلید بسازید، دکمهٔ Authorize را بزنید و مقدار crm_sk_… را در X-Api-Key بگذارید.
                    فیلدهای مخفی و محاسباتی در این سند نیستند. هدر Idempotency-Key از ساخته‌شدن دوبارهٔ رکورد جلوگیری می‌کند.
                    """
            },
            ["servers"] = new JsonArray(new JsonObject { ["url"] = origin.TrimEnd('/') + "/api/v1" }),
            ["tags"] = tags,
            ["paths"] = paths,
            ["components"] = new JsonObject
            {
                ["securitySchemes"] = new JsonObject
                {
                    ["ApiKey"] = new JsonObject
                    {
                        ["type"] = "apiKey",
                        ["in"] = "header",
                        ["name"] = "X-Api-Key",
                        ["description"] = "کلید همین سازمان. کلید محرمانه crm_sk_ و کلید عمومی crm_pk_."
                    }
                },
                ["schemas"] = schemas,
                ["responses"] = new JsonObject
                {
                    ["Unauthorized"] = Plain("کلید نیست یا نامعتبر است."),
                    ["Forbidden"] = Plain("این کلید به این ماژول یا این عملیات دسترسی ندارد."),
                    ["NotFound"] = Plain("رکورد یا ماژول پیدا نشد."),
                    ["Conflict"] = Plain("رکورد تکراری است یا بیش از یک رکورد با فیلد تطبیق پیدا شد."),
                    ["Invalid"] = Plain("داده با ساختار ماژول جور نیست.")
                }
            },
            ["security"] = new JsonArray(new JsonObject { ["ApiKey"] = new JsonArray() })
        };

        return doc.ToJsonString(Json);
    }

    private static IEnumerable<FieldDef> WritableFields(IReadOnlyList<FieldDef> fields) =>
        fields.Where(f => f.IsVisible && string.IsNullOrWhiteSpace(f.FormulaExpression));

    private static JsonObject WriteSchema(ModuleDef module, IReadOnlyList<FieldDef> fields, IReadOnlyList<FieldDef>? lineFields)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var field in fields)
        {
            props[field.Name] = FieldSchema(field);
            if (field.IsRequired)
                required.Add(field.Name);
        }

        if (lineFields is { Count: > 0 })
        {
            var lineProps = new JsonObject();
            foreach (var field in lineFields)
                lineProps[field.Name] = FieldSchema(field);
            props["lines"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "سطرهای سند. با هر ارسال، سطرهای قبلی همین سند جایگزین می‌شوند.",
                ["items"] = new JsonObject { ["type"] = "object", ["properties"] = lineProps }
            };
        }
        else if (module.DocumentKind != DocumentKind.None)
        {
            props["lines"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "سطرهای سند.",
                ["items"] = new JsonObject { ["type"] = "object" }
            };
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props
        };
        if (required.Count > 0)
            schema["required"] = required;
        return schema;
    }

    private static JsonObject FieldSchema(FieldDef field)
    {
        var schema = field.Type switch
        {
            FieldType.Number => IntProp(field.Label),
            FieldType.Decimal or FieldType.Currency or FieldType.Percent => new JsonObject { ["type"] = "number", ["description"] = field.Label },
            FieldType.Checkbox => new JsonObject { ["type"] = "boolean", ["description"] = field.Label },
            FieldType.Date => new JsonObject { ["type"] = "string", ["format"] = "date", ["description"] = field.Label },
            FieldType.DateTime => new JsonObject { ["type"] = "string", ["format"] = "date-time", ["description"] = field.Label },
            _ => StringProp(field.Label)
        };

        if (field.Type == FieldType.Picklist && field.PicklistValues.Count > 0)
        {
            var values = new JsonArray();
            foreach (var option in field.PicklistValues.Where(p => p.IsActive).OrderBy(p => p.SortOrder))
                values.Add(option.Value);
            if (values.Count > 0)
                schema["enum"] = values;
            schema["description"] = field.Label + " — " + string.Join("، ", field.PicklistValues.Where(p => p.IsActive).OrderBy(p => p.SortOrder).Select(p => $"{p.Label} ({p.Value})"));
        }

        if (field.Type == FieldType.MultiPicklist)
            schema["description"] = field.Label + " — مقدارها را با ویرگول جدا کنید.";
        if (field.Type == FieldType.Phone)
            schema["description"] = field.Label + " — چند شماره را با خط جدید جدا کنید.";
        if (field.Type == FieldType.Lookup && !string.IsNullOrWhiteSpace(field.LookupModule))
            schema["description"] = $"{field.Label} — شناسهٔ عددی رکورد در ماژول {field.LookupModule}.";
        if (field.IsUniqueCheck)
            schema["description"] = (schema["description"]?.GetValue<string>() ?? field.Label) + " یکتا است.";
        if (field.MaxLength is int max && max > 0 && schema["type"]?.GetValue<string>() == "string")
            schema["maxLength"] = max;
        return schema;
    }

    private static JsonObject RecordSchema(string writeSchema) => new()
    {
        ["type"] = "object",
        ["properties"] = Props(
            ("id", IntProp()),
            ("title", StringProp()),
            ("ownerUserId", IntProp()),
            ("createdAtUtc", new JsonObject { ["type"] = "string", ["format"] = "date-time" }),
            ("updatedAtUtc", new JsonObject { ["type"] = "string", ["format"] = "date-time" }),
            ("data", SchemaRef(writeSchema)),
            ("lookups", new JsonObject { ["type"] = "object", ["description"] = "وقتی expand=lookups باشد، عنوان رکوردهای مرتبط." }))
    };

    private static JsonObject ListSchema(string recordSchema) => new()
    {
        ["type"] = "object",
        ["properties"] = Props(
            ("total", IntProp()),
            ("page", IntProp()),
            ("pageSize", IntProp()),
            ("items", new JsonObject { ["type"] = "array", ["items"] = SchemaRef(recordSchema) }))
    };

    private static void Add(JsonObject paths, string path, string method, JsonObject operation)
    {
        if (paths[path] is not JsonObject item)
        {
            item = new JsonObject();
            paths[path] = item;
        }

        item[method] = operation;
    }

    private static JsonObject Op(string operationId, string tag, string summary, string? description, JsonArray? parameters, JsonObject? body, params JsonObject[] responses)
    {
        var response = new JsonObject();
        foreach (var item in responses)
        {
            foreach (var pair in item)
                response[pair.Key] = pair.Value?.DeepClone();
        }

        Ensure(response, "401", "Unauthorized");
        Ensure(response, "403", "Forbidden");
        Ensure(response, "422", "Invalid");

        var op = new JsonObject
        {
            ["operationId"] = operationId,
            ["tags"] = Strings(tag),
            ["summary"] = summary,
            ["responses"] = response
        };
        if (!string.IsNullOrWhiteSpace(description))
            op["description"] = description;
        if (body is not null)
            Ensure(response, "409", "Conflict");
        if (parameters is { Count: > 0 })
            op["parameters"] = parameters;
        if (body is not null)
            op["requestBody"] = body;
        return op;
    }

    private static void Ensure(JsonObject responses, string code, string name)
    {
        if (responses[code] is null)
            responses[code] = new JsonObject { ["$ref"] = "#/components/responses/" + name };
    }

    private static JsonObject Ok(JsonObject schema) => One("200", "موفق", schema);
    private static JsonObject Created(JsonObject schema) => One("201", "ساخته شد", schema);

    private static JsonObject One(string code, string description, JsonObject schema) => new()
    {
        [code] = new JsonObject
        {
            ["description"] = description,
            ["content"] = new JsonObject
            {
                ["application/json"] = new JsonObject { ["schema"] = schema }
            }
        }
    };

    private static JsonObject NotFound() => new()
    {
        ["404"] = new JsonObject { ["$ref"] = "#/components/responses/NotFound" },
        ["409"] = new JsonObject { ["$ref"] = "#/components/responses/Conflict" }
    };

    private static JsonObject Plain(string description) => new() { ["description"] = description };

    private static JsonObject Body(string schemaName) => new()
    {
        ["required"] = true,
        ["content"] = new JsonObject
        {
            ["application/json"] = new JsonObject { ["schema"] = SchemaRef(schemaName) }
        }
    };

    private static JsonObject BatchBody(string itemSchema) => new()
    {
        ["required"] = true,
        ["content"] = new JsonObject
        {
            ["application/json"] = new JsonObject
            {
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = Strings("items"),
                    ["properties"] = Props(("items", new JsonObject
                    {
                        ["type"] = "array",
                        ["maxItems"] = 100,
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = Props(
                                ("match", StringProp("نام فیلد تطبیق، مثلاً email یا sku. خالی یعنی ساختن.")),
                                ("values", SchemaRef(itemSchema)))
                        }
                    }))
                }
            }
        }
    };

    private static JsonObject SchemaRef(string name) => new() { ["$ref"] = "#/components/schemas/" + name };

    private static JsonArray ListParams()
    {
        var items = HeaderParams();
        items.Add(Query("search", "string", "جستجو در عنوان و داده‌ها"));
        items.Add(Query("page", "integer", "شماره صفحه"));
        items.Add(Query("pageSize", "integer", "تعداد در هر صفحه، حداکثر ۲۰۰"));
        items.Add(Query("sort", "string", "نام فیلد مرتب‌سازی"));
        items.Add(Query("dir", "string", "asc یا desc"));
        items.Add(Query("updatedSince", "string", "فقط رکوردهای تغییرکرده بعد از این زمان، ISO 8601"));
        items.Add(ExpandParam());
        return items;
    }

    private static JsonArray HeaderParams()
    {
        var items = new JsonArray();
        items.Add(new JsonObject
        {
            ["name"] = "Idempotency-Key",
            ["in"] = "header",
            ["required"] = false,
            ["description"] = "اگر همین مقدار دوباره بیاید، رکورد دوباره ساخته نمی‌شود.",
            ["schema"] = new JsonObject { ["type"] = "string" }
        });
        return items;
    }

    private static JsonObject ExpandParam() => Query("expand", "string", "lookups برای آوردن عنوان رابطه‌ها");

    private static JsonObject IdParam() => new()
    {
        ["name"] = "id",
        ["in"] = "path",
        ["required"] = true,
        ["schema"] = new JsonObject { ["type"] = "integer" }
    };

    private static JsonObject MatchParam(IReadOnlyList<FieldDef> fields)
    {
        var schema = new JsonObject { ["type"] = "string" };
        var names = fields.Where(f => f.IsUniqueCheck || f.Type is FieldType.Email or FieldType.Phone || f.Name is "sku" or "email" or "phone" or "slug")
            .Select(f => f.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count > 0)
        {
            var en = new JsonArray();
            foreach (var name in names)
                en.Add(name);
            schema["enum"] = en;
        }

        return new JsonObject
        {
            ["name"] = "match",
            ["in"] = "query",
            ["required"] = true,
            ["description"] = "فیلدی که رکورد موجود با آن پیدا می‌شود.",
            ["schema"] = schema
        };
    }

    private static JsonObject Query(string name, string type, string description) => new()
    {
        ["name"] = name,
        ["in"] = "query",
        ["required"] = false,
        ["description"] = description,
        ["schema"] = new JsonObject { ["type"] = type }
    };

    private static JsonArray With(JsonArray first, params JsonObject[] more)
    {
        var clone = (JsonArray)first.DeepClone();
        foreach (var item in more)
            clone.Add(item);
        return clone;
    }

    private static JsonObject Tag(string name, string description) => new()
    {
        ["name"] = name,
        ["description"] = description
    };

    private static string SchemaName(string module, string suffix)
    {
        var parts = module.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var pascal = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return pascal + suffix;
    }

    private static JsonObject Props(params (string Name, JsonObject Schema)[] fields)
    {
        var props = new JsonObject();
        foreach (var (name, schema) in fields)
            props[name] = schema;
        return props;
    }

    private static JsonArray Strings(params string[] values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }

    private static JsonObject StringProp(string? description = null)
    {
        var schema = new JsonObject { ["type"] = "string" };
        if (!string.IsNullOrWhiteSpace(description))
            schema["description"] = description;
        return schema;
    }

    private static JsonObject IntProp(string? description = null)
    {
        var schema = new JsonObject { ["type"] = "integer" };
        if (!string.IsNullOrWhiteSpace(description))
            schema["description"] = description;
        return schema;
    }

    private static JsonObject BoolProp() => new() { ["type"] = "boolean" };
}
