using System.ComponentModel.DataAnnotations;

namespace CantinaApi.Common.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    // Each step doubles hashing time; tests lower it to keep the suite fast.
    [Range(4, 31)]
    public int BcryptWorkFactor { get; init; } = 12;
}
