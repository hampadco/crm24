using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Validation;

public static class PersianValidationSetup
{
    public static IMvcBuilder AddPersianValidation(this IMvcBuilder builder)
    {
        builder.AddMvcOptions(options =>
        {
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

            options.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(
                name => $"فیلد «{name}» الزامی است.");
            options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor(
                (value, name) => $"مقدار «{value}» برای «{name}» معتبر نیست.");
            options.ModelBindingMessageProvider.SetMissingBindRequiredValueAccessor(
                _ => "یک مقدار الزامی ارسال نشده است.");
            options.ModelBindingMessageProvider.SetMissingKeyOrValueAccessor(
                () => "یک مقدار الزامی ارسال نشده است.");
            options.ModelBindingMessageProvider.SetUnknownValueIsInvalidAccessor(
                name => $"مقدار واردشده برای «{name}» معتبر نیست.");
            options.ModelBindingMessageProvider.SetValueIsInvalidAccessor(
                value => $"مقدار «{value}» معتبر نیست.");
            options.ModelBindingMessageProvider.SetNonPropertyAttemptedValueIsInvalidAccessor(
                value => $"مقدار «{value}» معتبر نیست.");
            options.ModelBindingMessageProvider.SetNonPropertyUnknownValueIsInvalidAccessor(
                () => "مقدار واردشده معتبر نیست.");
            options.ModelBindingMessageProvider.SetNonPropertyValueMustBeANumberAccessor(
                () => "مقدار باید عدد باشد.");
            options.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(
                name => $"فیلد «{name}» باید عدد باشد.");
        });

        return builder;
    }
}
