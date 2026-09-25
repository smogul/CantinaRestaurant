using System.ComponentModel.DataAnnotations;

namespace CantinaApi.Common;

// [Url] also accepts ftp, so this narrows it to absolute http and https links.
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class HttpUrlAttribute() : ValidationAttribute("The {0} field must be an absolute http or https URL.")
{
    public override bool IsValid(object? value) =>
        value is null
        || (value is string text
            && Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
}
