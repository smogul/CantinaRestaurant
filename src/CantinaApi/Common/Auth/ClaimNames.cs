namespace CantinaApi.Common.Auth;

// Short JWT claim names; inbound mapping to the long .NET claim types is turned off.
public static class ClaimNames
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string TokenId = "jti";
}
