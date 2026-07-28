# Module Customization Studio

استودیوی سفارشی‌سازی ماژول برای مدیران Tenant: چیدمان فیلدها در بلاک‌ها، افزودن فیلد سفارشی، روابط، تشخیص تکراری، و وابستگی نمایش فیلد/بلاک.

## دسترسی

- فقط `IsTenantAdmin`
- منو: **تنظیمات → سفارشی‌سازی ماژول** (`/App/customize`)

## موجودیت‌ها

| موجودیت | نقش |
|---------|-----|
| `FieldBlock` | گروه/بخش چیدمان فیلدهای یک ماژول |
| `FieldDef.BlockId` | تعلق فیلد به بلاک (اختیاری) |
| `FieldDef.MaxLength` / `IsVisible` / `VisibilityRuleJson` | محدودیت و نمایش شرطی |
| `ModuleDef.DuplicateMatchMode` | `or` / `and` برای تشخیص تکراری |
| `RelationDef.LinkFieldName` | Lookup روی مقصد برای لیست مرتبط |

Tenantهای قدیمی بدون بلاک کار می‌کنند؛ فرم فیلدها را flat نشان می‌دهد. Tenantهای جدید هنگام seed بلاک پیش‌فرض «اطلاعات اصلی» می‌گیرند.

## API سرویس (`MetadataService`)

- `GetBlocksAsync` / `CreateBlockAsync` / `UpdateBlockAsync` / `DeleteBlockAsync`
- `CreateFieldAsync` (فقط فیلد سفارشی) / `UpdateFieldAsync`
- `ReorderLayoutAsync(moduleId, layout)` — ترتیب بلاک و فیلدها
- `GetRelationsForModuleAsync` / `CreateRelationAsync` / `DeleteRelationAsync`
- `UpdateModuleDuplicateModeAsync`

پس از هر نوشتن، کش فیلد/بلاک (و در صورت نیاز ماژول) invalidate می‌شود.

## UI استودیو

`/App/customize/{moduleName}` با تب‌ها:

1. **چیدمان** (پیش‌فرض) — SortableJS برای کشیدن فیلد بین بلاک‌ها + ذخیره JSON؛ دکمه «شرط نمایش» برای هر بلاک
2. **فیلدها** — لیست و ویرایش ویژگی‌ها + شرط نمایش (field / op / value → `VisibilityRuleJson`)
3. **روابط** — لیست `RelationDef`، افزودن (مقصد، برچسب، many-to-many، لینک Lookup)
4. **تکراری‌ها** — حالت OR/AND + چک‌لیست فیلدهای `IsUniqueCheck`

## روابط (Phase D)

- رابطه بین دو ماژول با برچسب و اختیاری `LinkFieldName`
- در جزئیات رکورد، گروه‌های مرتبط برچسب `RelationDef` را می‌گیرند؛ اگر `LinkFieldName` ست باشد، لیست مرتبط از آن Lookup روی ماژول مقصد خوانده می‌شود

## تشخیص تکراری (Phase D)

- `DuplicateMatchMode = or` (پیش‌فرض): هر فیلد `IsUniqueCheck` جداگانه
- `and`: فقط وقتی **همه** فیلدهای یکتا مقدار دارند و ترکیبشان روی یک رکورد موجود منطبق است

## وابستگی نمایش (Phase D)

- JSON: `{"field":"stage","op":"eq","value":"Closed Won"}` — عملگرها: `eq`, `neq`, `contains`
- روی فیلد و بلاک (`VisibilityRuleJson`)
- فرم رکورد: کلاس `crm-dep-target` + `data-visibility-rule`؛ اسکریپت `wwwroot/js/crm-form-deps.js`

## فرم‌های رکورد

`_DynamicForm` فیلدها را با `Block.SortOrder` سپس `Field.SortOrder` گروه‌بندی می‌کند؛ در نبود بلاک، رفتار قبلی حفظ می‌شود.

## Migration

```
ModuleStudioFieldBlocks
ModuleStudioPhaseD
```

اعمال:

```bash
dotnet ef database update --project src/Crm.Infrastructure --startup-project src/Crm.Web --context CrmDbContext
```
